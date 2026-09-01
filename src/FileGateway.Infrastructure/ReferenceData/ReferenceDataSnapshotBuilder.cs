// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs
using System.Text.Json;
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Files;
using FileGateway.Logs.Definitions;
using ConfigurationMetadataMapping = FileGateway.Configurations.Definitions.ConfigurationMetadataMapping;
using ConfigurationMetadataMode = FileGateway.Configurations.Definitions.ConfigurationMetadataMode;
using ConfigurationMetadataRule = FileGateway.Configurations.Definitions.ConfigurationMetadataRule;
using Microsoft.Extensions.Logging;

namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>기준정보 snapshot을 만들 수 없는 전역 검증 실패. Errors에 전역 오류를 모아 담는다.</summary>
public sealed class ReferenceDataValidationException(IReadOnlyList<string> errors)
    : Exception($"reference data validation failed ({errors.Count} error(s)): {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// 원시 SP 결과를 파싱·검증해 불변 스냅샷을 만든다. 전역 식별자 오류는 전체를 거부하고,
/// 개별 정의 오류는 해당 정의만 격리한다. 전 과정 순수 메모리 — FTP 접근 없음.
/// </summary>
public static class ReferenceDataSnapshotBuilder
{
    private static readonly JsonSerializerOptions MappingJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ReferenceDataSnapshot Build(ReferenceDataRaw raw, ILogger? logger = null)
    {
        var globalErrors = new List<string>();

        var equipmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equipmentId in raw.EquipmentIds)
            if (!equipmentIds.Add(equipmentId))
                globalErrors.Add($"duplicate equipmentId: {equipmentId}");

        var servers = new Dictionary<string, FileServerConnection>(StringComparer.Ordinal);
        foreach (var server in raw.Servers)
        {
            if (string.IsNullOrEmpty(server.ServerId))
            {
                globalErrors.Add("server with empty serverId");
                continue;
            }
            if (!servers.TryAdd(server.ServerId, new FileServerConnection(server.ServerId, server.Host, server.RootPath)))
                globalErrors.Add($"duplicate serverId: {server.ServerId}");
        }

        // 전역 식별자 오류는 정의 단위 검증/격리 로그보다 먼저 fail-closed — snapshot 전체가 폐기될
        // refresh에서 개별 정의가 실제로 격리된 것처럼 quarantine 경고가 남는 것을 막는다.
        if (globalErrors.Count > 0)
            throw new ReferenceDataValidationException(globalErrors);

        var logs = BuildLogDefinitions(raw.LogDefinitions, equipmentIds, servers, logger);
        var configurations = BuildConfigurationDefinitions(raw.ConfigurationDefinitions, equipmentIds, servers, logger);

        return new ReferenceDataSnapshot(equipmentIds, servers, logs, configurations);
    }

    private static List<ResolvedLogDefinition> BuildLogDefinitions(
        IReadOnlyList<RawLogDefinition> rows,
        HashSet<string> equipmentIds,
        Dictionary<string, FileServerConnection> servers,
        ILogger? logger)
    {
        var resolved = new List<ResolvedLogDefinition>();
        var duplicateKeys = rows.GroupBy(row => (row.EquipmentId, row.LogType))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var prefix = $"log[{i}] {row.EquipmentId}/{row.LogType}: ";
            var errors = new List<string>();
            // 물리 경로/패턴 원문이 warning 로그에 노출되지 않도록 정의별 raw 값을 마스킹 대상으로 수집.
            var sensitive = new[] { row.PathTemplate, row.FilePattern, row.MetadataPattern };
            var key = (row.EquipmentId, row.LogType);
            if (duplicateKeys.Contains(key))
                errors.Add(prefix + "duplicate equipmentId + logType definition");

            if (!TryParseEnum<GenerationType>(row.GenerationType, out var generationType))
            {
                errors.Add(prefix + $"unsupported generationType: {row.GenerationType}");
                LogInvalidDefinition(logger, "log", i, row.EquipmentId, row.LogType, errors, sensitive);
                continue;
            }
            if (!TryParseEnum<Cardinality>(row.Cardinality, out var cardinality))
            {
                errors.Add(prefix + $"unsupported cardinality: {row.Cardinality}");
                LogInvalidDefinition(logger, "log", i, row.EquipmentId, row.LogType, errors, sensitive);
                continue;
            }
            if (!TryParseEnum<MetadataMode>(row.MetadataMode, out var metadataMode))
            {
                errors.Add(prefix + $"unsupported metadataMode: {row.MetadataMode}");
                LogInvalidDefinition(logger, "log", i, row.EquipmentId, row.LogType, errors, sensitive);
                continue;
            }
            if (!TryDeserializeMappings(row.MetadataMappingsJson, out var mappings))
            {
                errors.Add(prefix + "invalid metadataMappings JSON");
                LogInvalidDefinition(logger, "log", i, row.EquipmentId, row.LogType, errors, sensitive);
                continue;
            }

            var definition = new EquipmentLogDefinition(
                row.EquipmentId, row.LogType, row.ServerId, generationType,
                new LogDiscoveryRule(row.PathTemplate, row.FilePattern, cardinality),
                new LogMetadataRule(metadataMode, row.MetadataPattern, mappings));

            errors.AddRange(LogDefinitionValidator.Validate(definition).Select(e => prefix + e));

            if (string.IsNullOrEmpty(row.EquipmentId) || !equipmentIds.Contains(row.EquipmentId))
                errors.Add(prefix + $"unknown equipmentId: {row.EquipmentId}");
            if (!string.IsNullOrEmpty(row.ServerId) && servers.TryGetValue(row.ServerId, out var server))
            {
                if (errors.Count == 0)
                    resolved.Add(new ResolvedLogDefinition(definition, server));
            }
            else
                errors.Add(prefix + $"unknown serverId: {row.ServerId}");

            if (errors.Count > 0)
                LogInvalidDefinition(logger, "log", i, row.EquipmentId, row.LogType, errors, sensitive);
        }

        return resolved;
    }

    private static List<ResolvedConfigurationDefinition> BuildConfigurationDefinitions(
        IReadOnlyList<RawConfigurationDefinition> rows,
        HashSet<string> equipmentIds,
        Dictionary<string, FileServerConnection> servers,
        ILogger? logger)
    {
        var resolved = new List<ResolvedConfigurationDefinition>();
        var duplicateKeys = rows.GroupBy(row => (row.EquipmentId, row.ConfigurationType))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var prefix = $"configuration[{i}] {row.EquipmentId}/{row.ConfigurationType}: ";
            var errors = new List<string>();
            // 물리 경로/패턴 원문이 warning 로그에 노출되지 않도록 정의별 raw 값을 마스킹 대상으로 수집.
            var sensitive = new[]
            {
                row.CurrentPathTemplate, row.CurrentFilePattern,
                row.HistoryPathTemplate, row.HistoryFilePattern,
                row.HistoryMarkerPathTemplate, row.HistoryMetadataPattern
            };
            var key = (row.EquipmentId, row.ConfigurationType);
            if (duplicateKeys.Contains(key))
                errors.Add(prefix + "duplicate equipmentId + configurationType definition");

            if (!TryParseConfigurationMetadata(row, prefix, errors, out var metadata))
            {
                LogInvalidDefinition(logger, "configuration", i, row.EquipmentId, row.ConfigurationType, errors, sensitive);
                continue;
            }

            var definition = new EquipmentConfigurationDefinition(
                row.EquipmentId, row.ConfigurationType, row.ServerId,
                new CurrentRule(row.CurrentPathTemplate, row.CurrentFilePattern, row.CurrentFileMatchMode),
                new HistoryRule(row.HistoryPathTemplate, row.HistoryFilePattern,
                    row.HistoryMarkerPathTemplate, row.HistoryFileMatchMode, metadata));

            errors.AddRange(ConfigurationDefinitionValidator.Validate(definition).Select(e => prefix + e));

            if (string.IsNullOrEmpty(row.EquipmentId) || !equipmentIds.Contains(row.EquipmentId))
                errors.Add(prefix + $"unknown equipmentId: {row.EquipmentId}");
            if (!string.IsNullOrEmpty(row.ServerId) && servers.TryGetValue(row.ServerId, out var server))
            {
                if (errors.Count == 0)
                    resolved.Add(new ResolvedConfigurationDefinition(definition, server));
            }
            else
                errors.Add(prefix + $"unknown serverId: {row.ServerId}");

            if (errors.Count > 0)
                LogInvalidDefinition(logger, "configuration", i, row.EquipmentId, row.ConfigurationType, errors, sensitive);
        }

        return resolved;
    }

    private static bool TryParseConfigurationMetadata(
        RawConfigurationDefinition row,
        string prefix,
        List<string> errors,
        out ConfigurationMetadataRule? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(row.HistoryMetadataMode))
        {
            if (!string.IsNullOrWhiteSpace(row.HistoryMetadataPattern) ||
                !string.IsNullOrWhiteSpace(row.HistoryMetadataMappings))
                errors.Add(prefix + "metadata pattern/mappings require historyMetadataMode");
            return string.IsNullOrWhiteSpace(row.HistoryMetadataPattern) &&
                string.IsNullOrWhiteSpace(row.HistoryMetadataMappings);
        }

        if (!TryParseEnum<ConfigurationMetadataMode>(row.HistoryMetadataMode, out var mode))
        {
            errors.Add(prefix + $"unsupported historyMetadataMode: {row.HistoryMetadataMode}");
            return false;
        }
        if (!TryDeserializeConfigurationMappings(row.HistoryMetadataMappings, out var mappings))
        {
            errors.Add(prefix + "invalid historyMetadataMappings JSON");
            return false;
        }

        metadata = new ConfigurationMetadataRule(mode, row.HistoryMetadataPattern, mappings);
        return true;
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

    private static bool TryDeserializeConfigurationMappings(
        string json, out List<ConfigurationMetadataMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            mappings = [];
            return true;
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<ConfigurationMetadataMapping>>(json, MappingJsonOptions);
            mappings = result ?? [];
            return result is not null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentNullException or NotSupportedException)
        {
            mappings = [];
            return false;
        }
    }

    private static void LogInvalidDefinition(
        ILogger? logger, string kind, int index, string equipmentId, string definitionType,
        IReadOnlyList<string> errors, IReadOnlyList<string?> sensitiveValues)
    {
        if (logger is null || errors.Count == 0) return;
        var sanitized = errors.Select(e => Redact(e, sensitiveValues));
        logger.LogWarning(
            "reference data definition quarantined {DefinitionKind} {DefinitionIndex} {EquipmentId} {DefinitionType}: {ValidationErrors}",
            kind, index, equipmentId, definitionType, string.Join("; ", sanitized));
    }

    /// <summary>정의의 원본 경로/패턴 값이 validation 메시지에 실려 로그로 새지 않도록 치환.</summary>
    private static string Redact(string message, IReadOnlyList<string?> sensitiveValues)
    {
        foreach (var value in sensitiveValues)
            if (!string.IsNullOrEmpty(value))
                message = message.Replace(value, "<redacted>", StringComparison.Ordinal);
        return message;
    }
}
