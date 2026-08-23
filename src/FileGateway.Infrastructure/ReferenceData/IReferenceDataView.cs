// src/FileGateway.Infrastructure/ReferenceData/IReferenceDataView.cs
namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>검증 완료 스냅샷 조회. 소비자(Logs/Configurations/Api)는 DB를 직접 모른다.</summary>
public interface IReferenceDataView
{
    Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct);
}
