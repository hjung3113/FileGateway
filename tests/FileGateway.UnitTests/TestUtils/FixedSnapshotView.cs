using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

/// <summary>고정 스냅샷 또는 ReferenceDataUnavailable을 반환하는 IReferenceDataView 테스트 더블.</summary>
public sealed class FixedSnapshotView(ReferenceDataSnapshot? snapshot) : IReferenceDataView
{
    public Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct)
        => snapshot is null
            ? Task.FromException<ReferenceDataSnapshot>(new FileGatewayException("ReferenceDataUnavailable"))
            : Task.FromResult(snapshot);
}
