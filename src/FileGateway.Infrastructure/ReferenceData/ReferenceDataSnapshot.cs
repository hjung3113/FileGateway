// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshot.cs
using FileGateway.Configurations.Definitions;
using FileGateway.Core.Files;
using FileGateway.Logs.Definitions;

namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>
/// 검증을 통과한 기준정보의 불변 스냅샷. 순수 메모리 — FTP 접근 없음.
/// 조회 키(equipmentId/logType/configurationType)는 정확 일치(ordinal)다.
/// </summary>
public sealed class ReferenceDataSnapshot
{
    private readonly Dictionary<(string EquipmentId, string LogType), ResolvedLogDefinition> _logs;
    private readonly Dictionary<(string EquipmentId, string ConfigurationType), ResolvedConfigurationDefinition> _configurations;
    private readonly Dictionary<string, IReadOnlyList<LogTypeSummary>> _logSummaries;
    private readonly Dictionary<string, IReadOnlyList<string>> _configurationTypeSummaries;

    public IReadOnlySet<string> EquipmentIds { get; }
    public IReadOnlyDictionary<string, FileServerConnection> Servers { get; }

    public ReferenceDataSnapshot(
        IReadOnlySet<string> equipmentIds,
        IReadOnlyDictionary<string, FileServerConnection> servers,
        IReadOnlyList<ResolvedLogDefinition> logs,
        IReadOnlyList<ResolvedConfigurationDefinition> configurations)
    {
        EquipmentIds = equipmentIds;
        Servers = servers;

        _logs = [];
        var logSummaries = new Dictionary<string, List<LogTypeSummary>>();
        foreach (var log in logs)
        {
            var def = log.Definition;
            _logs.Add((def.EquipmentId, def.LogType), log);
            if (!logSummaries.TryGetValue(def.EquipmentId, out var summaries))
                logSummaries[def.EquipmentId] = summaries = [];
            summaries.Add(new LogTypeSummary(def.LogType, def.GenerationType.ToString()));
        }

        _configurations = [];
        var configurationTypes = new Dictionary<string, List<string>>();
        foreach (var configuration in configurations)
        {
            var def = configuration.Definition;
            _configurations.Add((def.EquipmentId, def.ConfigurationType), configuration);
            if (!configurationTypes.TryGetValue(def.EquipmentId, out var types))
                configurationTypes[def.EquipmentId] = types = [];
            types.Add(def.ConfigurationType);
        }

        // catalog 요약은 이름 오름차순 정렬
        _logSummaries = logSummaries.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<LogTypeSummary>)kv.Value.OrderBy(s => s.LogType, StringComparer.Ordinal).ToList());
        _configurationTypeSummaries = configurationTypes.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.OrderBy(t => t, StringComparer.Ordinal).ToList());
    }

    public ResolvedLogDefinition? FindLog(string equipmentId, string logType)
        => _logs.TryGetValue((equipmentId, logType), out var log) ? log : null;

    public ResolvedConfigurationDefinition? FindConfiguration(string equipmentId, string configurationType)
        => _configurations.TryGetValue((equipmentId, configurationType), out var configuration) ? configuration : null;

    public IReadOnlyList<LogTypeSummary> GetLogSummaries(string equipmentId)
        => _logSummaries.TryGetValue(equipmentId, out var summaries) ? summaries : [];

    public IReadOnlyList<string> GetConfigurationTypeSummaries(string equipmentId)
        => _configurationTypeSummaries.TryGetValue(equipmentId, out var types) ? types : [];
}
