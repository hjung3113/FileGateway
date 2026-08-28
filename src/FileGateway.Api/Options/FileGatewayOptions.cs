// src/FileGateway.Api/Options/FileGatewayOptions.cs
using FileGateway.Infrastructure.Ftp;

namespace FileGateway.Api.Options;

/// <summary>"FileGateway" 섹션. 기본값은 코드에 두고 config로 덮어쓴다.</summary>
public sealed class FileGatewayOptions
{
    public LogsOptions Logs { get; set; } = new();
    public ConfigurationsOptions Configurations { get; set; } = new();
    public PagingOptions Paging { get; set; } = new();
    public TokensOptions Tokens { get; set; } = new();
    public ReferenceDataOptions ReferenceData { get; set; } = new();
    public FtpOptions? Ftp { get; set; }
    public DevToolsOptions DevTools { get; set; } = new();
}

public sealed class LogsOptions
{
    public TimeSpan MaxQueryRange { get; set; } = TimeSpan.FromDays(31);
}

public sealed class ConfigurationsOptions
{
    public TimeSpan HistoryMaxQueryRange { get; set; } = TimeSpan.FromDays(366);
}

public sealed class PagingOptions
{
    public int LimitDefault { get; set; } = 100;
    public int LimitMax { get; set; } = 1000;
}

public sealed class TokensOptions
{
    public TimeSpan FileIdTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ContinuationTtl { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class ReferenceDataOptions
{
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Development 환경이 아니어도 /tester, /scalar/v1를 opt-in으로 열기 위한 설정.</summary>
public sealed class DevToolsOptions
{
    public bool Enabled { get; set; }
}
