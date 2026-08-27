namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: 설비가 제공하는 logType/configurationType 조회.
/// FTP를 스캔하지 않고 기준정보 snapshot만 반환하므로 빠르다.
public static class FileTypesScenario
{
    public static async Task RunAsync(FileGatewayClient client, string equipmentId = "EQ-001")
    {
        System.Text.Json.JsonElement result;
        try
        {
            result = await client.GetFileTypesAsync(equipmentId);
        }
        catch (FileGatewayException ex) when (ex.Code == "EquipmentNotFound")
        {
            Console.WriteLine($"no such equipment: {equipmentId}");
            return;
        }

        Console.WriteLine($"equipment {result.GetProperty("equipmentId").GetString()}:");
        foreach (var log in result.GetProperty("logs").EnumerateArray())
            Console.WriteLine($"  log: {log.GetProperty("logType").GetString()} ({log.GetProperty("generationType").GetString()})");
        foreach (var cfg in result.GetProperty("configurations").EnumerateArray())
            Console.WriteLine($"  configuration: {cfg.GetProperty("configurationType").GetString()}");
    }
}
