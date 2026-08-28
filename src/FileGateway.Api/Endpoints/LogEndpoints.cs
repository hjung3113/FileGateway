using FileGateway.Api.Downloading;
using FileGateway.Api.Options;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Time;
using FileGateway.Core.Tokens;
using FileGateway.Logs;
using FileGateway.Logs.Tokens;
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
            ITokenCodec codec, IFileAccess fileAccess, HttpContext ctx, CancellationToken ct) =>
        {
            var query = ParseListQuery(request, options.Value);          // 목록과 완전 동일 파싱/매치 경로
            ctx.Items["Audit.EquipmentId"] = query.EquipmentId;
            ctx.Items["Audit.LogType"] = query.LogType;

            var page = await logs.ListAsync(query, ct);
            if (page.Items.Count == 0)
                throw new FileGatewayException("FileNotFound", "no file matched the query");

            // 매치된 descriptor의 FileId로 물리 위치 재해석(서비스 계약 무변경, /files/download와 동일 의미)
            var located = new List<LocatedFile>(page.Items.Count);
            foreach (var item in page.Items)
                located.Add(await logs.LocateByFileIdAsync(DecodeLogFileId(codec, item.FileId), ct));

            if (located.Count == 1)                                      // 1건: 기존 단일 스트리밍 응답(불변)
            {
                ctx.Items["Audit.FileId"] = page.Items[0].FileId;
                ctx.Items["Audit.FileName"] = located[0].FileName;       // FileSize는 DownloadResult가 open 시점 크기로 설정(현행 유지)
                return (IResult)new DownloadResult(located[0], fileAccess);
            }
            return (IResult)new ZipDownloadResult(ZipName(query), located, fileAccess);   // 2건 이상: zip 스트리밍
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

    // 직전에 자체 발급한 log fileId 토큰을 재해석한다(purpose 단일).
    private static TokenPayload DecodeLogFileId(ITokenCodec codec, string fileId)
    {
        var d = codec.Unprotect(fileId, LogTokenKinds.FileIdPurpose);
        if (d.Validity != TokenValidity.Valid)                           // 방어 코드: 도달 사실상 불가(수 ms 전 자체 발급)
            throw new FileGatewayException("InternalError", "re-issued file id failed to decode");
        return d.Payload!;
    }

    // zip 파일명은 장식적 값이며 계약이 아니다(클라이언트가 파싱하지 않는다).
    private static string ZipName(LogListQuery query)
        => $"{query.EquipmentId}_{query.LogType}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}Z.zip";
}

