namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: 조건에 파일이 정확히 1건일 때 목록 조회 없이 바로 다운로드.
/// 2건 이상 일치하면 409 MultipleFilesMatched — 목록 조회로 전환해
/// fileId를 확정한 뒤 공통 다운로드(FilesDownloadByIdScenario)를 사용한다.
public static class LogsDirectDownloadScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        try
        {
            var result = await client.DownloadLogByConditionAsync(
                "EQ-001", "EventLog", ".",
                from: "2026-08-20T09:00:00+09:00", to: "2026-08-20T10:00:00+09:00");
            Console.WriteLine($"saved {result.Path} ({result.Size} bytes)");
            return;
        }
        catch (FileGatewayException ex) when (ex.Code == "FileNotFound")
        {
            Console.WriteLine("no file matched given condition");
            return;
        }
        catch (FileGatewayException ex) when (ex.Code == "MultipleFilesMatched")
        {
            Console.WriteLine("multiple files matched — falling back to list + explicit fileId");
        }

        var page = await client.ListLogsPageAsync(
            "EQ-001", "EventLog", from: "2026-08-20T09:00:00+09:00", to: "2026-08-20T10:00:00+09:00");
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            var result = await client.DownloadByFileIdAsync(
                item.GetProperty("fileId").GetString()!, item.GetProperty("fileName").GetString()!, ".");
            Console.WriteLine($"saved {result.Path} ({result.Size} bytes)");
        }
    }
}
