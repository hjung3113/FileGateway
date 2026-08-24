using FileGateway.Api.Audit;
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

builder.Services.AddDataProtection(); // Task 20에서 IIS persist 구성 추가
builder.Services.AddSingleton<ITokenCodec, DataProtectionTokenCodec>();
builder.Services.AddSingleton<FtpConcurrencyLimiter>();
builder.Services.AddSingleton<FtpOptions>(sp => sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.Ftp
    ?? throw new InvalidOperationException("Ftp options required"));
builder.Services.AddTransient<IFileAccess, FtpFileAccess>();
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
