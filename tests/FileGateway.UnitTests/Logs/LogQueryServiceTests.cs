using FileGateway.Core.Errors;
using FileGateway.Core.Queries;
using FileGateway.Core.Files;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Infrastructure.Tokens;
using FileGateway.Logs;
using FileGateway.Logs.Tokens;
using FileGateway.UnitTests.TestUtils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.UnitTests.Logs;

public class LogQueryServiceTests
{
    private static readonly ITokenCodec Codec = new DataProtectionTokenCodec(
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    private static readonly IReadOnlyDictionary<string, string> NoAttrs = new Dictionary<string, string>();
    private static readonly DateTimeOffset From = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset To = new(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9));

    private static RawLogDefinition EventLog => new("EQ-001", "EventLog", "SRV1", "Hourly",
        "Logs/all", "*_Event*.zip", "Multiple", "Template",
        "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]");
    private static RawLogDefinition AttrLog => new("EQ-001", "AttrLog", "SRV1", "Hourly",
        "Logs/attr", "*_Event*.zip", "Multiple", "Template",
        "Logs/attr/{yyyy}{MM}{dd}{HH}_Event_{attribute.lot}.zip", "[]");
    private static RawLogDefinition TraceLog => new("EQ-001", "Trace", "SRV1", "Continuous",
        "Trace/current", "Trace_*.zip", "Multiple", "Template",
        "Trace/current/Trace_{subtype}.zip", "[]");

    private static ReferenceDataSnapshot Snapshot(params RawLogDefinition[] extraLogs)
        => ReferenceDataSnapshotBuilder.Build(new(
            ["EQ-001"], [new RawServer("SRV1", "ftp1", "ftproot")],
            [EventLog, .. extraLogs], []));

    private static LogQueryService Service(FakeFileAccess ftp, ReferenceDataSnapshot? snap = null,
        TimeProvider? clock = null)
        => new(new FixedView(snap ?? Snapshot()), ftp, Codec, clock ?? TimeProvider.System,
               TimeSpan.FromDays(31), 50, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30));

    [Fact]
    public async Task List_issues_fileIds_and_paginates()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        var svc = Service(ftp);

        var q = new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        var first = Assert.Single(p1.Items);
        Assert.NotNull(p1.ContinuationToken);
        Assert.Equal("2026082218_Event.zip", first.FileName);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)), first.Timestamp);
        Assert.False(string.IsNullOrEmpty(first.FileId));

        var p2 = await svc.ListAsync(q with { ContinuationToken = p1.ContinuationToken }, CancellationToken.None);
        var second = Assert.Single(p2.Items);
        Assert.Null(p2.ContinuationToken);
        Assert.Equal("2026082217_Event.zip", second.FileName);
    }

    [Fact]
    public async Task Empty_result_is_items_empty_token_null()
    {
        var svc = Service(new FakeFileAccess());
        var q = new LogListQuery("EQ-001", "EventLog", null, null, null, NoAttrs, null, null);
        var page = await svc.ListAsync(q, CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task Unknown_equipment_or_type_is_definition_not_found()
    {
        var svc = Service(new FakeFileAccess());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            svc.ListAsync(new LogListQuery("EQ-X", "EventLog", null, null, null, NoAttrs, null, null),
                CancellationToken.None));
        Assert.Equal("LogDefinitionNotFound", ex.Code);
    }

    [Fact]
    public async Task Subtype_and_attribute_filters_apply_case_sensitively()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        var svc = Service(ftp);

        // Template 메타 패턴에 subtype 없음 → 필터 지정 시 0건 (정확 일치)
        var q = new LogListQuery("EQ-001", "EventLog", From, To, "Nope", NoAttrs, null, null);
        Assert.Empty((await svc.ListAsync(q, CancellationToken.None)).Items);

        var attrFtp = new FakeFileAccess();
        attrFtp.AddFile("Logs/attr/2026082218_Event_7.zip", "1"u8.ToArray());
        var attrSvc = Service(attrFtp, Snapshot(AttrLog));
        Assert.Single((await attrSvc.ListAsync(new LogListQuery("EQ-001", "AttrLog", From, To, null,
            new Dictionary<string, string> { ["lot"] = "7" }, null, null), CancellationToken.None)).Items);
        Assert.Empty((await attrSvc.ListAsync(new LogListQuery("EQ-001", "AttrLog", From, To, null,
            new Dictionary<string, string> { ["lot"] = "8" }, null, null), CancellationToken.None)).Items);
        // 키/값 모두 case-sensitive
        Assert.Empty((await attrSvc.ListAsync(new LogListQuery("EQ-001", "AttrLog", From, To, null,
            new Dictionary<string, string> { ["LOT"] = "7" }, null, null), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Subtype_match_is_case_sensitive()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Trace/current/Trace_alpha.zip", "1"u8.ToArray());
        var svc = Service(ftp, Snapshot(TraceLog));

        Assert.Single((await svc.ListAsync(
            new LogListQuery("EQ-001", "Trace", null, null, "alpha", NoAttrs, null, null),
            CancellationToken.None)).Items);
        Assert.Empty((await svc.ListAsync(
            new LogListQuery("EQ-001", "Trace", null, null, "Alpha", NoAttrs, null, null),
            CancellationToken.None)).Items);
    }

    [Fact]
    public async Task ResolveSingle_maps_zero_one_many()
    {
        var ftp = new FakeFileAccess();
        var svc = Service(ftp);
        var q = new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, null, null);
        var zero = await svc.ResolveSingleAsync(q, CancellationToken.None);
        Assert.Equal(MatchCount.Zero, zero.Count);
        Assert.Null(zero.File);

        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        var one = await svc.ResolveSingleAsync(q, CancellationToken.None);
        Assert.Equal(MatchCount.One, one.Count);
        Assert.NotNull(one.File);
        Assert.Equal("Logs/all/2026082218_Event.zip", one.File.RelativePath);
        Assert.Equal("2026082218_Event.zip", one.File.FileName);
        Assert.Equal(1, one.File.Size);
        Assert.Equal("SRV1", one.File.Server.ServerId);

        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        var many = await svc.ResolveSingleAsync(q, CancellationToken.None);
        Assert.Equal(MatchCount.Many, many.Count);
        Assert.Null(many.File);
    }

    [Fact]
    public async Task FileId_round_trip_locates_file()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "12345"u8.ToArray());
        var svc = Service(ftp);
        var page = await svc.ListAsync(
            new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, null, null),
            CancellationToken.None);
        var fileId = Assert.Single(page.Items).FileId;

        var decoded = Codec.Unprotect(fileId, LogTokenKinds.FileIdPurpose);
        Assert.Equal(TokenValidity.Valid, decoded.Validity);
        var located = await svc.LocateByFileIdAsync(decoded.Payload!, CancellationToken.None);
        Assert.Equal("Logs/all/2026082218_Event.zip", located.RelativePath);
        Assert.Equal(5, located.Size);
    }

    [Fact]
    public async Task FileId_for_missing_file_is_FileNotFound()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        var svc = Service(ftp);
        var page = await svc.ListAsync(
            new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, null, null),
            CancellationToken.None);
        var fileId = Assert.Single(page.Items).FileId;
        ftp.RemoveFile("Logs/all/2026082218_Event.zip"); // 이후 삭제

        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            svc.LocateByFileIdAsync(Codec.Unprotect(fileId, LogTokenKinds.FileIdPurpose).Payload!,
                CancellationToken.None));
        Assert.Equal("FileNotFound", ex.Code);
    }

    [Fact]
    public async Task Continuation_token_bound_to_different_conditions_is_rejected()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        var svc = Service(ftp);

        var q = new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        Assert.NotNull(p1.ContinuationToken);

        var changed = q with { Subtype = "X" };
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            svc.ListAsync(changed with { ContinuationToken = p1.ContinuationToken }, CancellationToken.None));
        Assert.Equal("InvalidRequest", ex.Code);
    }

    [Fact]
    public async Task Continuation_reuses_first_page_effective_range()
    {
        // from/to==null 첫 페이지의 기본 24h 범위가 토큰에 고정되는지: 시계가 크게 진행한 뒤에도
        // 두 번째 페이지가 같은 파일 집합을 반환해야 한다(재계산이면 하한이 17시 파일을 밀어낸다).
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        // 토큰 만료는 codec이 실제 시계로 검사하므로 pageTtl을 넉넉히 잡는다 —
        // 커서의 effective range는 아래 고정 시계로 결정된다.
        var now = new DateTimeOffset(2026, 8, 22, 20, 0, 0, TimeSpan.Zero);
        LogQueryService At(DateTimeOffset clock)
            => new(new FixedView(Snapshot()), ftp, Codec, new FixedTimeProvider(clock),
                TimeSpan.FromDays(31), 50, TimeSpan.FromHours(24), TimeSpan.FromDays(30));
        var q = new LogListQuery("EQ-001", "EventLog", null, null, null, NoAttrs, 1, null);
        var p1 = await At(now).ListAsync(q, CancellationToken.None);
        Assert.Equal("2026082218_Event.zip", Assert.Single(p1.Items).FileName);

        // 17시(KST) 파일의 UTC 시각은 08:00Z — now+16h의 기본 하한(08-23 12:00Z - 24h = 08-22 12:00Z)보다 오래돼
        // 재계산 시 사라진다. 토큰 고정 range라면 그대로 반환된다.
        var p2 = await At(now.AddHours(16)).ListAsync(q with { ContinuationToken = p1.ContinuationToken },
            CancellationToken.None);
        Assert.Equal("2026082217_Event.zip", Assert.Single(p2.Items).FileName);
        Assert.Null(p2.ContinuationToken);
    }

    [Fact]
    public async Task Subtype_empty_string_behaves_as_unspecified_across_pages()
    {
        // 진입부 정규화: "" subtype은 미지정과 같은 의미 — continuation을 ""로 재요청해도
        // 바인딩을 통과하고 필터도 미지정으로 적용되어 결과 집합이 유지된다.
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        var svc = Service(ftp);

        var q = new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        Assert.Equal("2026082218_Event.zip", Assert.Single(p1.Items).FileName);

        var p2 = await svc.ListAsync(q with { Subtype = "", ContinuationToken = p1.ContinuationToken },
            CancellationToken.None);
        Assert.Equal("2026082217_Event.zip", Assert.Single(p2.Items).FileName);
    }

    [Fact]
    public async Task Continuous_log_paginates_by_file_name()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Trace/current/Trace_alpha.zip", "1"u8.ToArray());
        ftp.AddFile("Trace/current/Trace_beta.zip", "2"u8.ToArray());
        var svc = Service(ftp, Snapshot(TraceLog));

        var q = new LogListQuery("EQ-001", "Trace", null, null, null, NoAttrs, 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        var first = Assert.Single(p1.Items);
        Assert.Equal("Trace_alpha.zip", first.FileName);
        Assert.True(first.IsContinuous);
        Assert.Null(first.Timestamp);
        Assert.NotNull(p1.ContinuationToken);

        var p2 = await svc.ListAsync(q with { ContinuationToken = p1.ContinuationToken }, CancellationToken.None);
        Assert.Equal("Trace_beta.zip", Assert.Single(p2.Items).FileName);
        Assert.Null(p2.ContinuationToken);
    }

    [Fact]
    public async Task Equal_timestamps_tie_break_by_file_name_then_continue()
    {
        var ftp = new FakeFileAccess();
        // 동일 timestamp(18시)의 서로 다른 이름 2개 + 더 오래된 파일 1개
        ftp.AddFile("Logs/attr/2026082218_Event_A.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/attr/2026082218_Event_B.zip", "2"u8.ToArray());
        ftp.AddFile("Logs/attr/2026082217_Event_C.zip", "3"u8.ToArray());
        var svc = Service(ftp, Snapshot(AttrLog));

        var q = new LogListQuery("EQ-001", "AttrLog", From, To, null, NoAttrs, 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        Assert.Equal("2026082218_Event_A.zip", Assert.Single(p1.Items).FileName); // 동일 ts → fileName ASC(ci) 첫째

        var p2 = await svc.ListAsync(q with { ContinuationToken = p1.ContinuationToken }, CancellationToken.None);
        Assert.Equal("2026082218_Event_B.zip", Assert.Single(p2.Items).FileName); // 동일 ts 둘째(SkipUntilAfter equal-ts)

        var p3 = await svc.ListAsync(q with { ContinuationToken = p2.ContinuationToken }, CancellationToken.None);
        Assert.Equal("2026082217_Event_C.zip", Assert.Single(p3.Items).FileName); // 이후 오래된 ts
        Assert.Null(p3.ContinuationToken);
    }

    [Fact]
    public async Task Invalid_continuation_token_rejected_before_file_access()
    {
        // 커서 검증이 resolver/FTP 탐색보다 먼저다 — fileAccess가 호출되면 코드 "RemoteAccessFailed"로 실패한다
        var svc = new LogQueryService(new FixedView(Snapshot()), new ExplodingFileAccess(), Codec,
            TimeProvider.System, TimeSpan.FromDays(31), 50, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30));
        var q = new LogListQuery("EQ-001", "EventLog", From, To, null, NoAttrs, null, "garbage-token");
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => svc.ListAsync(q, CancellationToken.None));
        Assert.Equal("InvalidRequest", ex.Code);
    }

    private sealed class ExplodingFileAccess : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
            => throw new FileGatewayException("RemoteAccessFailed", "file access must not happen");
        public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
            => throw new FileGatewayException("RemoteAccessFailed", "file access must not happen");
        public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
            => throw new FileGatewayException("RemoteAccessFailed", "file access must not happen");
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
            => throw new FileGatewayException("RemoteAccessFailed", "file access must not happen");
    }

    [Fact]
    public async Task LocateByFileId_rejects_non_fileid_purpose()
    {
        // Api가 TokenValidity로 선행 처리하더라도 purpose 불일치 방어는 서비스에 남긴다
        var payload = new TokenPayload(LogTokenKinds.ContinuationPurpose,
            new Dictionary<string, string> { ["equipmentId"] = "EQ-001", ["logType"] = "EventLog" },
            DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            Service(new FakeFileAccess()).LocateByFileIdAsync(payload, CancellationToken.None));
        Assert.Equal("InvalidFileId", ex.Code);
    }
}
