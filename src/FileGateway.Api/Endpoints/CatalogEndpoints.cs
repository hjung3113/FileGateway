using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.Api.Endpoints;

/// <summary>설비별 제공 파일 종류 조회. 기준정보 snapshot만 사용(FTP 접근 없음).</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/equipments/{equipmentId}/file-types",
            async (string equipmentId, IReferenceDataView referenceData, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Items["Audit.EquipmentId"] = equipmentId;
            var snapshot = await referenceData.GetSnapshotAsync(ct);
            if (!snapshot.EquipmentIds.Contains(equipmentId))
                throw new FileGatewayException("EquipmentNotFound", "unknown equipment");
            return Results.Ok(new
            {
                equipmentId,
                logs = snapshot.GetLogSummaries(equipmentId)
                    .Select(s => new { logType = s.LogType, generationType = s.GenerationType }),
                configurations = snapshot.GetConfigurationTypeSummaries(equipmentId)
                    .Select(t => new { configurationType = t }),
            });
        });
        return app;
    }
}
