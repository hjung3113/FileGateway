// src/FileGateway.Infrastructure/ReferenceData/SpReferenceDataSource.cs
using System.Data;
using FileGateway.Core.Errors;
using Microsoft.Data.SqlClient;

namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>SP FileGateway_GetReferenceData 4개 result set을 읽는 원시 reader.</summary>
/// <param name="spName">
/// 기본 SP명. 통합테스트가 계약 위반 변형 SP(예: result set 누락)를 가리킬 수 있게 주입 가능하다.
/// </param>
public sealed class SpReferenceDataSource(string connectionString, string spName = "dbo.FileGateway_GetReferenceData")
    : IReferenceDataSource
{
    private static readonly string[] EquipmentColumns = ["EquipmentId"];
    private static readonly string[] ServerColumns = ["ServerId", "Host", "FileRootPath"];
    private static readonly string[] LogColumns =
    [
        "EquipmentId", "LogType", "ServerId", "GenerationType", "DirectoryTemplate",
        "FileNamePattern", "SlotCardinality", "MetadataParseMode", "RelativePathMetadataPattern",
        "MetadataGroupMappings", "FileNameTemplate"
    ];
    private static readonly string[] ConfigurationColumns =
    [
        "EquipmentId", "ConfigurationType", "ServerId", "CurrentDirectoryTemplate",
        "CurrentFileNamePattern", "CurrentFileNameMatchMode", "HistoryDirectoryTemplate",
        "HistoryFileNamePattern", "HistoryFileNameMatchMode", "HistoryCompletionMarkerPathTemplate",
        "HistoryTimestampParseMode", "HistoryFileNameTimestampPattern", "HistoryTimestampMappings"
    ];

    public async Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = spName;
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var equipmentOrdinals = RequireColumns(reader, "equipments", EquipmentColumns);
        var equipments = new List<string>();
        while (await reader.ReadAsync(ct)) equipments.Add(reader.GetString(equipmentOrdinals["EquipmentId"]));
        await RequireNextResultAsync(reader, "servers", ct);

        var serverOrdinals = RequireColumns(reader, "servers", ServerColumns);
        var servers = new List<RawServer>();
        while (await reader.ReadAsync(ct))
            servers.Add(new(reader.GetString(serverOrdinals["ServerId"]), reader.GetString(serverOrdinals["Host"]),
                reader.GetString(serverOrdinals["FileRootPath"])));
        await RequireNextResultAsync(reader, "logs", ct);

        var logOrdinals = RequireColumns(reader, "logs", LogColumns);
        var logs = new List<RawLogDefinition>();
        while (await reader.ReadAsync(ct))
            logs.Add(new(reader.GetString(logOrdinals["EquipmentId"]), reader.GetString(logOrdinals["LogType"]),
                reader.GetString(logOrdinals["ServerId"]), reader.GetString(logOrdinals["GenerationType"]),
                reader.GetString(logOrdinals["DirectoryTemplate"]), reader.GetString(logOrdinals["FileNamePattern"]),
                reader.GetString(logOrdinals["SlotCardinality"]), reader.GetString(logOrdinals["MetadataParseMode"]),
                reader.GetString(logOrdinals["RelativePathMetadataPattern"]),
                reader.GetString(logOrdinals["MetadataGroupMappings"]),
                reader.GetString(logOrdinals["FileNameTemplate"])));
        await RequireNextResultAsync(reader, "configurationDefinitions", ct);

        var configurationOrdinals = RequireColumns(reader, "configurationDefinitions", ConfigurationColumns);
        var configs = new List<RawConfigurationDefinition>();
        while (await reader.ReadAsync(ct))
            configs.Add(new(reader.GetString(configurationOrdinals["EquipmentId"]),
                reader.GetString(configurationOrdinals["ConfigurationType"]),
                reader.GetString(configurationOrdinals["ServerId"]),
                reader.GetString(configurationOrdinals["CurrentDirectoryTemplate"]),
                reader.GetString(configurationOrdinals["CurrentFileNamePattern"]),
                reader.GetString(configurationOrdinals["CurrentFileNameMatchMode"]),
                reader.GetString(configurationOrdinals["HistoryDirectoryTemplate"]),
                reader.GetString(configurationOrdinals["HistoryFileNamePattern"]),
                reader.GetString(configurationOrdinals["HistoryFileNameMatchMode"]),
                reader.GetString(configurationOrdinals["HistoryCompletionMarkerPathTemplate"]),
                reader.GetString(configurationOrdinals["HistoryTimestampParseMode"]),
                reader.GetString(configurationOrdinals["HistoryFileNameTimestampPattern"]),
                reader.GetString(configurationOrdinals["HistoryTimestampMappings"])));

        return new(equipments, servers, logs, configs);
    }

    // SP 계약: 4개 result set이 모두 존재해야 한다. NextResultAsync=false(다음 set 없음)를
    // 무시하면 누락 set 이후가 빈 목록으로 해석되어 빈 catalog로 LKG를 교체할 수 있다.
    private static async Task RequireNextResultAsync(SqlDataReader reader, string resultSet, CancellationToken ct)
    {
        if (!await reader.NextResultAsync(ct))
            throw new FileGatewayException("ReferenceDataIncomplete",
                $"reference data result set '{resultSet}' missing (SP must return all 4 result sets)");
    }

    private static IReadOnlyDictionary<string, int> RequireColumns(
        SqlDataReader reader, string resultSet, IReadOnlyList<string> expectedColumns)
    {
        var actualColumns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        if (reader.FieldCount != expectedColumns.Count)
            throw new FileGatewayException("ReferenceDataIncomplete",
                $"reference data result set '{resultSet}' has expected {expectedColumns.Count} columns, actual {reader.FieldCount}");

        var actualOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, ordinal) in actualColumns.Select((name, ordinal) => (name, ordinal)))
            if (!actualOrdinals.TryAdd(name, ordinal))
                throw new FileGatewayException("ReferenceDataIncomplete",
                    $"reference data result set '{resultSet}' has duplicate column '{name}'");

        var missing = expectedColumns.Where(name => !actualOrdinals.ContainsKey(name)).ToArray();
        var unexpected = actualColumns.Where(name => !expectedColumns.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0 || unexpected.Length > 0)
            throw new FileGatewayException("ReferenceDataIncomplete",
                $"reference data result set '{resultSet}' columns do not match contract; " +
                $"missing: [{string.Join(", ", missing)}], unexpected: [{string.Join(", ", unexpected)}]");

        return expectedColumns.ToDictionary(name => name, reader.GetOrdinal, StringComparer.OrdinalIgnoreCase);
    }
}
