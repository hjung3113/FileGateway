// src/FileGateway.Api/Endpoints/CatalogEndpoints.cs
using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.Api.Endpoints;

/// <summary>설비별 제공 파일 종류 조회. 기준정보 cache만 사용(FTP 접근 없음). Task 15가 전체 catalog로 확장한다.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/equipments/{equipmentId}/file-types",
            async (string equipmentId, IReferenceDataView view, CancellationToken ct) =>
        {
            var snapshot = await view.GetSnapshotAsync(ct);
            if (!snapshot.EquipmentIds.Contains(equipmentId))
                throw new FileGatewayException("EquipmentNotFound");
            return Results.Ok(new
            {
                logs = snapshot.GetLogSummaries(equipmentId),
                configurations = snapshot.GetConfigurationTypeSummaries(equipmentId),
            });
        });
        return app;
    }
}
