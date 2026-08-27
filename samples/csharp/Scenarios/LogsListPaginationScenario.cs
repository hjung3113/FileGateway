namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: Hourly/Daily 로그 목록을 continuationToken으로 전체 페이지 순회.
/// 조건(equipmentId/logType/from/to/subtype/attr.*)을 바꾸려면 토큰 없이
/// 첫 페이지부터 새로 조회해야 한다(섞으면 400 InvalidRequest).
public static class LogsListPaginationScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        var total = 0;
        await foreach (var item in client.IterateAllLogsAsync(
            "EQ-001",
            "EventLog",
            from: "2026-08-20T00:00:00+09:00",
            to: "2026-08-21T00:00:00+09:00",
            limit: 50)) // 페이지 크기는 페이지마다 바꿔도 됨(조건 아님)
        {
            total++;
            Console.WriteLine($"{item.GetProperty("timestamp")}  {item.GetProperty("fileName")}  {item.GetProperty("size")}B");
        }

        Console.WriteLine($"total {total} files");
    }
}
