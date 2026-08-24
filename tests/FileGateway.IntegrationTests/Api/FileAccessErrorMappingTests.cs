using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.IntegrationTests.Api;

/// <summary>FileAccessException이 generic 500이 아니라 FileAccessError별 오류 코드/상태로 매핑되는지 검증.</summary>
public class FileAccessErrorMappingTests
{
    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/all", "*_Event*.zip", "Multiple", "Template",
                "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]"),
        ],
        []));

    /// <summary>resolve(목록)는 성공하고 open에서 지정한 FileAccessError로 실패하는 IFileAccess.</summary>
    private sealed class FailingOpenFileAccess(FileAccessError error) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
            => Task.FromResult(new RemoteDirectoryListing(true,
                [new RemoteFileEntry("2026082218_Event.zip", 8)]));

        public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
            => Task.FromResult(8L);

        public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
            => Task.FromResult(true);

        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
            => throw new FileAccessException(error, $"simulated {error}");
    }

    private const string DownloadPath =
        "/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00";

    private static async Task<(string code, int status)> GetError(FileAccessError error)
    {
        using var factory = new ApiFactory();
        factory.SetSnapshot(Snapshot());
        factory.SetFileAccess(new FailingOpenFileAccess(error));
        using var response = await factory.CreateClient().GetAsync(DownloadPath);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("code").GetString()!, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(FileAccessError.ConnectionFailed)]
    [InlineData(FileAccessError.AuthenticationFailed)]
    [InlineData(FileAccessError.Timeout)]
    [InlineData(FileAccessError.IoFailure)]
    public async Task Open_failure_maps_to_502_FileServerUnavailable(FileAccessError error)
    {
        var (code, status) = await GetError(error);
        Assert.Equal("FileServerUnavailable", code);
        Assert.Equal(502, status);
    }

    [Fact]
    public async Task Open_protocol_error_maps_to_502_FileServerProtocolError()
    {
        var (code, status) = await GetError(FileAccessError.ProtocolError);
        Assert.Equal("FileServerProtocolError", code);
        Assert.Equal(502, status);
    }

    [Fact]
    public async Task Open_race_time_FileNotFound_maps_to_404()
    {
        // resolve 시점엔 존재했으나 open 직전 사라진 race: FileNotFound 그대로 404.
        var (code, status) = await GetError(FileAccessError.FileNotFound);
        Assert.Equal("FileNotFound", code);
        Assert.Equal(404, status);
    }
}
