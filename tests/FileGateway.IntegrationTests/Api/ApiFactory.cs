using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Api.Options;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Api;

/// <summary>API 통합테스트 공통 factory: 고정 snapshot 뷰 + 교체 가능 IFileAccess(기본: 호출되면 실패).
/// 실제 호스트는 지연 생성되는 InnerFactory가 담당하며, RestartApplication으로 재시작을 시뮬레이션할 수 있다.</summary>
public sealed class ApiFactory : IAsyncLifetime, IDisposable
{
    // 기본 시각: 시드 파일(2026-08-22 KST)이 기본 24h 조회 범위에 들어오도록 고정.
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 8, 23, 3, 0, 0, TimeSpan.Zero));
    private readonly SwitchableFileAccess _fileAccess = new(new ThrowingFileAccess());
    private readonly FixedSnapshotView _view = new(null);

    public ApiFactory() { }

    internal ApiFactory(Action<IServiceCollection>? extraServices) => _extraServices = extraServices;

    private readonly Action<IServiceCollection>? _extraServices;
    private string? _dataProtectionKeyDir;
    private InnerFactory? _host;

    /// <summary>xUnit 클래스 fixture 정리 시점에 실제 호스트까지 dispose한다.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }
    }

    /// <summary>테스트 본문에서 using으로 쓰는 국소 인스턴스용 동기 정리(RestartApplication과 동일 방식).</summary>
    public void Dispose()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host = null;
    }


    /// <summary>DataProtection 키 저장 디렉터리 지정(첫 호스트 생성 전에만 유효).</summary>
    public void SetDataProtectionKeyDirectory(string keyDir) => _dataProtectionKeyDir = keyDir;

    /// <summary>재시작 시뮬레이션: 기존 호스트 dispose 후 동일 keyDir로 재구성.</summary>
    public void RestartApplication(string keyDir)
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host = null;
        _dataProtectionKeyDir = keyDir;
    }

    private InnerFactory Host => _host ??= new InnerFactory(this);

    public void SetSnapshot(ReferenceDataSnapshot? snapshot) => _view.SetSnapshot(snapshot);

    public void SetFileAccess(IFileAccess inner) => _fileAccess.Inner = inner;

    /// <summary>테스트가 시드/변이 가능한 FakeFileAccess를 IFileAccess로 등록한다(호출마다 새 인스턴스).</summary>
    public void UseFakeFtp(Action<FakeFileAccess>? seed = null)
    {
        var ftp = new FakeFileAccess();
        seed?.Invoke(ftp);
        SetFileAccess(ftp);
        Ftp = ftp;
    }

    /// <summary>UseFakeFtp로 등록한 마지막 FakeFileAccess. 등록 전 접근은 의미 없다.</summary>
    public FakeFileAccess Ftp { get; private set; } = new();

    public HttpClient CreateClient() // 기본 "test-key" 헤더
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return client;
    }

    /// <summary>로그 목록 조회로 fileId 하나를 발급받는다(list → items[0].fileId).</summary>
    public async Task<string> IssueFileIdAsync()
    {
        var body = await CreateClient().GetFromJsonAsync<JsonElement>(
            "/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        return body.GetProperty("items")[0].GetProperty("fileId").GetString()!;
    }

    /// <summary>파일 metadata 조회의 오류 응답(code, status)을 반환한다. 성공(200)이면 code는 빈 문자열.</summary>
    public async Task<(string code, int status)> GetFileErrorAsync(string fileId)
    {
        using var response = await CreateClient()
            .GetAsync($"/api/v1/files?fileId={Uri.EscapeDataString(fileId)}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.TryGetProperty("code", out var c) ? c.GetString()! : "";
        return (code, (int)response.StatusCode);
    }

    /// <summary>실제 호스트 빌더: outer factory의 공유 상태(snapshot 뷰/IFileAccess/시계)를 그대로 주입한다.</summary>
    private sealed class InnerFactory(ApiFactory owner) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            if (owner._dataProtectionKeyDir is { } keyDir)
                builder.UseSetting("DataProtection:KeyDirectory", keyDir);
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
                services.AddSingleton<IReferenceDataView>(owner._view);
                services.AddSingleton<IFileAccess>(owner._fileAccess);
                owner._extraServices?.Invoke(services);
                services.AddSingleton<TimeProvider>(owner._clock);
            });
        }
    }

    /// <summary>테스트가 IFileAccess 구현을 교체할 수 있게 하는 위임 래퍼(호스트 빌드 후에도 교체 가능).</summary>
    private sealed class SwitchableFileAccess(IFileAccess inner) : IFileAccess
    {
        public IFileAccess Inner = inner;

        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct)
            => Inner.ListFilesAsync(server, relativeDirectory, ct);

        public Task<RemoteDirectoryNames> ListDirectoriesAsync(
            FileServerConnection server, string relativeDirectory, CancellationToken ct)
            => Inner.ListDirectoriesAsync(server, relativeDirectory, ct);

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
