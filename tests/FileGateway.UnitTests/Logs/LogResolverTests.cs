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
        var files = await new LogResolver(ftp).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);
        var f = Assert.Single(files);
        Assert.Equal("2026082218_Event_A.zip", f.Entry.Name);
        Assert.NotNull(f.Metadata.Timestamp);
    }

    [Fact]
    public async Task Missing_directory_yields_empty_not_error()
    {
        var files = await new LogResolver(new FakeFileAccess())
            .ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None);
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
    public async Task Metadata_parse_failure_is_definition_conflict()
    {
        // glob(Event_*.zip)은 대소문자 무시라 통과, 메타 템플릿 정규식은 대소문자 구분이라 파싱 실패
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/Event_A.ZIP", "x"u8.ToArray());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_names_are_conflict()
    {
        // FakeFileAccess는 경로 키가 대소문자 무시라 이 시나리오를 담을 수 없다 → 고정 listing stub 사용
        var ftp = new ListingFileAccess(new RemoteDirectoryListing(true,
        [
            new RemoteFileEntry("Event_A.zip", 1),
            new RemoteFileEntry("event_a.ZIP", 2),
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
        var files = await new LogResolver(ftp).ResolveAsync(Def(), range, CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.Equal(["Logs/2026/08/22/18/Event_A.zip", "Logs/2026/08/22/17/Event_A.zip"],
            files.Select(f => f.RelativePath)); // timestamp DESC
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
        var files = await new LogResolver(ftp).ResolveAsync(def, range, CancellationToken.None);
        Assert.Single(files); // 18시 파일만 (flat 디렉터리 전체 조회 후 timestamp 필터)
        // 네 슬롯(17~20시)이 한 디렉터리로 중복 제거되어도 범위 [From,To) 필터+정렬이 유지된다
        var range2 = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 20, 0, 0, TimeSpan.FromHours(9)));
        var files2 = await new LogResolver(ftp).ResolveAsync(def, range2, CancellationToken.None);
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
        var files = await new LogResolver(ftp).ResolveAsync(def,
            new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), CancellationToken.None);
        Assert.Equal(["Trace_A.log", "Trace_B.log"], files.Select(f => f.Entry.Name));
    }

    private sealed class ThrowingFileAccess : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromException<RemoteDirectoryListing>(new FileAccessException(FileAccessError.ConnectionFailed, "down"));
        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ListingFileAccess(RemoteDirectoryListing listing) : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromResult(listing);
        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
