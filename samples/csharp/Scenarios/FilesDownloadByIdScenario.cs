namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: 목록에서 얻은 fileId로 metadata 확인 후 streaming 다운로드.
/// fileId는 24시간 유효한 opaque token이다. 삭제/만료 시 오류 code로
/// 원인을 구분할 수 있다.
public static class FilesDownloadByIdScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        var page = await client.ListLogsPageAsync("EQ-001", "EventLog");
        var items = page.GetProperty("items");
        if (items.GetArrayLength() == 0)
        {
            Console.WriteLine("no matching log");
            return;
        }

        var first = items[0];
        var fileId = first.GetProperty("fileId").GetString()!;

        try
        {
            var meta = await client.GetFileMetadataAsync(fileId);
            Console.WriteLine($"metadata: {meta.GetProperty("fileName")} ({meta.GetProperty("size")} bytes)");

            var result = await client.DownloadByFileIdAsync(fileId, meta.GetProperty("fileName").GetString()!, ".");
            Console.WriteLine($"saved {result.Path} ({result.Size} bytes)");
        }
        catch (FileGatewayException ex) when (ex.Code == "FileIdExpired")
        {
            Console.WriteLine("fileId expired (24h TTL) — re-list to get a fresh one");
        }
        catch (FileGatewayException ex) when (ex.Code is "LogDefinitionNotFound" or "ConfigurationDefinitionNotFound")
        {
            Console.WriteLine($"reference data deleted: {ex.Code}");
        }
        catch (FileGatewayException ex) when (ex.Code == "FileNotFound")
        {
            Console.WriteLine("logical file no longer exists on remote storage");
        }
    }
}
