// src/FileGateway.Api/Audit/AuditMiddleware.cs
using System.Diagnostics;

namespace FileGateway.Api.Audit;

/// <summary>최외곽 감사 로그. 실패 요청을 포함해 최종 Response.StatusCode와 Items["Audit.ErrorCode"]를 함께 기록한다.
/// /health/*는 미기록. API Key 원문·token payload·물리 경로는 기록하지 않는다.</summary>
public sealed class AuditMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
{
    private const string CallerIdKey = "CallerId";
    private const string EquipmentKey = "Audit.EquipmentId";
    private const string LogTypeKey = "Audit.LogType";
    private const string ConfigurationTypeKey = "Audit.ConfigurationType";
    private const string FileIdKey = "Audit.FileId";
    private const string FileNameKey = "Audit.FileName";
    private const string FileSizeKey = "Audit.FileSize";
    private const string ErrorCodeKey = "Audit.ErrorCode";

    private readonly ILogger _logger = loggerFactory.CreateLogger("FileGateway.Audit");

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            // route template 우선(엔드포인트 미매칭 시 raw path). ApiKey/엔드포인트가 채운 Items만 읽는다.
            var path = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                       ?? context.Request.Path.ToString();
            _logger.LogInformation(
                "callerId {CallerId} clientIp {ClientIp} {Method} {Path} equipmentId {EquipmentId} " +
                "logType {LogType} configurationType {ConfigurationType} fileId {FileId} fileName {FileName} " +
                "fileSize {FileSize} status {Status} errorCode {ErrorCode} elapsedMs {ElapsedMs}",
                Items(context, CallerIdKey), context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Method, path,
                Items(context, EquipmentKey), Items(context, LogTypeKey), Items(context, ConfigurationTypeKey),
                Items(context, FileIdKey), Items(context, FileNameKey), Items(context, FileSizeKey),
                context.Response.StatusCode, Items(context, ErrorCodeKey), elapsedMs);
        }
    }

    private static string? Items(HttpContext context, string key)
        => context.Items.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}
