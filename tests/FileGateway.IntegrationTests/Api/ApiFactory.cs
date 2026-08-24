using FileGateway.Api.Options;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Api;

/// <summary>API 통합테스트 공통 factory: 고정 snapshot 뷰 + 교체 가능 IFileAccess(기본: 호출되면 실패).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // 기본 시각: 시드 파일(2026-08-22 KST)이 기본 24h 조회 범위에 들어오도록 고정.
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 8, 23, 3, 0, 0, TimeSpan.Zero));
    private readonly SwitchableFileAccess _fileAccess = new(new ThrowingFileAccess());
    private readonly FixedSnapshotView _view = new(null);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.Configure<FileGatewayOptions>(o =>
            {
                // 고정 시계로 발급되는 토큰이 실제 시계 기준 검증에서 만료로 보이지 않게 한다.
                o.Tokens.FileIdTtl = TimeSpan.FromDays(365);
                o.Tokens.ContinuationTtl = TimeSpan.FromDays(365);
            });
            services.Configure<AuthenticationOptions>(o => o.ApiKeys =
                [new() { Key = "test-key", CallerId = "caller-1" }]);
            services.AddSingleton<IReferenceDataView>(_view);
            services.AddSingleton<IFileAccess>(_fileAccess);
            services.AddSingleton<TimeProvider>(_clock);
        });
    }

    public void SetSnapshot(ReferenceDataSnapshot? snapshot) => _view.SetSnapshot(snapshot);

    public void SetFileAccess(IFileAccess inner) => _fileAccess.Inner = inner;

    public new HttpClient CreateClient() // 기본 "test-key" 헤더
    {
        var client = base.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return client;
    }

    /// <summary>테스트가 IFileAccess 구현을 교체할 수 있게 하는 위임 래퍼(호스트 빌드 후에도 교체 가능).</summary>
    private sealed class SwitchableFileAccess(IFileAccess inner) : IFileAccess
    {
        public IFileAccess Inner = inner;

        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct)
            => Inner.ListFilesAsync(server, relativeDirectory, ct);

        public Task<long> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct)
            => Inner.StatFileAsync(server, relativePath, ct);

        public Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct)
            => Inner.FileExistsAsync(server, relativePath, ct);

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct)
            => Inner.OpenReadAsync(server, relativePath, ct);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
