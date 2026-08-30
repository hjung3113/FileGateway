// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataRaw.cs
namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>SP FileGateway_GetReferenceData 4개 result set의 row 단위 원시 표현. 검증 전 값.</summary>
public sealed record RawServer(string ServerId, string Host, string RootPath);

public sealed record RawLogDefinition(
    string EquipmentId,
    string LogType,
    string ServerId,
    string GenerationType,
    string PathTemplate,
    string FilePattern,
    string Cardinality,
    string MetadataMode,
    string MetadataPattern,
    string MetadataMappingsJson);

public sealed record RawConfigurationDefinition(
    string EquipmentId,
    string ConfigurationType,
    string ServerId,
    string CurrentPathTemplate,
    string CurrentFilePattern,
    string HistoryPathTemplate,
    string HistoryFilePattern,
    string HistoryMarkerPathTemplate,
    string CurrentFileMatchMode = "",
    string HistoryFileMatchMode = "",
    string HistoryMetadataMode = "",
    string HistoryMetadataPattern = "",
    string HistoryMetadataMappings = "");

public sealed record ReferenceDataRaw(
    IReadOnlyList<string> EquipmentIds,
    IReadOnlyList<RawServer> Servers,
    IReadOnlyList<RawLogDefinition> LogDefinitions,
    IReadOnlyList<RawConfigurationDefinition> ConfigurationDefinitions);
