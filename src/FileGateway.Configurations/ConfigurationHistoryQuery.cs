namespace FileGateway.Configurations;

public sealed record ConfigurationHistoryQuery(
    string EquipmentId,
    string ConfigurationType,
    DateTimeOffset From,
    DateTimeOffset To,
    int? Limit,
    string? ContinuationToken);
