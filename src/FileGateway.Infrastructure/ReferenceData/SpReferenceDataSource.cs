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
    public async Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = spName;
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var equipments = new List<string>();
        while (await reader.ReadAsync(ct)) equipments.Add(reader.GetString(0));
        await RequireNextResultAsync(reader, "servers", ct);

        var servers = new List<RawServer>();
        while (await reader.ReadAsync(ct))
            servers.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        await RequireNextResultAsync(reader, "logs", ct);

        var logs = new List<RawLogDefinition>();
        while (await reader.ReadAsync(ct))
            logs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9)));
        await RequireNextResultAsync(reader, "configurationDefinitions", ct);
        RequireFieldCount(reader, "configurationDefinitions", expected: 13);

        var configs = new List<RawConfigurationDefinition>();
        while (await reader.ReadAsync(ct))
            configs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
                reader.GetString(12)));

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

    private static void RequireFieldCount(SqlDataReader reader, string resultSet, int expected)
    {
        if (reader.FieldCount != expected)
            throw new FileGatewayException("ReferenceDataIncomplete",
                $"reference data result set '{resultSet}' has expected {expected} columns, actual {reader.FieldCount}");
    }
}
