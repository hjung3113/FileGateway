using FileGateway.Api.Options;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Api;

/// <summary>API 통합테스트 공통 factory: 고정 snapshot 뷰 + 호출되면 실패하는 IFileAccess(FTP 접근 없음 구조적 검증).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly FixedSnapshotView _view = new(null);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.Configure<AuthenticationOptions>(o => o.ApiKeys =
                [new() { Key = "test-key", CallerId = "caller-1" }]);
            services.AddSingleton<IReferenceDataView>(_view);
            services.AddSingleton<IFileAccess>(new ThrowingFileAccess());
        });
    }

    public void SetSnapshot(ReferenceDataSnapshot? snapshot) => _view.SetSnapshot(snapshot);

    public new HttpClient CreateClient() // 기본 "test-key" 헤더
    {
        var client = base.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return client;
    }
}
