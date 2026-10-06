namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: 조건에 맞는 로그를 목록 조회 없이 바로 다운로드.
/// 1건이면 단일 파일, 2건 이상이면 zip(Content-Type: application/zip)으로 내려오므로
/// 응답 Content-Type으로 구분한다. zip은 Content-Length가 없고, 최대 `limit`건(기본 100,
/// 최대 1000)만 담기며 다음 페이지 token이 없다. 파일을 하나씩 받으려면 목록 조회로
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
            if (string.Equals(result.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"multiple files matched — saved as zip {result.Path} ({result.Size} bytes)");
                Console.WriteLine("zip holds at most `limit` entries (default 100, max 1000) — narrow from/to if you need more");
            }
            else
            {
                Console.WriteLine($"saved {result.Path} ({result.Size} bytes)");
            }
        }
        catch (FileGatewayException ex) when (ex.Code == "FileNotFound")
        {
            Console.WriteLine("no file matched given condition");
        }
    }
}
