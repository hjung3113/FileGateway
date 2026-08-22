using System.Security.Claims;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.FileSystem;
using FubarDev.FtpServer.FileSystem.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Ftp;

public sealed class FtpAdapterFixture : IAsyncLifetime
{
    public const string UserName = "fgtest", Password = "fgpass";
    private readonly ServiceCollection _services = new();
    private ServiceProvider? _provider;
    private IFtpServerHost? _server;

    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        Port = 21000 + Random.Shared.Next(0, 2000);
        _services.AddFtpServer(sb => sb
            .UseSingleRoot(o => o.RootPath = "/"));
        // 기본 UseInMemoryFileSystem 등록은 로그인(연결)마다 새 in-memory FS를 만들어 시드가 다른 연결에 안 보인다.
        // 모든 연결이 하나의 in-memory FS를 공유하도록 팩토리를 직접 등록한다.
        _services.AddSingleton<IFileSystemClassFactory>(new SharedInMemoryFileSystemFactory());
        _services.AddSingleton<IMembershipProvider>(new DictionaryMembershipProvider(
            new Dictionary<string, string> { [UserName] = Password }));
        _services.Configure<FtpServerOptions>(o =>
        {
            o.ServerAddress = "127.0.0.1";
            o.Port = Port;
        });
        _provider = _services.BuildServiceProvider();
        _server = _provider.GetRequiredService<IFtpServerHost>();
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null) await _server.StopAsync(CancellationToken.None);
        _provider?.Dispose();
    }

    private sealed class SharedInMemoryFileSystemFactory : IFileSystemClassFactory
    {
        private readonly InMemoryFileSystem _fileSystem = new(StringComparer.OrdinalIgnoreCase);

        public Task<IUnixFileSystem> Create(IAccountInformation accountInformation)
            => Task.FromResult<IUnixFileSystem>(_fileSystem);
    }

    /// <summary>DictionaryMembershipProvider/UseCustomMembership가 이 패키지 버전에 없어 직접 구현한 멤버십.</summary>
    private sealed class DictionaryMembershipProvider(Dictionary<string, string> users) : IMembershipProvider
    {
        public Task<MemberValidationResult> ValidateUserAsync(string username, string password)
        {
            if (users.TryGetValue(username, out var expected) && expected == password)
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimsIdentity.DefaultNameClaimType, username)], "password"));
                return Task.FromResult(
                    new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, principal));
            }

            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }
    }
}
