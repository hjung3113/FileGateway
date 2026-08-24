// src/FileGateway.Api/Endpoints/HealthEndpoints.cs
using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

        // ready는 최초 기준정보 로딩을 실제로 유발한다(확정 결정 14). FTP 접근 없음.
        app.MapGet("/health/ready", async (IReferenceDataView view, CancellationToken ct) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await view.GetSnapshotAsync(timeout.Token);
                return view is ReferenceDataCache cache
                    ? Results.Ok(new { status = "Healthy", stale = false,
                        lastGoodRefreshAt = cache.LastGoodRefreshAt })
                    : Results.Ok(new { status = "Healthy", stale = false });
            }
            catch (Exception ex) when (ex is FileGatewayException or OperationCanceledException)
            {
                // 최초 로딩 실패/timeout이라도 last-known-good이 있으면 stale로 서비스 가능.
                if (view is ReferenceDataCache { HasUsableSnapshot: true } cache)
                    return Results.Ok(new { status = "Degraded", stale = true,
                        lastGoodRefreshAt = cache.LastGoodRefreshAt });
                return Results.Json(new { status = "Unhealthy" }, statusCode: 503);
            }
        });

        return app;
    }
}
