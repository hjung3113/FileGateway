using FluentFTP;

namespace FileGateway.Infrastructure.Ftp;

public enum FtpSecurity { Plain, ExplicitTls, ImplicitTls }

public sealed class FtpOptions
{
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public FtpSecurity Security { get; set; } = FtpSecurity.Plain;
    public bool AcceptUntrustedCertificates { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int ReadTimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentGlobal { get; set; } = 50;
    public int MaxConcurrentPerServer { get; set; } = 5;
    public int? HostPortOverride { get; set; } // 테스트 편의용

    internal static int ResolveHostPort(FtpOptions o)
        => o.HostPortOverride ?? (o.Security == FtpSecurity.ImplicitTls ? 990 : 21);

    public static FtpConfig ToFtpConfig(FtpOptions o) => new()
    {
        ConnectTimeout = o.ConnectTimeoutSeconds * 1000,
        ReadTimeout = o.ReadTimeoutSeconds * 1000,
        DataConnectionConnectTimeout = o.ConnectTimeoutSeconds * 1000,
        DataConnectionReadTimeout = o.ReadTimeoutSeconds * 1000,
        EncryptionMode = o.Security switch
        {
            FtpSecurity.ExplicitTls => FtpEncryptionMode.Explicit,
            FtpSecurity.ImplicitTls => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None,
        },
        ValidateAnyCertificate = o.AcceptUntrustedCertificates, // self-signed 내부 서버 허용 여부(운영 설정)
    };
}
