// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataRaw.cs
namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>SP FileGateway_GetReferenceData 4개 result set의 row 단위 원시 표현. 검증 전 값.</summary>
public sealed record RawServer(string ServerId, string Host, string FileRootPath);

public sealed record RawLogDefinition(
    string EquipmentId,
    string LogType,
    string ServerId,
    string GenerationType,
    string DirectoryTemplate,
    string FileNamePattern,
    string SlotCardinality,
    string MetadataParseMode,
    string RelativePathMetadataPattern,
    string MetadataGroupMappingsJson);

public sealed record RawConfigurationDefinition(
    string EquipmentId,
    string ConfigurationType,
    string ServerId,
    string CurrentDirectoryTemplate,
    string CurrentFileNamePattern,
    string CurrentFileNameMatchMode,
    string HistoryDirectoryTemplate,
    string HistoryFileNamePattern,
    string HistoryFileNameMatchMode,
    string HistoryCompletionMarkerPathTemplate,
    string HistoryTimestampParseMode,
    string HistoryFileNameTimestampPattern,
    string HistoryTimestampMappings)
{
    // 기존 테스트/호출부의 기본 Configuration 정의 표기를 새 raw 계약으로 연결한다.
    public RawConfigurationDefinition(
        string equipmentId, string configurationType, string serverId,
        string currentDirectoryTemplate, string currentFileNamePattern,
        string historyDirectoryTemplate, string historyFileNamePattern,
        string historyCompletionMarkerPathTemplate)
        : this(equipmentId, configurationType, serverId,
            currentDirectoryTemplate, currentFileNamePattern, "",
            historyDirectoryTemplate, historyFileNamePattern, "",
            historyCompletionMarkerPathTemplate, "", "", "")
    {
    }
}

public sealed record ReferenceDataRaw(
    IReadOnlyList<string> EquipmentIds,
    IReadOnlyList<RawServer> Servers,
    IReadOnlyList<RawLogDefinition> LogDefinitions,
    IReadOnlyList<RawConfigurationDefinition> ConfigurationDefinitions);
