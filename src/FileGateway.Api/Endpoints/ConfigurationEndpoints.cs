using FileGateway.Api.Downloading;
using FileGateway.Api.Options;
using FileGateway.Configurations;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Time;
using Microsoft.Extensions.Options;

namespace FileGateway.Api.Endpoints;

/// <summary>구성 Current/History 조회·다운로드. 목록과 다운로드는 동일 Resolve 규칙(IConfigurationQueryService)을 사용한다.</summary>
public static class ConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/configurations/current", async (
            HttpRequest request, IConfigurationQueryService configurations,
            HttpContext ctx, CancellationToken ct) =>
        {
            var (equipmentId, configurationType) = ParseTarget(request);
            ctx.Items["Audit.EquipmentId"] = equipmentId;
            ctx.Items["Audit.ConfigurationType"] = configurationType;
            // 단순 배열(빈 결과 200 []) — 로그 목록과 달리 envelope 없음
            return Results.Ok(await configurations.GetCurrentAsync(equipmentId, configurationType, ct));
        }).WithQueryParameters(TargetQueryParameters);

        app.MapGet("/api/v1/configurations/current/download", async (
            HttpRequest request, IConfigurationQueryService configurations,
            IFileAccess fileAccess, HttpContext ctx, CancellationToken ct) =>
        {
            var (equipmentId, configurationType) = ParseTarget(request);
            ctx.Items["Audit.EquipmentId"] = equipmentId;
            ctx.Items["Audit.ConfigurationType"] = configurationType;
            var match = await configurations.ResolveCurrentSingleAsync(equipmentId, configurationType, ct);
            if (match.Count == MatchCount.Zero)
                throw new FileGatewayException("FileNotFound", "no current configuration file matched");
            if (match.Count == MatchCount.Many)
                throw new FileGatewayException("MultipleFilesMatched", "current configuration matched more than one file");
            var file = match.File!;
            ctx.Items["Audit.FileName"] = file.FileName;
            ctx.Items["Audit.FileSize"] = file.Size;
            if (match.FileId is not null)
                ctx.Items["Audit.FileId"] = match.FileId;
            return new DownloadResult(file, fileAccess);
        }).WithQueryParameters(TargetQueryParameters);

        app.MapGet("/api/v1/configurations/history", async (
            HttpRequest request, IOptions<FileGatewayOptions> options, IConfigurationQueryService configurations,
            HttpContext ctx, CancellationToken ct) =>
        {
            var query = ParseHistoryQuery(request, options.Value);
            ctx.Items["Audit.EquipmentId"] = query.EquipmentId;
            ctx.Items["Audit.ConfigurationType"] = query.ConfigurationType;
            var page = await configurations.GetHistoryAsync(query, ct);
            return Results.Ok(new { items = page.Items, continuationToken = page.ContinuationToken });
        }).WithQueryParameters(HistoryQueryParameters);
        return app;
    }

    private static readonly (string, bool, string)[] TargetQueryParameters =
    [
        ("equipmentId", true, "Equipment identifier"),
        ("configurationType", true, "Configuration type provided by the equipment"),
    ];

    private static readonly (string, bool, string)[] HistoryQueryParameters =
    [
        ("equipmentId", true, "Equipment identifier"),
        ("configurationType", true, "Configuration type provided by the equipment"),
        ("from", true, "Range start (inclusive), ISO 8601"),
        ("to", true, "Range end (exclusive), ISO 8601"),
        ("limit", false, "Maximum items per page"),
        ("continuationToken", false, "Opaque cursor from a previous response, for the next page"),
    ];

    internal static (string EquipmentId, string ConfigurationType) ParseTarget(HttpRequest request)
    {
        string Required(string name)
            => request.Query.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.ToString() : throw new FileGatewayException("InvalidRequest", $"missing {name}");
        return (Required("equipmentId"), Required("configurationType"));
    }

    internal static ConfigurationHistoryQuery ParseHistoryQuery(HttpRequest request, FileGatewayOptions opt)
    {
        var (equipmentId, configurationType) = ParseTarget(request);

        // from/to 필수. 파싱 실패(형식 오류)도 누락과 같은 InvalidRequest로 처리한다.
        // from >= to / 범위 초과 검증은 GetHistoryAsync가 방어한다(Global Constraints).
        RequiredTime("from", out var from);
        RequiredTime("to", out var to);

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
        return new(equipmentId, configurationType, from, to, limit, token);

        void RequiredTime(string name, out DateTimeOffset value)
        {
            if (!request.Query.TryGetValue(name, out var v) || v.Count == 0)
                throw new FileGatewayException("InvalidRequest", $"missing {name}");
            value = ParseTime(v.ToString(), name);
        }
    }

    private static DateTimeOffset ParseTime(string value, string name)
    {
        try { return SiteTime.Parse(value); }
        catch (ArgumentException ex) { throw new FileGatewayException("InvalidRequest", $"invalid {name}", ex); }
    }
}
