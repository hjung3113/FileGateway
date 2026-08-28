using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileGateway.Core.Files;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.UnitTests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FileGateway.Api.Downloading;
using Microsoft.AspNetCore.Http;

namespace FileGateway.IntegrationTests.Api;

public class LogEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    // factory: snapshot(EQ-001/EventLog Hourly flat + TraceLog Continuous), FakeFileAccess 기반 IFileAccess 등록
    // FakeFileAccess 시드: Logs/all/2026082218_Event.zip 등
    public LogEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        factory.SetSnapshot(Snapshot());
        factory.SetFileAccess(FakeFtp());
    }

    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/all", "*_Event*.zip", "Multiple", "Template",
                "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]"),
            new RawLogDefinition("EQ-001", "TraceLog", "SRV1", "Continuous",
                "Trace/current", "Trace_*.zip", "Multiple", "Template",
                "Trace/current/Trace_{subtype}.zip", "[]"),
        ],
        []));

    private static FakeFileAccess FakeFtp()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "event-18"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "event-17"u8.ToArray());
        ftp.AddFile("Logs/all/2026082210_Event.zip", "event-10"u8.ToArray());
        ftp.AddFile("Trace/current/Trace_alpha.zip", "trace-alpha"u8.ToArray());
        return ftp;
    }

    private async Task<JsonElement> GetJson(string path)
    {
        using var response = await _factory.CreateClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string code, int status)> GetError(string path)
    {
        using var response = await _factory.CreateClient().GetAsync(path);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("code").GetString()!, (int)response.StatusCode);
    }

    [Fact]
    public async Task List_returns_envelope_with_camel_case_fields()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
        var item = body.GetProperty("items")[0];
        foreach (var field in new[] { "fileId", "fileName", "equipmentId", "logType", "subtype", "timestamp", "size", "isContinuous", "attributes" })
            Assert.True(item.TryGetProperty(field, out _), $"missing {field}");
    }

    [Fact]
    public async Task Empty_result_is_items_empty_token_null()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2020-01-01T00:00:00%2B09:00&to=2020-01-02T00:00:00%2B09:00");
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.Null(body.GetProperty("continuationToken").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task Missing_required_params_is_400_InvalidRequest()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/v1/logs?equipmentId=EQ-001");
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("InvalidRequest", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Continuous_with_from_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00:00:00%2B09:00")).code);

    [Fact]
    public async Task Limit_above_max_is_400()
        => Assert.Equal("InvalidRequest", (await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit={1001}")).code);

    [Fact]
    public async Task Bad_timestamp_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=yesterday")).code);

    [Fact]
    public async Task Pagination_walks_pages_and_allows_limit_change()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = p1.GetProperty("continuationToken").GetString();
        Assert.NotNull(token);
        var p2 = await GetJson($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=2&continuationToken={Uri.EscapeDataString(token!)}");
        Assert.True(p2.GetProperty("items").GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Continuation_with_changed_condition_is_400()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = Uri.EscapeDataString(p1.GetProperty("continuationToken").GetString()!);
        var error = await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&subtype=X&continuationToken={token}");
        Assert.Equal("InvalidRequest", error.code);
    }

    [Fact]
    public async Task AC_18_1_Download_single_match_streams_with_headers()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(response.Content.Headers.ContentLength, bytes.Length);
    }



    [Fact]
    public async Task Download_audit_log_carries_fileId()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new ApiFactory(s => s.AddSingleton<ILoggerProvider>(logs));
        factory.SetSnapshot(Snapshot());
        factory.SetFileAccess(FakeFtp());
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00");
        Assert.Equal(200, (int)response.StatusCode);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        var fileId = Regex.Match(entry.Message, @"fileId (\S+) fileName").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(fileId), $"audit message missing fileId: {entry.Message}");
        var fileSize = Regex.Match(entry.Message, @"fileSize (\S+) status").Groups[1].Value;
        Assert.True(long.TryParse(fileSize, out var size) && size > 0, $"audit message missing positive fileSize: {entry.Message}");
    }

    [Fact]
    public async Task Download_audit_records_open_time_size_not_resolve_time_size()
    {
        const int resolveSize = 999, actualSize = 8;
        var logs = new CollectingLoggerProvider();
        using var factory = new ApiFactory(s => s.AddSingleton<ILoggerProvider>(logs));
        factory.SetSnapshot(Snapshot());
        factory.UseFakeFtp(ftp =>
        {
            ftp.AddFile("Logs/all/2026082218_Event.zip", new byte[actualSize]);
            ftp.OverrideListingSize("Logs/all/2026082218_Event.zip", resolveSize); // resolve/open 사이 크기 변동 race
        });
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(actualSize, response.Content.Headers.ContentLength);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        var fileSize = long.Parse(Regex.Match(entry.Message, @"fileSize (\S+) status").Groups[1].Value);
        Assert.Equal(actualSize, fileSize); // open 시점 크기, 목록 시점 크기 아님
    }

    [Fact]
    public async Task Error_body_has_no_physical_path()
    {
        using var response = await _factory.CreateClient()
            .GetAsync("/api/v1/logs?equipmentId=EQ-001&logType=Nope");
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ftp1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ftproot", json, StringComparison.Ordinal);
    }

    // 설계 §5: 명시 범위(3파일 매치 — 기본 24h 창은 2매치뿐)를 사용해야 한다.
    private const string ThreeFileRange =
        "/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T10%3A00%3A00%2B09%3A00&to=2026-08-22T19%3A00%3A00%2B09%3A00";

    private static async Task<ZipArchive> ReadZipAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }

    [Fact]
    public async Task AC_18_2_AC_18_7_Download_multiple_match_streams_zip()
    {
        using var response = await _factory.CreateClient().GetAsync(ThreeFileRange);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        using var zip = await ReadZipAsync(response);
        // 엔트리 순서 = 목록 정렬(timestamp DESC), 엔트리명 = 원본 FileName, 내용 = seed 원문
        Assert.Equal(
            ["2026082218_Event.zip", "2026082217_Event.zip", "2026082210_Event.zip"],
            zip.Entries.Select(e => e.FullName).ToArray());
        await using (var s = zip.Entries[0].Open())
            Assert.Equal("event-18", await new StreamReader(s).ReadToEndAsync());
        await using (var s = zip.Entries[2].Open())
            Assert.Equal("event-10", await new StreamReader(s).ReadToEndAsync());
    }

    [Fact]
    public async Task AC_18_7_Download_zip_is_streamed_without_content_length()
    {
        // TestServer는 응답 body를 완전히 버퍼링해 클라이언트 측 Content-Length를 재구성하므로,
        // 전송 계층과 무관한 앱 계약을 직접 검증한다: zip 총 크기를 사전에 알 수 없어
        // 응답 시작 전 ContentLength를 설정하지 않는다(사전 전체 크기 계산 코드 없음 → chunked).
        var ftp = FakeFtp();
        var server = new FileServerConnection("SRV1", "ftp1", "ftproot");
        var files = new[] { "Logs/all/2026082218_Event.zip", "Logs/all/2026082217_Event.zip", "Logs/all/2026082210_Event.zip" }
            .Select(p => new LocatedFile(server, p, Path.GetFileName(p), 0)).ToList();
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await new ZipDownloadResult("logs.zip", files, ftp).ExecuteAsync(ctx);

        Assert.Null(ctx.Response.ContentLength);
        ctx.Response.Body.Position = 0;
        using var zip = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(3, zip.Entries.Count); // 파일별 순차 복사로 완전한 zip 생성
    }

    // 슬롯별 디렉터리({HH}) 템플릿: 다른 슬롯의 동일 이름 + case-only 차이 이름 충돌
    private static ReferenceDataSnapshot ConflictSnapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "HourEvent", "SRV1", "Hourly",
                "Logs/hourly/{yyyy}/{MM}/{dd}/{HH}", "Event.*", "Multiple", "Template",
                "Logs/hourly/{yyyy}/{MM}/{dd}/{HH}/{subtype}", "[]"),
        ],
        []));

    private static void SeedConflictFiles(FakeFileAccess ftp)
    {
        ftp.AddFile("Logs/hourly/2026/08/22/18/Event.zip", "hour-18"u8.ToArray());
        ftp.AddFile("Logs/hourly/2026/08/22/17/Event.zip", "hour-17"u8.ToArray());
        ftp.AddFile("Logs/hourly/2026/08/22/10/event.ZIP", "hour-10"u8.ToArray());
    }

    private const string ConflictRange =
        "/api/v1/logs/download?equipmentId=EQ-001&logType=HourEvent&from=2026-08-22T10%3A00%3A00%2B09%3A00&to=2026-08-22T19%3A00%3A00%2B09%3A00";

    [Fact]
    public async Task AC_18_2_Download_zip_entry_name_conflict_gets_suffix()
    {
        using var factory = new ApiFactory();
        factory.SetSnapshot(ConflictSnapshot());
        factory.SetFileAccess(SeedAndReturn());
        using var response = await factory.CreateClient().GetAsync(ConflictRange);
        Assert.Equal(200, (int)response.StatusCode);
        using var zip = await ReadZipAsync(response);
        // 첫 등장은 원본명, 이후 중복은 case-insensitive 판정으로 _N suffix(ListAsync 순서로 결정적).
        // 세 번째 원본명은 event.ZIP(case-only 변형, 별도 슬롯) — suffix는 그 파일 자체의 stem/ext로 만든다(§2.3).
        Assert.Equal(
            ["Event.zip", "Event_2.zip", "event_3.ZIP"],
            zip.Entries.Select(e => e.FullName).ToArray());

        static FakeFileAccess SeedAndReturn()
        {
            var ftp = new FakeFileAccess();
            SeedConflictFiles(ftp);
            return ftp;
        }
    }

    [Fact]
    public async Task AC_18_4_Download_limit_caps_zip_entries_and_limit_above_max_is_400()
    {
        using var response = await _factory.CreateClient().GetAsync(ThreeFileRange + "&limit=2");
        Assert.Equal(200, (int)response.StatusCode);
        using var zip = await ReadZipAsync(response);
        Assert.Equal(2, zip.Entries.Count); // 매치 3건 중 limit=2로 제한

        var error = await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&limit=1001");
        Assert.Equal(400, error.status);
        Assert.Equal("InvalidRequest", error.code);
    }

    [Fact]
    public async Task AC_18_5_Download_continuous_with_from_is_400_InvalidRequest()
        => Assert.Equal("InvalidRequest",
            (await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00%3A00%3A00%2B09%3A00")).code);

    [Fact]
    public async Task AC_18_6_Download_uses_same_match_set_as_list()
    {
        // 1건: 목록 items 수 = 단일 다운로드, fileName 일치
        var single = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18%3A00%3A00%2B09%3A00&to=2026-08-22T19%3A00%3A00%2B09%3A00");
        Assert.Equal(1, single.GetProperty("items").GetArrayLength());
        using var singleDownload = await _factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18%3A00%3A00%2B09%3A00&to=2026-08-22T19%3A00%3A00%2B09%3A00");
        Assert.Contains("2026082218_Event.zip", singleDownload.Content.Headers.ContentDisposition?.FileName);

        // N건: 목록 items 수 = zip 엔트리 수
        var multi = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T10%3A00%3A00%2B09%3A00&to=2026-08-22T19%3A00%3A00%2B09%3A00");
        Assert.Equal(3, multi.GetProperty("items").GetArrayLength());
        using var zipDownload = await _factory.CreateClient().GetAsync(ThreeFileRange);
        using var zip = await ReadZipAsync(zipDownload);
        Assert.Equal(3, zip.Entries.Count);
    }

    [Fact]
    public async Task AC_18_2_Download_zip_second_open_failure_aborts_response_with_audit()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new ApiFactory(s => s.AddSingleton<ILoggerProvider>(logs));
        factory.SetSnapshot(Snapshot());
        var inner = new FakeFileAccess();
        inner.AddFile("Logs/all/2026082218_Event.zip", "event-18"u8.ToArray());
        inner.AddFile("Logs/all/2026082217_Event.zip", "event-17"u8.ToArray());
        inner.AddFile("Logs/all/2026082210_Event.zip", "event-10"u8.ToArray());
        factory.SetFileAccess(new NthOpenFailFileAccess(inner, failOnNthOpen: 2)); // 2번째 엔트리 open 실패

        using var client = factory.CreateClient();
        using var response = await client.GetAsync(ThreeFileRange, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(200, (int)response.StatusCode); // zip 헤더 전송은 이미 시작

        // 본문은 완전한 zip이 아님: 읽기가 실패하거나, 끝까지 읽혀도 3엔트리 zip으로 파싱 불가
        var complete = false;
        try
        {
            using var zip = await ReadZipAsync(response);
            complete = zip.Entries.Count == 3;
        }
        catch (Exception) { /* 응답 중단으로 truncated zip / 연결 리셋 */ }
        Assert.False(complete, "second-file open failure must not produce a complete 3-entry zip");

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        var errorCode = Regex.Match(entry.Message, @"errorCode (\S+) ").Groups[1].Value;
        if (errorCode.Length == 0) errorCode = Regex.Match(entry.Message, @"errorCode (\S+)$").Groups[1].Value;
        Assert.Equal("FileServerUnavailable", errorCode);
    }

    [Fact]
    public async Task AC_18_2_Download_zip_audit_records_zip_name_and_total_size()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new ApiFactory(s => s.AddSingleton<ILoggerProvider>(logs));
        factory.SetSnapshot(Snapshot());
        factory.SetFileAccess(FakeFtp());
        using var response = await factory.CreateClient().GetAsync(ThreeFileRange);
        Assert.Equal(200, (int)response.StatusCode);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        Assert.EndsWith(".zip", Regex.Match(entry.Message, @"fileName (\S+) fileSize").Groups[1].Value);
        // seed 총 바이트: "event-18"(8) + "event-17"(8) + "event-10"(8) = 24
        Assert.Equal(24, long.Parse(Regex.Match(entry.Message, @"fileSize (\S+) status").Groups[1].Value));
    }
}
