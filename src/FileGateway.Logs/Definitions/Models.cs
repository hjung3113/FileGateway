// src/FileGateway.Logs/Definitions/Models.cs
using FileGateway.Core.Files;

namespace FileGateway.Logs.Definitions;

public enum GenerationType { Hourly, Daily, Continuous }
public enum Cardinality { Single, Multiple }
public enum MetadataMode { Template, Regex }

public sealed record LogDiscoveryRule(
    string PathTemplate, string FilePattern, Cardinality Cardinality, string? FileNameTemplate = null);

public sealed record MetadataMapping(string Group, string Target, string? Format);

public sealed record LogMetadataRule(MetadataMode Mode, string Pattern, IReadOnlyList<MetadataMapping> Mappings);

public sealed record EquipmentLogDefinition(
    string EquipmentId,
    string LogType,
    string ServerId,
    GenerationType GenerationType,
    LogDiscoveryRule DiscoveryRule,
    LogMetadataRule MetadataRule);

public sealed record ResolvedLogDefinition(EquipmentLogDefinition Definition, FileServerConnection Server);

public sealed record LogTypeSummary(string LogType, string GenerationType);
