using FileGateway.Configurations;
using FileGateway.Configurations.Internal;
using FileGateway.Configurations.Tokens;
using FileGateway.Core.Errors;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Infrastructure.Tokens;
using FileGateway.Logs.Tokens;
using FileGateway.UnitTests.TestUtils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.UnitTests.Configurations;

public class ConfigurationQueryServiceTests
{
    private static readonly ITokenCodec Codec = new DataProtectionTokenCodec(
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    private static readonly DateTimeOffset From = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset To = new(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9));

    private static ReferenceDataSnapshot Snapshot()
        => ReferenceDataSnapshotBuilder.Build(new(
            ["EQ-001"], [new RawServer("SRV1", "ftp1", "ftproot")], [],
            [new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
                "PM/current", "PM*.cfg",
                "PM/history/{yyyy}/{MM}/{dd}", "PM*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")]));

    private static ConfigurationQueryService Service(FakeFileAccess ftp)
        => new(new FixedView(Snapshot()), new CurrentResolver(ftp), new HistoryResolver(ftp), ftp,
               Codec, TimeProvider.System, TimeSpan.FromDays(366), 50,
               TimeSpan.FromHours(24), TimeSpan.FromMinutes(30));

    private static void SeedSnapshot(FakeFileAccess ftp, int day, params string[] files)
    {
        var d = $"PM/history/2026/08/{day:00}";
        foreach (var f in files) ftp.AddFile($"{d}/{f}", new byte[f.Length]);
        if (files.Length > 0) ftp.AddFile($"{d}/_DONE", []);
    }

    [Fact]
    public async Task History_requires_range_via_service_validation()
    {
        var svc = Service(new FakeFileAccess());

        // from >= to
        var reversed = await Assert.ThrowsAsync<FileGatewayException>(() => svc.GetHistoryAsync(
            new ConfigurationHistoryQuery("EQ-001", "PM", To, From, null, null), CancellationToken.None));
        Assert.Equal("InvalidRequest", reversed.Code);

        // to - from > HistoryMaxQueryRange(366일)
        var tooWide = await Assert.ThrowsAsync<FileGatewayException>(() => svc.GetHistoryAsync(
            new ConfigurationHistoryQuery("EQ-001", "PM", From, From.AddDays(367), null, null),
            CancellationToken.None));
        Assert.Equal("InvalidRequest", tooWide.Code);
    }

    [Fact]
    public async Task History_paginates_and_allows_limit_change()
    {
        var ftp = new FakeFileAccess();
        SeedSnapshot(ftp, 22, "PM1.cfg", "PM2.cfg", "PM3.cfg");
        var svc = Service(ftp);

        var q = new ConfigurationHistoryQuery("EQ-001", "PM", From, To, 2, null);
        var p1 = await svc.GetHistoryAsync(q, CancellationToken.None);
        Assert.Equal(["PM1.cfg", "PM2.cfg"], p1.Items.Select(i => i.FileName)); // 동일 ts → fileName ASC
        Assert.All(p1.Items, i => Assert.False(string.IsNullOrEmpty(i.FileId)));
        Assert.All(p1.Items, i => Assert.Equal(22, i.SnapshotTimestamp.Day));
        Assert.NotNull(p1.ContinuationToken);

        // limit은 페이지 크기 — 페이지마다 변경 가능
        var p2 = await svc.GetHistoryAsync(q with { Limit = 1, ContinuationToken = p1.ContinuationToken },
            CancellationToken.None);
        var last = Assert.Single(p2.Items);
        Assert.Equal("PM3.cfg", last.FileName);
        Assert.Null(p2.ContinuationToken);
    }

    [Fact]
    public async Task History_continuation_condition_change_is_invalid()
    {
        var ftp = new FakeFileAccess();
        SeedSnapshot(ftp, 22, "PM1.cfg", "PM2.cfg");
        var svc = Service(ftp);

        var q = new ConfigurationHistoryQuery("EQ-001", "PM", From, To, 1, null);
        var p1 = await svc.GetHistoryAsync(q, CancellationToken.None);
        Assert.NotNull(p1.ContinuationToken);

        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => svc.GetHistoryAsync(
            q with { EquipmentId = "EQ-002", ContinuationToken = p1.ContinuationToken },
            CancellationToken.None));
        Assert.Equal("InvalidRequest", ex.Code);
    }

    [Fact]
    public async Task Snapshot_fileId_locates_and_rechecks_marker()
    {
        var ftp = new FakeFileAccess();
        SeedSnapshot(ftp, 22, "PM1.cfg");
        var svc = Service(ftp);

        var page = await svc.GetHistoryAsync(
            new ConfigurationHistoryQuery("EQ-001", "PM", From, To, null, null), CancellationToken.None);
        var item = Assert.Single(page.Items);
        var payload = Codec.Unprotect(item.FileId, ConfigurationTokenKinds.FileIdSnapshotPurpose).Payload!;

        var located = await svc.LocateByFileIdAsync(payload, CancellationToken.None);
        Assert.Equal("PM/history/2026/08/22/PM1.cfg", located.RelativePath);
        Assert.Equal("PM1.cfg", located.FileName);
        Assert.Equal("PM1.cfg".Length, located.Size);

        // marker 제거 — Snapshot File이 남아 있어도 미완료 Set은 더 이상 조회 대상이 아니다
        ftp.RemoveFile("PM/history/2026/08/22/_DONE");
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => svc.LocateByFileIdAsync(payload, CancellationToken.None));
        Assert.Equal("FileNotFound", ex.Code);
    }

    [Fact]
    public async Task Current_fileId_points_to_current_content()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/PM1.cfg", "1"u8.ToArray());
        var svc = Service(ftp);

        var item = Assert.Single(await svc.GetCurrentAsync("EQ-001", "PM", CancellationToken.None));
        Assert.Equal(1, item.Size);
        var payload = Codec.Unprotect(item.FileId, ConfigurationTokenKinds.FileIdCurrentPurpose).Payload!;

        // 내용 교체 후 동일 fileId로 다시 해석 — Current fileId는 버전이 아니라 현재 내용을 가리킨다
        ftp.AddFile("PM/current/PM1.cfg", "12345"u8.ToArray());
        var located = await svc.LocateByFileIdAsync(payload, CancellationToken.None);
        Assert.Equal(5, located.Size);
    }

    [Fact]
    public async Task Unknown_purpose_is_invalid_file_id()
    {
        var svc = Service(new FakeFileAccess());
        var payload = new TokenPayload(LogTokenKinds.FileIdPurpose,
            new Dictionary<string, string> { ["equipmentId"] = "EQ-001", ["logType"] = "EventLog" },
            DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => svc.LocateByFileIdAsync(payload, CancellationToken.None));
        Assert.Equal("InvalidFileId", ex.Code);
    }
}
