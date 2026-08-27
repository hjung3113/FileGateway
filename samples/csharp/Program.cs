using FileGateway.Samples;
using FileGateway.Samples.Scenarios;

var scenarios = new Dictionary<string, Func<FileGatewayClient, Task>>
{
    ["file-types"] = client => FileTypesScenario.RunAsync(client),
    ["logs-pagination"] = LogsListPaginationScenario.RunAsync,
    ["logs-filter"] = LogsFilterScenario.RunAsync,
    ["logs-direct-download"] = LogsDirectDownloadScenario.RunAsync,
    ["files-download-by-id"] = FilesDownloadByIdScenario.RunAsync,
    ["configurations-current"] = ConfigurationsCurrentScenario.RunAsync,
    ["configurations-history"] = ConfigurationsHistoryScenario.RunAsync,
    ["error-handling"] = ErrorHandlingMatrixScenario.RunAsync,
};

if (args.Length != 1 || !scenarios.TryGetValue(args[0], out var run))
{
    Console.WriteLine("usage: dotnet run -- <scenario>");
    Console.WriteLine("scenarios: " + string.Join(", ", scenarios.Keys));
    return 1;
}

using var client = new FileGatewayClient();
await run(client);
return 0;
