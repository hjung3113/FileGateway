using FileGateway.Api.Downloading;
using FileGateway.Api.Options;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Time;
using FileGateway.Logs;
using Microsoft.Extensions.Options;

namespace FileGateway.Api.Endpoints;

/// <summary>로그 목록/단일 다운로드. 목록과 다운로드는 동일 Resolve 규칙(ILogQueryService)을 사용한다.</summary>
public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/logs", async (
            HttpRequest request, IOptions<FileGatewayOptions> options, ILogQueryService logs,
            HttpContext ctx, CancellationToken ct) =>
        {
            var query = ParseListQuery(request, options.Value);
            ctx.Items["Audit.EquipmentId"] = query.EquipmentId;
            ctx.Items["Audit.LogType"] = query.LogType;
            var page = await logs.ListAsync(query, ct);
            return Results.Ok(new { items = page.Items, continuationToken = page.ContinuationToken });
        });

        app.MapGet("/api/v1/logs/download", async (
            HttpRequest request, IOptions<FileGatewayOptions> options, ILogQueryService logs,
            IFileAccess fileAccess, HttpContext ctx, CancellationToken ct) =>
        {
            var query = ParseListQuery(request, options.Value);
            ctx.Items["Audit.EquipmentId"] = query.EquipmentId;
            ctx.Items["Audit.LogType"] = query.LogType;
            var match = await logs.ResolveSingleAsync(query, ct);
            if (match.Count == MatchCount.Zero)
                throw new FileGatewayException("FileNotFound", "no file matched the query");
            if (match.Count == MatchCount.Many)
                throw new FileGatewayException("MultipleFilesMatched", "query matched more than one file");
            var file = match.File!;
            ctx.Items["Audit.FileName"] = file.FileName;
            ctx.Items["Audit.FileId"] = match.FileId;
            return new DownloadResult(file, fileAccess);
        });
        return app;
    }

    internal static LogListQuery ParseListQuery(HttpRequest request, FileGatewayOptions opt)
    {
        string Required(string name)
            => request.Query.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.ToString() : throw new FileGatewayException("InvalidRequest", $"missing {name}");
        var equipmentId = Required("equipmentId");
        var logType = Required("logType");

        // 파싱 실패(형식 오류)도 조건 오류와 같은 InvalidRequest로 처리한다.
        DateTimeOffset? Time(string name) => request.Query.TryGetValue(name, out var v) && v.Count > 0
            ? TryParseTime(v.ToString(), name) : null;

        string? subtype = request.Query.TryGetValue("subtype", out var s) && s.Count > 0 ? s.ToString() : null;
        var attrs = request.Query.Where(kv => kv.Key.StartsWith("attr.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key["attr.".Length..], kv => kv.Value.ToString());

        int? limit = null;
        if (request.Query.TryGetValue("limit", out var l) && l.Count > 0)
        {
            if (!int.TryParse(l, out var n) || n <= 0)
                throw new FileGatewayException("InvalidRequest", "invalid limit");
            if (n > opt.Paging.LimitMax)
                throw new FileGatewayException("InvalidRequest", $"limit exceeds max {opt.Paging.LimitMax}");
            limit = n;
        }
        string? token = request.Query.TryGetValue("continuationToken", out var t) && t.Count > 0 ? t.ToString() : null;
        return new(equipmentId, logType, Time("from"), Time("to"), subtype, attrs, limit, token);
    }

    private static DateTimeOffset TryParseTime(string value, string name)
    {
        try { return SiteTime.Parse(value); }
        catch (ArgumentException ex) { throw new FileGatewayException("InvalidRequest", $"invalid {name}", ex); }
    }
}
