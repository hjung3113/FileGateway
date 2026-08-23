// src/FileGateway.Infrastructure/ReferenceData/SpReferenceDataSource.cs
using System.Data;
using Microsoft.Data.SqlClient;

namespace FileGateway.Infrastructure.ReferenceData;

public sealed class SpReferenceDataSource(string connectionString) : IReferenceDataSource
{
    public async Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.FileGateway_GetReferenceData";
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var equipments = new List<string>();
        while (await reader.ReadAsync(ct)) equipments.Add(reader.GetString(0));
        await reader.NextResultAsync(ct);

        var servers = new List<RawServer>();
        while (await reader.ReadAsync(ct))
            servers.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct);

        var logs = new List<RawLogDefinition>();
        while (await reader.ReadAsync(ct))
            logs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9)));
        await reader.NextResultAsync(ct);

        var configs = new List<RawConfigurationDefinition>();
        while (await reader.ReadAsync(ct))
            configs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));

        return new(equipments, servers, logs, configs);
    }
}
