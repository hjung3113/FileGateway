// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs
using System.Text.Json;
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Files;
using FileGateway.Logs.Definitions;

namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>기준정보 전체 검증 실패. Errors에 모든 오류를 모아 담는다.</summary>
public sealed class ReferenceDataValidationException(IReadOnlyList<string> errors)
    : Exception($"reference data validation failed ({errors.Count} error(s)): {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// 원시 SP 결과를 파싱·검증해 불변 스냅샷을 만든다. 하나라도 오류가 있으면 전체를 거부한다(부분 적용 없음).
/// 전 과정 순수 메모리 — FTP 접근 없음.
/// </summary>
public static class ReferenceDataSnapshotBuilder
{
    private static readonly JsonSerializerOptions MappingJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ReferenceDataSnapshot Build(ReferenceDataRaw raw)
    {
        var errors = new List<string>();

        var equipmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equipmentId in raw.EquipmentIds)
            if (!equipmentIds.Add(equipmentId))
                errors.Add($"duplicate equipmentId: {equipmentId}");

        var servers = new Dictionary<string, FileServerConnection>(StringComparer.Ordinal);
        foreach (var server in raw.Servers)
        {
            if (string.IsNullOrEmpty(server.ServerId))
            {
                errors.Add("server with empty serverId");
                continue;
            }
            if (!servers.TryAdd(server.ServerId, new FileServerConnection(server.ServerId, server.Host, server.RootPath)))
                errors.Add($"duplicate serverId: {server.ServerId}");
        }

        var logs = BuildLogDefinitions(raw.LogDefinitions, equipmentIds, servers, errors);
        var configurations = BuildConfigurationDefinitions(raw.ConfigurationDefinitions, equipmentIds, servers, errors);

        if (errors.Count > 0)
            throw new ReferenceDataValidationException(errors);

        return new ReferenceDataSnapshot(equipmentIds, servers, logs, configurations);
    }

    private static List<ResolvedLogDefinition> BuildLogDefinitions(
        IReadOnlyList<RawLogDefinition> rows,
        HashSet<string> equipmentIds,
        Dictionary<string, FileServerConnection> servers,
        List<string> errors)
    {
        var resolved = new List<ResolvedLogDefinition>();
        var keys = new HashSet<(string EquipmentId, string LogType)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var prefix = $"log[{i}] {row.EquipmentId}/{row.LogType}: ";

            if (!TryParseEnum<GenerationType>(row.GenerationType, out var generationType))
            {
                errors.Add(prefix + $"unsupported generationType: {row.GenerationType}");
                continue;
            }
            if (!TryParseEnum<Cardinality>(row.Cardinality, out var cardinality))
            {
                errors.Add(prefix + $"unsupported cardinality: {row.Cardinality}");
                continue;
            }
            if (!TryParseEnum<MetadataMode>(row.MetadataMode, out var metadataMode))
            {
                errors.Add(prefix + $"unsupported metadataMode: {row.MetadataMode}");
                continue;
            }
            if (!TryDeserializeMappings(row.MetadataMappingsJson, out var mappings))
            {
                errors.Add(prefix + $"invalid metadataMappings JSON: {row.MetadataMappingsJson}");
                continue;
            }

            var definition = new EquipmentLogDefinition(
                row.EquipmentId, row.LogType, row.ServerId, generationType,
                new LogDiscoveryRule(row.PathTemplate, row.FilePattern, cardinality),
                new LogMetadataRule(metadataMode, row.MetadataPattern, mappings));

            errors.AddRange(LogDefinitionValidator.Validate(definition).Select(e => prefix + e));

            if (string.IsNullOrEmpty(row.EquipmentId) || !equipmentIds.Contains(row.EquipmentId))
                errors.Add(prefix + $"unknown equipmentId: {row.EquipmentId}");
            if (!keys.Add((row.EquipmentId, row.LogType)))
            {
                errors.Add(prefix + "duplicate equipmentId + logType definition");
                continue;
            }
            if (!string.IsNullOrEmpty(row.ServerId) && servers.TryGetValue(row.ServerId, out var server))
                resolved.Add(new ResolvedLogDefinition(definition, server));
            else
                errors.Add(prefix + $"unknown serverId: {row.ServerId}");
        }

        return resolved;
    }

    private static List<ResolvedConfigurationDefinition> BuildConfigurationDefinitions(
        IReadOnlyList<RawConfigurationDefinition> rows,
        HashSet<string> equipmentIds,
        Dictionary<string, FileServerConnection> servers,
        List<string> errors)
    {
        var resolved = new List<ResolvedConfigurationDefinition>();
        var keys = new HashSet<(string EquipmentId, string ConfigurationType)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var prefix = $"configuration[{i}] {row.EquipmentId}/{row.ConfigurationType}: ";

            var definition = new EquipmentConfigurationDefinition(
                row.EquipmentId, row.ConfigurationType, row.ServerId,
                new CurrentRule(row.CurrentPathTemplate, row.CurrentFilePattern),
                new HistoryRule(row.HistoryPathTemplate, row.HistoryFilePattern, row.HistoryMarkerPathTemplate));

            errors.AddRange(ConfigurationDefinitionValidator.Validate(definition).Select(e => prefix + e));

            if (string.IsNullOrEmpty(row.EquipmentId) || !equipmentIds.Contains(row.EquipmentId))
                errors.Add(prefix + $"unknown equipmentId: {row.EquipmentId}");
            if (!keys.Add((row.EquipmentId, row.ConfigurationType)))
            {
                errors.Add(prefix + "duplicate equipmentId + configurationType definition");
                continue;
            }
            if (!string.IsNullOrEmpty(row.ServerId) && servers.TryGetValue(row.ServerId, out var server))
                resolved.Add(new ResolvedConfigurationDefinition(definition, server));
            else
                errors.Add(prefix + $"unknown serverId: {row.ServerId}");
        }

        return resolved;
    }

    private static bool TryParseEnum<TEnum>(string value, out TEnum parsed) where TEnum : struct, Enum
        => Enum.TryParse(value, out parsed) && Enum.IsDefined(parsed);

    private static bool TryDeserializeMappings(string json, out List<MetadataMapping> mappings)
    {
        try
        {
            var result = JsonSerializer.Deserialize<List<MetadataMapping>>(json, MappingJsonOptions);
            mappings = result ?? [];
            return result is not null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentNullException or NotSupportedException)
        {
            mappings = [];
            return false;
        }
    }
}
