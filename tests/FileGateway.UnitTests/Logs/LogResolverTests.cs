using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Time;
using FileGateway.Logs.Definitions;
using FileGateway.Logs.Internal;
using FileGateway.UnitTests.TestUtils;

namespace FileGateway.UnitTests.Logs;

public class LogResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");

    private static ResolvedLogDefinition Def(GenerationType gen = GenerationType.Hourly,
        string pathTemplate = "Logs/{yyyy}/{MM}/{dd}/{HH}", string metaPattern = "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip",
        Cardinality card = Cardinality.Multiple, string filePattern = "Event_*.zip")
        => new(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1", gen,
               new LogDiscoveryRule(pathTemplate, filePattern, card),
               new LogMetadataRule(MetadataMode.Template, metaPattern, [])), Srv);

    private static EffectiveRange Range(int y, int m, int d, int h)
        => new(new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.FromHours(9)),
               new DateTimeOffset(y, m, d, h + 1, 0, 0, TimeSpan.FromHours(9)));

    [Fact]
    public async Task Hourly_flat_directory_multiple_hours_one_listing()
    {
        // flat 구조: pathTemplate 토큰 없음 = 한 디렉터리, 파일명에 시간
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event_A.zip", "x"u8.ToArray());
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip",
            filePattern: "*_Event_*.zip");
        var files = (await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None)).Files;
        var f = Assert.Single(files);
        Assert.Equal("2026082218_Event_A.zip", f.Entry.Name);
        Assert.NotNull(f.Metadata.Timestamp);
    }

    [Fact]
    public async Task Missing_directory_yields_empty_not_error()
    {
        var files = (await new LogResolver(new FakeFileAccess())
            .ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None)).Files;
        Assert.Empty(files);
    }

    [Fact]
    public async Task Ftp_io_failure_fails_whole_request()
    {
        var ex = await Assert.ThrowsAsync<FileAccessException>(() =>
            new LogResolver(new ThrowingFileAccess()).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal(FileAccessError.ConnectionFailed, ex.Error);
    }

    [Fact]
    public async Task File_pattern_matches_non_log_files_but_resolver_returns_parseable_files()
    {
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("Event_A.zip", 1),
            new RemoteFileEntry("Event_backup.txt", 2),
            new RemoteFileEntry("Event_old.gz", 3),
        ]));
        var files = (await new LogResolver(ftp).ResolveAsync(
            Def(filePattern: "Event_*"), Range(2026, 8, 22, 18), CancellationToken.None)).Files;

        var file = Assert.Single(files);
        Assert.Equal("Event_A.zip", file.Entry.Name);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_names_are_conflict()
    {
        // FakeFileAccess는 경로 키가 대소문자 무시라 이 시나리오를 담을 수 없다 → 고정 listing stub 사용.
        // Template 리터럴("Event_"/".zip")은 대소문자를 구분하므로, 두 파일 모두 metadata가 파싱되면서도
        // 이름만 case-only로 다르도록 {subtype} 캡처 문자만 대소문자를 바꾼다.
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("Event_A.zip", 1),
            new RemoteFileEntry("Event_a.zip", 2),
        ]));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Same_basename_in_different_hour_directories_is_not_conflict()
    {
        // 논리 identity는 timestamp + fileName: 서로 다른 시간대 디렉터리의 같은 basename은 별개 파일이다
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/17/Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/Event_A.zip", "y"u8.ToArray());
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(9)));
        var files = (await new LogResolver(ftp).ResolveAsync(Def(), range, CancellationToken.None)).Files;
        Assert.Equal(2, files.Count);
        Assert.Equal(["Logs/2026/08/22/18/Event_A.zip", "Logs/2026/08/22/17/Event_A.zip"],
            files.Select(f => f.RelativePath)); // timestamp DESC
    }

    [Fact]
    public async Task Same_basename_same_timestamp_across_directories_is_conflict()
    {
        // flat/regex 정의: timestamp를 파일명에서 추출하므로 서로 다른 디렉터리의 같은 basename이
        // 같은 (timestamp, fileName ci)로 매핑될 수 있다 — 논리 키 중복은 pagination 커서를
        // 붕괴시키므로 FileDefinitionConflict로 거부한다.
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/17/20260822_1800_Event.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/20260822_1800_Event.zip", "y"u8.ToArray());
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex,
                @"^Logs/\d{4}/\d{2}/\d{2}/\d{2}/(?<ts>\d{8}_\d{4})_Event\.zip$",
                [new MetadataMapping("ts", "timestamp", "yyyyMMdd_HHmm")])), Srv);
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(9)));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(def, range, CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_names_outside_requested_range_are_not_conflict()
    {
        // flat 디렉터리에 여러 슬롯이 모인다 — 17시 case-only 중복 파일이 있어도 18시 조회는
        // 시간범위 필터 후에만 이름 중복을 검사하므로 그 중복과 무관하게 정상 결과를 반환해야 한다.
        // FakeFileAccess는 경로 키가 대소문자 무시라 이 시나리오를 담을 수 없다 → 고정 listing stub 사용
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("2026082217_Event_A.zip", 1),
            new RemoteFileEntry("2026082217_event_a.zip", 2), // 17시 case-only 중복
            new RemoteFileEntry("2026082218_Event_C.zip", 3),
        ]));
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip",
            filePattern: "*_Event_*.zip");

        var files = (await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None)).Files;

        var file = Assert.Single(files);
        Assert.Equal("2026082218_Event_C.zip", file.Entry.Name);
    }

    [Fact]
    public async Task Single_cardinality_with_two_files_in_slot_is_conflict()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/Event_B.zip", "y"u8.ToArray());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(card: Cardinality.Single), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Single_cardinality_ignores_duplicate_files_outside_requested_range()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082217_Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event_B.zip", "y"u8.ToArray());
        ftp.AddFile("Logs/all/2026082218_Event_C.zip", "z"u8.ToArray());
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip",
            Cardinality.Single, "*_Event_*.zip");

        var files = (await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None)).Files;

        var file = Assert.Single(files);
        Assert.Equal("2026082218_Event_C.zip", file.Entry.Name);
    }

    [Fact]
    public async Task Hourly_filters_by_parsed_timestamp_and_sorts_desc()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event_A.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event_B.zip", "2"u8.ToArray());
        ftp.AddFile("Logs/all/2026082220_Event_C.zip", "3"u8.ToArray()); // 경계 배제 증명용(20:00)
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip",
            filePattern: "*_Event_*.zip");
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(9)));
        var files = (await new LogResolver(ftp).ResolveAsync(def, range, CancellationToken.None)).Files;
        Assert.Single(files); // 18시 파일만 (flat 디렉터리 전체 조회 후 timestamp 필터)
        // 네 슬롯(17~20시)이 한 디렉터리로 중복 제거되어도 범위 [From,To) 필터+정렬이 유지된다
        var range2 = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 20, 0, 0, TimeSpan.FromHours(9)));
        var files2 = (await new LogResolver(ftp).ResolveAsync(def, range2, CancellationToken.None)).Files;
        Assert.Equal(["2026082218_Event_A.zip", "2026082217_Event_B.zip"],
            files2.Select(f => f.Entry.Name)); // 반개구간 [From,To): 20:00 파일 제외, timestamp DESC
    }

    [Fact]
    public async Task Continuous_lists_current_slot_sorted_by_name()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Trace/cur/Trace_B.log", "1"u8.ToArray());
        ftp.AddFile("Trace/cur/Trace_A.log", "2"u8.ToArray());
        var def = Def(GenerationType.Continuous, "Trace/cur", "Trace/cur/Trace_{subtype}.log",
            filePattern: "Trace_*.log");
        var files = (await new LogResolver(ftp).ResolveAsync(def,
            new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), CancellationToken.None)).Files;
        Assert.Equal(["Trace_A.log", "Trace_B.log"], files.Select(f => f.Entry.Name));
    }

    [Fact]
    public async Task Deterministic_template_hit_skips_listing_and_returns_file()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/EQ001_2026082218.zip", "x"u8.ToArray());
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Single,
                "EQ001_{yyyy}{MM}{dd}{HH}.zip"),
            // 토큰은 metadata pattern 전체에서 한 번만 등장 가능 — 시간은 파일명에서만 추출하고 디렉터리는 리터럴로 둔다.
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/18/EQ001_{yyyy}{MM}{dd}{HH}.zip", [])), Srv);

        var result = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal("EQ001_2026082218.zip", file.Entry.Name);
        Assert.Empty(result.Misses);
        Assert.Equal(0, ftp.ListFilesCallCount); // LIST 생략, StatFileAsync만 사용
    }

    [Fact]
    public async Task Deterministic_template_miss_reports_FileNotFound_without_throwing()
    {
        var ftp = new FakeFileAccess(); // 파일 없음
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Single,
                "EQ001_{yyyy}{MM}{dd}{HH}.zip"),
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/18/EQ001_{yyyy}{MM}{dd}{HH}.zip", [])), Srv);

        var result = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);

        Assert.Empty(result.Files);
        var miss = Assert.Single(result.Misses);
        Assert.Equal("FileNotFound", miss.Reason);
        Assert.Equal("Logs/2026/08/22/18/EQ001_2026082218.zip", miss.RelativePath);
    }

    [Fact]
    public async Task Deterministic_template_metadata_mismatch_reports_without_throwing()
    {
        // 파일은 존재하지만 metadata rule이 해석한 시각이 요청 슬롯과 다르다 — 설정 오류를 500으로 올리지 않고
        // 기존 LIST 경로의 "파싱 실패 후보 제외"와 동일하게 처리한다.
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/EQ001_2026082218.zip", "x"u8.ToArray());
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Single,
                "EQ001_{yyyy}{MM}{dd}{HH}.zip"),
            // metadata rule이 다른(존재하지 않는) 파일명 리터럴을 기대하도록 해 매칭 실패를 유도
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/18/OTHER_{yyyy}{MM}{dd}{HH}.zip", [])), Srv);

        var result = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);

        Assert.Empty(result.Files);
        var miss = Assert.Single(result.Misses);
        Assert.Equal("MetadataMismatch", miss.Reason);
    }

    [Fact]
    public async Task Deterministic_excludes_files_outside_unaligned_range()
    {
        // 슬롯은 시 경계로 내림된다: from=14:30이면 14:00 슬롯도 열거되지만 그 파일의 timestamp는
        // [14:30, 15:30) 밖 — LIST 경로와 동일한 [From, To) 필터로 제외되며, 정상 필터링이므로
        // 미스로도 기록되지 않는다.
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/14/EQ001_2026082214.zip", "x"u8.ToArray());
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Single,
                "EQ001_{yyyy}{MM}{dd}{HH}.zip"),
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/14/EQ001_{yyyy}{MM}{dd}{HH}.zip", [])), Srv);
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 14, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 15, 30, 0, TimeSpan.FromHours(9)));

        var result = await new LogResolver(ftp).ResolveAsync(def, range, CancellationToken.None);

        Assert.Empty(result.Files);
        // 15:00 슬롯만 파일 부재 미스 — 14:00 파일은 조용히 제외된다.
        var miss = Assert.Single(result.Misses);
        Assert.Equal("FileNotFound", miss.Reason);
        Assert.Equal("Logs/2026/08/22/15/EQ001_2026082215.zip", miss.RelativePath);
    }

    [Fact]
    public async Task Deterministic_file_pattern_mismatch_skips_stat_and_reports_miss()
    {
        // fileNameTemplate이 만든 이름이 filePattern과 불일치하면 LIST 경로와 동일하게 후보에서
        // 제외된다 — 원격 확인(StatFileAsync 왕복)조차 하지 않는다.
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/EQ001_2026082218.zip", "x"u8.ToArray());
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.gz", Cardinality.Single,
                "EQ001_{yyyy}{MM}{dd}{HH}.zip"),
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/18/EQ001_{yyyy}{MM}{dd}{HH}.zip", [])), Srv);

        var result = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);

        Assert.Empty(result.Files);
        var miss = Assert.Single(result.Misses);
        Assert.Equal("FilePatternMismatch", miss.Reason);
        Assert.Equal("Logs/2026/08/22/18/EQ001_2026082218.zip", miss.RelativePath);
        Assert.Equal(0, ftp.StatFileCallCount);
    }

    [Fact]
    public async Task Deterministic_preserves_actual_remote_file_name_casing()
    {
        // Windows/IIS FTP는 case-insensitive라 템플릿이 계산한 casing이 달라도 Stat은 성공한다 —
        // 그래도 응답 fileName은 실제 원격 파일의 casing을 보존해야 한다(04a/06 문서 계약).
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/EQ001_2026082218.zip", "x"u8.ToArray()); // 실제 원격 casing
        var def = new ResolvedLogDefinition(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1",
            GenerationType.Hourly,
            new LogDiscoveryRule("Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", Cardinality.Single,
                "eq001_{yyyy}{MM}{dd}{HH}.ZIP"), // 계산된 casing이 다름
            new LogMetadataRule(MetadataMode.Template, "Logs/2026/08/22/18/eq001_{yyyy}{MM}{dd}{HH}.ZIP", [])), Srv);

        var result = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal("EQ001_2026082218.zip", file.Entry.Name);
        Assert.Equal(1, file.Entry.Size);
        Assert.Empty(result.Misses);
    }

    private sealed class ThrowingFileAccess : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromException<RemoteDirectoryListing>(new FileAccessException(FileAccessError.ConnectionFailed, "down"));
        public Task<RemoteDirectoryNames> ListDirectoriesAsync(
            FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(RemoteDirectoryNames.Missing);
        public Task<FileStat> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ListingFileAccess(RemoteDirectoryListing listing) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(listing);
        public Task<RemoteDirectoryNames> ListDirectoriesAsync(
            FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(RemoteDirectoryNames.Missing);
        public Task<FileStat> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
