using System.Net.Http.Json;
using System.Text.Json;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.IntegrationTests.Api;

/// <summary>DataProtection 키가 디스크에 유지되어 프로세스/IIS 재시작 후에도 fileId가 유효함을 검증한다.</summary>
public class KeyPersistenceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public KeyPersistenceTests(ApiFactory factory)
    {
        _factory = factory;
        factory.SetSnapshot(ReferenceDataSnapshotBuilder.Build(new(
            ["EQ-001"],
            [new RawServer("SRV1", "ftp1", "ftproot")],
            [
                new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                    "Logs/all", "*_Event*.zip", "Multiple", "Template",
                    "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]"),
            ],
            [])));
        factory.UseFakeFtp(ftp => ftp.AddFile("Logs/all/2026082218_Event.zip", "event-18"u8.ToArray()));
    }

    [Fact]
    public async Task File_id_survives_provider_recreation_with_same_key_directory()
    {
        var keyDir = Path.Combine(Path.GetTempPath(), "fg-e2e-keys-" + Guid.NewGuid());
        _factory.SetDataProtectionKeyDirectory(keyDir);

        var fileId = await _factory.IssueFileIdAsync(); // list → items[0].fileId
        // PersistKeysToFileSystem이 실제 호출됐다는 물리적 증거 — 키가 keyDir에 기록되지 않으면 실패.
        Assert.NotEmpty(Directory.GetFiles(keyDir, "key-*.xml"));
        _factory.RestartApplication(keyDir);            // 같은 key dir으로 호스트 재생성(재시작 시뮬레이션)

        var error = await _factory.GetFileErrorAsync(fileId);
        Assert.Equal(200, error.status);             // 재시작 후 fileId가 여전히 정상 해석됨
        Assert.NotEqual("InvalidFileId", error.code); // 키 유실이 아니면 Invalid/Expired 아님
        Assert.NotEqual("FileIdExpired", error.code);
    }
}
