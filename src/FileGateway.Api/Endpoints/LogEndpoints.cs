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
        }).WithQueryParameters(ListQueryParameters).WithOperationNote(AttributeFilterNote);

        app.MapGet("/api/v1/logs/download", async (
            HttpRequest request, IOptions<FileGatewayOptions> options, ILogQueryService logs,
            IFileAccess fileAccess, HttpContext ctx, CancellationToken ct) =>
        {
            var query = ParseListQuery(request, options.Value);          // 목록과 완전 동일 파싱/매치 경로
            ctx.Items["Audit.EquipmentId"] = query.EquipmentId;
            ctx.Items["Audit.LogType"] = query.LogType;

            // 목록과 동일한 단일 resolve로 물리 위치까지 확정한다(파일별 추가 원격 listing 없음).
            var matches = await logs.ListLocatedAsync(query, ct);
            if (matches.Count == 0)
                throw new FileGatewayException("FileNotFound", "no file matched the query");

            if (matches.Count == 1)                                      // 1건: 기존 단일 스트리밍 응답(불변)
            {
                ctx.Items["Audit.FileId"] = matches[0].Descriptor.FileId;
                ctx.Items["Audit.FileName"] = matches[0].File.FileName;  // FileSize는 DownloadResult가 open 시점 크기로 설정(현행 유지)
                return (IResult)new DownloadResult(matches[0].File, fileAccess);
            }
            var located = matches.Select(m => m.File).ToList();
            return (IResult)new ZipDownloadResult(ZipName(query), located, fileAccess);   // 2건 이상: zip 스트리밍
        }).WithQueryParameters(ListQueryParameters).WithOperationNote(AttributeFilterNote);
        return app;
    }

    private static readonly (string, bool, string)[] ListQueryParameters =
    [
        ("equipmentId", true, "Equipment identifier"),
        ("logType", true, "Log type provided by the equipment"),
        ("from", false, "Range start (inclusive), ISO 8601. Omit both from and to for the default range"),
        ("to", false, "Range end (exclusive), ISO 8601. Omit both from and to for the default range"),
        ("subtype", false, "Definition-specific subtype (optional, varies per log definition)"),
        ("limit", false, "Maximum items per page"),
        ("continuationToken", false, "Opaque cursor from a previous response, for the next page"),
    ];

    // attr.<name>=<value>는 동적 키라 고정 파라미터로 선언할 수 없다(선언하면 클라이언트가 "attr.*"를 리터럴 키로 취급함).
    private const string AttributeFilterNote =
        "Additionally accepts attr.<name>=<value> for each attribute filter (repeatable, dynamic key — not listed as a fixed parameter).";

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

    // zip 파일명은 장식적 값이며 계약이 아니다(클라이언트가 파싱하지 않는다).
    private static string ZipName(LogListQuery query)
        => $"{query.EquipmentId}_{query.LogType}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}Z.zip";
}

