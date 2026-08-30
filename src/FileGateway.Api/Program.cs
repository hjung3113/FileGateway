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
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FileGatewayOptions>(builder.Configuration.GetSection("FileGateway"));
builder.Services.AddOptions<FileGatewayOptions>()
    .Validate(o => o.Logs.MaxQueryRange >= TimeSpan.FromDays(2), "Logs.MaxQueryRange must be >= 2 days")
    .Validate(o => o.Configurations.HistoryMaxQueryRange > TimeSpan.Zero, "HistoryMaxQueryRange must be positive")
    .ValidateOnStart();
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "설비/서버 등록 시 발급된 caller API Key",
        };
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", document)] = new List<string>(),
        });
        return Task.CompletedTask;
    });
});

// DataProtection 키 내구성: 미설정 시 기본 키 링은 프로세스/IIS App Pool 재시작과 무관하지 않으나
// 배포 환경(프로필 없는 App Pool identity)에서는 휘발성이므로 명시적 디렉터리를 요구한다.
var keyDir = builder.Configuration["DataProtection:KeyDirectory"];
var dp = builder.Services.AddDataProtection(o => o.ApplicationDiscriminator = "FileGateway");
if (!string.IsNullOrEmpty(keyDir))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(keyDir));
    if (OperatingSystem.IsWindows())
        dp.ProtectKeysWithDpapi(); // 현재 프로세스 계정(App Pool identity) 범위로 복호화 제한 — IIS Application Pool "Load User Profile"=true 필수
}
builder.Services.AddSingleton<ITokenCodec, DataProtectionTokenCodec>();
builder.Services.AddSingleton<FtpConcurrencyLimiter>();
builder.Services.AddSingleton<FtpOptions>(sp => sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.Ftp
    ?? throw new InvalidOperationException("Ftp options required"));
builder.Services.AddSingleton<FtpFileAccess>();
builder.Services.AddSingleton<LocalFileAccess>();
builder.Services.AddSingleton<IFileAccess>(sp => new RoutingFileAccess( // 구체형 2개를 해석해 composite로 — 선언 타입 기준 해석의 순환 참조 방지
    sp.GetRequiredService<LocalFileAccess>(),
    sp.GetRequiredService<FtpFileAccess>())); // 상위 서비스는 이 등록만 바라봄; 구성원은 모두 stateless singleton이라 수명 조합 이슈 없음
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

var devToolsEnabled = app.Environment.IsDevelopment()
    || app.Services.GetRequiredService<IOptions<FileGatewayOptions>>().Value.DevTools.Enabled;
if (devToolsEnabled)
{
    // 웹 API 테스트 도구(/tester, /scalar/v1). 내부 기준정보/스키마를 노출하는 문서 endpoint이므로
    // Development이거나 FileGateway:DevTools:Enabled=true로 명시적으로 켠 경우에만 등록한다.
    if (!app.Environment.IsDevelopment())
        app.Logger.LogWarning(
            "FileGateway:DevTools:Enabled=true로 인해 /tester, /scalar/v1이 {Environment} 환경에서 노출됩니다. 내부에서만 접근 가능한지 확인하세요.",
            app.Environment.EnvironmentName);

    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("FileGateway API")
        .WithTheme(ScalarTheme.None)
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
        .WithCustomCss(ScalarBrandCss.Content));
    app.MapGet("/tester", () => Results.File("tester/index.html", "text/html"));
}

app.Run();

public partial class Program;

/// <summary>
/// Scalar 문서 UI를 FeedbackOps 디자인 토큰(Samsung One UI 계열 라이트 테마)에 맞춘 CSS 오버라이드.
/// 값 출처: https://github.com/hjung3113/FeedbackOps DESIGN.md.
/// </summary>
internal static class ScalarBrandCss
{
    public const string Content = """
        :root {
            --scalar-color-1: #101828;
            --scalar-color-2: #374151;
            --scalar-color-3: #687386;
            --scalar-color-accent: #1428a0;
            --scalar-background-1: #f3f7fe;
            --scalar-background-2: #fbfdff;
            --scalar-background-3: #edf3fb;
            --scalar-border-color: #cbd6e6;
            --scalar-radius: 6px;
            --scalar-radius-lg: 6px;
            --scalar-radius-xl: 12px;
            --scalar-font: 'Inter', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            --scalar-font-code: 'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
        }
        .light-mode {
            --scalar-button-1: #1428a0;
            --scalar-button-1-color: #ffffff;
            --scalar-button-1-hover: #101f80;
        }
        """;
}
