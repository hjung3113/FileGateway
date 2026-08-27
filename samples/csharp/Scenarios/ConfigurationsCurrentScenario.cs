namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: Current Configuration File 집합 조회/다운로드.
/// 같은 equipmentId+configurationType 아래 PM1~PM4처럼 여러 파일이 있을
/// 수 있다. 직접 다운로드는 파일이 정확히 1개일 때만 성공한다.
public static class ConfigurationsCurrentScenario
{
    public static async Task RunAsync(FileGatewayClient client)
    {
        var items = await client.ListCurrentConfigurationsAsync("EQ-001", "PM");
        foreach (var item in items.EnumerateArray())
        {
            Console.WriteLine(
                $"{item.GetProperty("fileName")}  {item.GetProperty("size")}B  fileId={item.GetProperty("fileId")}");
        }

        try
        {
            var result = await client.DownloadCurrentConfigurationAsync("EQ-001", "PM", ".");
            Console.WriteLine($"single file downloaded: {result.Path}");
        }
        catch (FileGatewayException ex) when (ex.Code == "MultipleFilesMatched")
        {
            Console.WriteLine("multiple current files exist — download each by fileId:");
            foreach (var item in items.EnumerateArray())
            {
                var r = await client.DownloadByFileIdAsync(
                    item.GetProperty("fileId").GetString()!, item.GetProperty("fileName").GetString()!, ".");
                Console.WriteLine($"  saved {r.Path} ({r.Size} bytes)");
            }
        }
        catch (FileGatewayException ex) when (ex.Code == "FileNotFound")
        {
            Console.WriteLine("no current configuration file for this equipment/type");
        }
    }
}
