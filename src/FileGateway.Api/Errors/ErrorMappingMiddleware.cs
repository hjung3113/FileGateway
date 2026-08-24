// src/FileGateway.Api/Errors/ErrorMappingMiddleware.cs
using System.Diagnostics;
using System.Text.Json;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;

namespace FileGateway.Api.Errors;

/// <summary>FileGatewayException을 ProblemDetails 계열 JSON({type,title,status,code,traceId})으로 변환하고
/// Items["Audit.ErrorCode"]를 남겨 Audit(외곽)이 실패 요청도 분류와 함께 기록하게 한다.</summary>
public sealed class ErrorMappingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (FileGatewayException ex)
        {
            var (status, title) = FileGatewayErrors.Map(ex.Code);
            context.Items["Audit.ErrorCode"] = ex.Code;
            if (context.Response.HasStarted) { /* 다운로드 중 오류: 응답 불가, 분류만 감사에 남긴다 */ }
            else await WriteProblem(context, status, ex.Code, title);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Items["Audit.ErrorCode"] = "ClientCancelled"; // 연결 종료에 맡긴다(새 응답 없음)
        }
        catch (FileAccessException ex)
        {
            var code = ex.Error switch
            {
                FileAccessError.ProtocolError => "FileServerProtocolError",
                FileAccessError.FileNotFound => "FileNotFound",
                _ => "FileServerUnavailable",
            };
            var (status, title) = FileGatewayErrors.Map(code);
            // ex/InnerException 미기록: FtpFileAccess.Classify가 감싼 원본 소켓/FluentFTP 예외는
            // 호스트/연결 세부정보를 담을 수 있어 로그에 남기지 않는다(비노출 제약).
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            context.RequestServices.GetRequiredService<ILogger<ErrorMappingMiddleware>>()
                .LogWarning("file access failure {Error} {TraceId} {Path}", ex.Error, traceId, context.Request.Path);
            if (context.Response.HasStarted) { /* 다운로드 중 오류: 응답 불가, 분류만 감사에 남긴다 */ }
            else await WriteProblem(context, status, code, title);
        }
        catch (Exception ex)
        {
            context.Items["Audit.ErrorCode"] = "InternalError";
            context.RequestServices.GetRequiredService<ILogger<ErrorMappingMiddleware>>()
                .LogError(ex, "unhandled error {Path}", context.Request.Path);
            if (!context.Response.HasStarted)
                await WriteProblem(context, 500, "InternalError", "Internal server error");
        }
    }

    private static async Task WriteProblem(HttpContext ctx, int status, string code, string title)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank", title, status, code,
            traceId = Activity.Current?.Id ?? ctx.TraceIdentifier,
        }));
    }
}
