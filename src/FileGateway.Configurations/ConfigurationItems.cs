namespace FileGateway.Configurations;

public sealed record ConfigurationItem(
    string FileId,
    string EquipmentId,
    string ConfigurationType,
    string FileName,
    long Size);

public sealed record ConfigurationHistoryItem(
    string FileId,
    string EquipmentId,
    string ConfigurationType,
    DateTimeOffset SnapshotTimestamp,
    string FileName,
    long Size);

public sealed record ConfigurationHistoryQuery(
    string EquipmentId,
    string ConfigurationType,
    DateTimeOffset From,
    DateTimeOffset To,
    int? Limit,
    string? ContinuationToken);
