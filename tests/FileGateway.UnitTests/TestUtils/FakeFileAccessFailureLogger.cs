using FileGateway.Infrastructure.Diagnostics;

namespace FileGateway.UnitTests.TestUtils;

/// <summary>테스트용 in-memory 실패 로거. 기록된 항목을 검사할 수 있다.</summary>
public sealed class FakeFileAccessFailureLogger : IFileAccessFailureLogger
{
    public sealed record Entry(string EquipmentId, string LogType, string ServerId,
        DateTimeOffset RequestedSlot, string ComputedRelativePath, string FailureReason);

    public List<Entry> Entries { get; } = [];

    public Task LogAsync(string equipmentId, string logType, string serverId,
        DateTimeOffset requestedSlot, string computedRelativePath, string failureReason, CancellationToken ct)
    {
        Entries.Add(new(equipmentId, logType, serverId, requestedSlot, computedRelativePath, failureReason));
        return Task.CompletedTask;
    }
}
