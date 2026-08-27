namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: subtype / 동적 attribute로 로그 좁혀서 조회.
/// subtype과 attr.<name> 값은 정확한 문자열 일치(case-sensitive)로 비교된다.
public static class LogsFilterScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        var page = await client.ListLogsPageAsync(
            "EQ-001",
            "TraceLog",
            subtype: "Warning",
            attributes: new Dictionary<string, string> { ["line"] = "L1", ["station"] = "ST3" },
            limit: 100);

        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            Console.WriteLine(
                $"{item.GetProperty("fileName")}  subtype={item.GetProperty("subtype")}  attrs={item.GetProperty("attributes")}");
        }

        if (page.GetProperty("continuationToken").ValueKind != System.Text.Json.JsonValueKind.Null)
            Console.WriteLine("more pages available — same subtype/attributes 유지한 채 continuationToken으로 이어서 조회");
    }
}
