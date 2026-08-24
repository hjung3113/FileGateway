using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Api.Options;
using FileGateway.Infrastructure.ReferenceData;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileGateway.IntegrationTests.Api;

public class ApiBootstrapTests
{
    private sealed class Factory(Action<IServiceCollection>? configure = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.Configure<AuthenticationOptions>(o => o.ApiKeys =
                    [new() { Key = "test-key", CallerId = "caller-1" }]);
                configure?.Invoke(services);
            });
        }
    }

    private sealed class CountingSource(ReferenceDataSnapshot snapshot) : IReferenceDataSource
    {
        public ReferenceDataSnapshot Snapshot { get; } = snapshot;
        public int Calls;
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
        { Calls++; return Task.FromResult(new ReferenceDataRaw(["EQ-001"], [], [], [])); }
    }

    private static Factory FactoryWithSnapshot(bool withUsableSnapshot)
        => new(services =>
        {
            var view = new FixedSnapshotView(withUsableSnapshot
                ? ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], []))
                : null);
            services.AddSingleton<IReferenceDataView>(view);
        });

    [Fact]
    public async Task Missing_api_key_is_401_InvalidApiKey()
    {
        using var factory = FactoryWithSnapshot(true);
        var response = await factory.CreateClient().GetAsync("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(401, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidApiKey", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Wrong_api_key_is_401()
    {
        using var factory = FactoryWithSnapshot(true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(401, (int)(await client.GetAsync("/api/v1/equipments/EQ-001/file-types")).StatusCode);
    }

    [Fact]
    public async Task Api_key_in_query_string_is_not_accepted()
    {
        using var factory = FactoryWithSnapshot(true);
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/equipments/EQ-001/file-types?X-Api-Key=test-key");
        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Health_live_is_ok_even_without_reference_data()
    {
        using var factory = FactoryWithSnapshot(false);
        Assert.Equal(200, (int)(await factory.CreateClient().GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Health_ready_triggers_initial_load_and_fails_when_unavailable()
    {
        // ready는 최초 기준정보 로딩을 실제로 유발한다(확정 결정 14). FixedSnapshotView(null)은
        // GetSnapshotAsync에서 ReferenceDataUnavailable을 throw하므로 ready는 503이고,
        // 실 ReferenceDataCache라면 ready 호출 시점에 source가 1회 호출된다.
        using var noData = FactoryWithSnapshot(false);
        Assert.Equal(503, (int)(await noData.CreateClient().GetAsync("/health/ready")).StatusCode);

        using var withData = FactoryWithSnapshot(true);
        Assert.Equal(200, (int)(await withData.CreateClient().GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Health_ready_induces_single_initial_load_on_real_cache()
    {
        var source = new CountingSource(ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], [])));
        using var factory = new Factory(services =>
        {
            services.AddSingleton<IReferenceDataSource>(source);
            services.AddSingleton<IReferenceDataView>(sp => new ReferenceDataCache(
                sp.GetRequiredService<IReferenceDataSource>(), TimeSpan.FromMinutes(15)));
        });

        var client = factory.CreateClient();
        Assert.Equal(200, (int)(await client.GetAsync("/health/ready")).StatusCode);   // 최초 로딩 유발
        Assert.Equal(1, source.Calls);                                                 // 로딩 실제 실행(단 1회)
        await client.GetAsync("/health/ready");                                        // TTL 내 재호출
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Audit_log_records_failed_request_with_status_and_error_code()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new Factory(s => s.AddSingleton<ILoggerProvider>(logs));
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/equipments/EQ-001/file-types"); // API Key 누락 → 401
        Assert.Equal(401, (int)response.StatusCode);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        Assert.Contains("401", entry.Message);                    // 최종 status
        Assert.Contains("InvalidApiKey", entry.Message);           // 안정적 오류 분류
    }


    [Fact]
    public async Task Reference_data_unavailable_maps_to_503_problem_details()
    {
        using var factory = FactoryWithSnapshot(false); // GetSnapshotAsync가 ReferenceDataUnavailable throw
        factory.CreateClient().DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        var response = await factory.CreateClient()
            .SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/equipments/EQ-001/file-types")
                { Headers = { { "X-Api-Key", "test-key" } } });
        Assert.Equal(503, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ReferenceDataUnavailable", body.GetProperty("code").GetString());
        Assert.NotNull(body.GetProperty("traceId").GetString());
    }

    [Fact]
    public void MaxQueryRange_below_two_days_fails_startup()
    {
        using var factory = new Factory(s => s.PostConfigure<FileGatewayOptions>(
            o => o.Logs.MaxQueryRange = TimeSpan.FromDays(1)));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public void Missing_key_directory_fails_startup_outside_development()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("DataProtection:KeyDirectory", ex.Message);
    }

    [Fact]
    public void Appsettings_file_binds_FileIdTtl_to_exactly_24_hours()
    {
        // 회귀: "24:00:00"은 TimeSpan 파서에서 24일(d.hh:mm:ss 해석)로 파싱된다.
        // 실제 appsettings.json을 AddJsonFile으로 로드해 모호한 문자열 재발을 잡는다.
        var appsettings = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FileGateway.Api", "appsettings.json"));
        var config = new ConfigurationBuilder().AddJsonFile(appsettings).Build();
        var options = config.GetSection("FileGateway").Get<FileGatewayOptions>()!;
        Assert.Equal(TimeSpan.FromHours(24), options.Tokens.FileIdTtl);
    }

    [Fact]
    public async Task Audit_log_contains_caller_and_endpoint_without_key()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new Factory(s =>
        {
            s.AddSingleton<ILoggerProvider>(logs);
            var view = new FixedSnapshotView(ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], [])));
            s.AddSingleton<IReferenceDataView>(view);
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/equipments/EQ-001/file-types");
        request.Headers.Add("X-Api-Key", "test-key");
        await client.SendAsync(request);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        Assert.Contains("caller-1", entry.Message);
        Assert.DoesNotContain("test-key", entry.Message);
    }
}
