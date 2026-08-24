using FileGateway.Api.Audit;
using Microsoft.AspNetCore.DataProtection;
using FileGateway.Api.Auth;
using FileGateway.Api.Endpoints;
using FileGateway.Api.Errors;
using FileGateway.Api.Options;
using FileGateway.Configurations;
using FileGateway.Configurations.Internal;
using FileGateway.Core.Files;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.Ftp;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Infrastructure.Tokens;
using FileGateway.Logs;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FileGatewayOptions>(builder.Configuration.GetSection("FileGateway"));
builder.Services.AddOptions<FileGatewayOptions>()
    .Validate(o => o.Logs.MaxQueryRange >= TimeSpan.FromDays(2), "Logs.MaxQueryRange must be >= 2 days")
    .Validate(o => o.Configurations.HistoryMaxQueryRange > TimeSpan.Zero, "HistoryMaxQueryRange must be positive")
    .ValidateOnStart();
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));

// DataProtection 키 내구성: 미설정 시 기본 키 링은 프로세스/IIS App Pool 재시작과 무관하지 않으나
// 배포 환경(프로필 없는 App Pool identity)에서는 휘발성이므로 명시적 디렉터리를 요구한다.
var keyDir = builder.Configuration["DataProtection:KeyDirectory"];
var dp = builder.Services.AddDataProtection(o => o.ApplicationDiscriminator = "FileGateway");
if (!string.IsNullOrEmpty(keyDir))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(keyDir));
    if (OperatingSystem.IsWindows())
        dp.ProtectKeysWithDpapi(protectToLocalMachine: true); // App Pool 서비스 계정(프로필 없는 local machine 범위)으로 복호화 제한
}
builder.Services.AddSingleton<ITokenCodec, DataProtectionTokenCodec>();
builder.Services.AddSingleton<FtpConcurrencyLimiter>();
builder.Services.AddSingleton<FtpOptions>(sp => sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.Ftp
    ?? throw new InvalidOperationException("Ftp options required"));
builder.Services.AddSingleton<IFileAccess, FtpFileAccess>(); // 싱글톤 서비스에만 주입되어 사실상 단일 인스턴스 — FtpFileAccess는 상태 없이 안전
builder.Services.AddSingleton<IReferenceDataSource>(sp => new SpReferenceDataSource(
    builder.Configuration.GetConnectionString("ReferenceData")
    ?? throw new InvalidOperationException("ReferenceData connection string required")));
builder.Services.AddSingleton<IReferenceDataView>(sp => new ReferenceDataCache(
    sp.GetRequiredService<IReferenceDataSource>(),
    sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.ReferenceData.CacheTtl));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILogQueryService>(sp =>
{
    var o = sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value;
    return new LogQueryService(
        sp.GetRequiredService<IReferenceDataView>(),
        sp.GetRequiredService<IFileAccess>(),
        sp.GetRequiredService<ITokenCodec>(),
        sp.GetRequiredService<TimeProvider>(),
        o.Logs.MaxQueryRange, o.Paging.LimitDefault, o.Paging.LimitMax,
        o.Tokens.FileIdTtl, o.Tokens.ContinuationTtl);
});
builder.Services.AddSingleton<IConfigurationQueryService>(sp =>
{
    var o = sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value;
    return new ConfigurationQueryService(
        sp.GetRequiredService<IReferenceDataView>(),
        new CurrentResolver(sp.GetRequiredService<IFileAccess>()),
        new HistoryResolver(sp.GetRequiredService<IFileAccess>()),
        sp.GetRequiredService<IFileAccess>(),
        sp.GetRequiredService<ITokenCodec>(),
        sp.GetRequiredService<TimeProvider>(),
        o.Configurations.HistoryMaxQueryRange, o.Paging.LimitDefault, o.Paging.LimitMax,
        o.Tokens.FileIdTtl, o.Tokens.ContinuationTtl);
});

var app = builder.Build();
if (string.IsNullOrEmpty(keyDir))
{
    if (builder.Environment.IsDevelopment())
        app.Logger.LogWarning(
            "DataProtection:KeyDirectory가 설정되지 않았습니다. 키가 내구성 없는 기본 위치에 저장되어 프로세스/IIS 재시작 시 fileId가 무효화될 수 있습니다(개발 환경 전용).");
    else
        throw new InvalidOperationException(
            "DataProtection:KeyDirectory is required outside the Development environment; without it every issued fileId is invalidated on process/App Pool restart.");
}

app.UseMiddleware<AuditMiddleware>();       // 최외곽: 최종 status + Audit.ErrorCode를 함께 기록
app.UseMiddleware<ErrorMappingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapLogEndpoints();
app.MapConfigurationEndpoints();
app.MapFileEndpoints();
app.Run();

public partial class Program;
