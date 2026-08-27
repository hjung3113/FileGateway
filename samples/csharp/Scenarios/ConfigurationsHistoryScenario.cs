namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: Configuration Snapshot History를 날짜 범위로 조회.
/// from/to는 필수(생략 시 400 InvalidRequest). marker 없는 미완료
/// Snapshot Set은 결과에 나타나지 않는다 — 목록에 있는 항목은 항상 완료된 것이다.
public static class ConfigurationsHistoryScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        var page = await client.ListHistoryPageAsync(
            "EQ-001", "PM",
            from: "2026-08-01T00:00:00+09:00", to: "2026-08-24T00:00:00+09:00",
            limit: 100);

        // snapshotTimestamp가 같은 항목들은 같은 날짜 폴더에서 복사된 한 Snapshot Set이다.
        var bySnapshot = new SortedDictionary<string, List<System.Text.Json.JsonElement>>(
            Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a)));

        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            var ts = item.GetProperty("snapshotTimestamp").GetString()!;
            if (!bySnapshot.TryGetValue(ts, out var list))
                bySnapshot[ts] = list = new List<System.Text.Json.JsonElement>();
            list.Add(item);
        }

        foreach (var (snapshotTs, files) in bySnapshot)
        {
            Console.WriteLine($"snapshot {snapshotTs}: {files.Count} files");
            foreach (var f in files)
                Console.WriteLine($"  {f.GetProperty("fileName")}  {f.GetProperty("size")}B");
        }

        if (page.GetProperty("continuationToken").ValueKind != System.Text.Json.JsonValueKind.Null)
            Console.WriteLine("more history pages — same equipmentId/configurationType/from/to로 이어서 조회");
    }
}
