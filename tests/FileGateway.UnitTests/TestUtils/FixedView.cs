using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.UnitTests.TestUtils;

/// <summary>고정 스냅샷을 반환하는 IReferenceDataView 3줄 헬퍼.</summary>
public sealed class FixedView(ReferenceDataSnapshot snapshot) : IReferenceDataView
{
    public Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(snapshot);
}
