using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using FileGateway.IntegrationTests.Ftp;
using FluentFTP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FileGateway.IntegrationTests.Api;

/// <summary>
/// 실제 스택 E2E: MSSQL 컨테이너(SP) + ReferenceDataCache + FluentFTP 클라이언트 + in-proc FTP 서버 +
/// DataProtection 토큰 코덱. 서비스 오버라이드 없음 — Program.cs의 실제 DI 구성을 그대로 사용한다.
/// </summary>
public class EndToEndTests(DatabaseFixture db, FtpAdapterFixture ftp)
    : IClassFixture<DatabaseFixture>, IClassFixture<FtpAdapterFixture>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        // 시드는 테스트마다 재실행된다(xUnit 클래스 인스턴스 per test) — DELETE 후 INSERT로 멱등화.
        await DbSeedAsync();
        await FtpSeedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceData"] = db.ConnectionString,
                ["Authentication:ApiKeys:0:Key"] = "e2e-key",
                ["Authentication:ApiKeys:0:CallerId"] = "e2e-caller",
                ["FileGateway:Ftp:UserName"] = FtpAdapterFixture.UserName,
                ["FileGateway:Ftp:Password"] = FtpAdapterFixture.Password,
                ["FileGateway:Ftp:HostPortOverride"] = ftp.Port.ToString(),
                ["FileGateway:ReferenceData:CacheTtl"] = "00:00:01",
            }));
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Full_flow_catalog_list_download_history_marker_removal()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "e2e-key");

        // 1) catalog — FTP 접근 없이 기준정보 투영
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(2, catalog.GetProperty("logs").GetArrayLength());

        // 2) 로그 목록 → fileId → metadata → download.
        // 실제 시계(TimeProvider.System)를 쓰므로 기본 2일 창 대신 시드 슬롯을 명시적으로 지정한다.
        var list = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var fileId = list.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        var meta = await client.GetFromJsonAsync<JsonElement>($"/api/v1/files?fileId={Uri.EscapeDataString(fileId)}");
        Assert.Equal("2026082218_Event.zip", meta.GetProperty("fileName").GetString());
        using var download = await client.GetAsync($"/api/v1/files/download?fileId={Uri.EscapeDataString(fileId)}");
        Assert.True(download.IsSuccessStatusCode, $"download failed: {(int)download.StatusCode}");
        Assert.Equal(100, download.Content.Headers.ContentLength);

        // 3) Continuous from 거부
        Assert.Equal(400, (int)(await client.GetAsync("/api/v1/logs?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00:00:00%2B09:00")).StatusCode);

        // 4) Current 다운로드 409(Multiple)
        Assert.Equal(409, (int)(await client.GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM")).StatusCode);

        // 5) History marker 제거 후 fileId → 404
        var history = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-22T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var snapshotId = history.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        // marker 제거 전 정상 해석(200)을 먼저 확인해야 404 assertion이 인과적이 된다.
        Assert.Equal(200, (int)(await client.GetAsync($"/api/v1/files?fileId={Uri.EscapeDataString(snapshotId)}")).StatusCode);
        await FtpDeleteAsync("ftproot/PM/history/2026/08/22/_DONE");
        Assert.Equal(404, (int)(await client.GetAsync($"/api/v1/files?fileId={Uri.EscapeDataString(snapshotId)}")).StatusCode);
    }

    [Fact]
    public async Task New_log_type_appears_after_cache_refresh()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "e2e-key");
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(2, before.GetProperty("logs").GetArrayLength());

        await db.ExecuteAsync(@"INSERT dbo.FgLogDefinition VALUES('EQ-001','AlarmLog','SRV1','Daily',
            'Alarms/{yyyy}/{MM}/{dd}','Alarm_*.log','Multiple','Template',
            'Alarms/{yyyy}/{MM}/{dd}/Alarm_{subtype}.log','[]');");

        // CacheTtl=1초: TTL 경과 후 첫 요청은 stale 반환 + background refresh(serve-stale, 확정 결정 14).
        // refresh 완료 후 요청부터 새 정의가 보인다 — 유계 폴링으로 반영을 기다린다(코드 변경 없이).
        JsonElement after = default;
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(200);
            after = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
            if (after.GetProperty("logs").GetArrayLength() == 3) break;
        }
        Assert.Equal(3, after.GetProperty("logs").GetArrayLength());
    }

    // DB 시드: EQ-001(EventLog Hourly flat, TraceLog Continuous, PM config), SRV1 → 127.0.0.1.
    // RootPath 'ftproot'는 FtpFileAccess가 절대 경로를 '/'+RootPath/rel로 루팅하므로 FTP 시드 접두사와 일치해야 한다.
    private Task DbSeedAsync() => db.ExecuteAsync(@"
        DELETE dbo.FgLogDefinition;
        DELETE dbo.FgConfigurationDefinition;
        DELETE dbo.FgEquipment;
        DELETE dbo.FgServer;
        INSERT dbo.FgEquipment VALUES('EQ-001');
        INSERT dbo.FgServer VALUES('SRV1','127.0.0.1','ftproot');
        INSERT dbo.FgLogDefinition VALUES('EQ-001','EventLog','SRV1','Hourly',
            'Logs/all','*.zip','Multiple','Template',
            'Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip','[]');
        INSERT dbo.FgLogDefinition VALUES('EQ-001','TraceLog','SRV1','Continuous',
            'Trace/cur','Trace_*.log','Single','Template',
            'Trace/cur/Trace_{subtype}.log','[]');
        INSERT dbo.FgConfigurationDefinition
            (EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, CurrentFileNamePattern,
             HistoryDirectoryTemplate, HistoryFileNamePattern, HistoryCompletionMarkerPathTemplate)
            VALUES('EQ-001','PM','SRV1',
            'PM/current','PM_*.cfg','PM/history/{yyyy}/{MM}/{dd}','PM_*.cfg',
            'PM/history/{yyyy}/{MM}/{dd}/_DONE');");

    // FTP 시드: Logs/all/2026082218_Event.zip(100B), Trace/cur/Trace_PM.log,
    // PM/current/PM_1.cfg·PM_2.cfg, PM/history/2026/08/22/{PM_1.cfg,PM_2.cfg,_DONE}
    private async Task FtpSeedAsync()
    {
        using var client = new AsyncFtpClient("127.0.0.1", FtpAdapterFixture.UserName, FtpAdapterFixture.Password, ftp.Port);
        await client.Connect();
        await UploadAsync(client, "ftproot/Logs/all/2026082218_Event.zip", new byte[100]);
        await UploadAsync(client, "ftproot/Trace/cur/Trace_PM.log", "trace"u8.ToArray());
        await UploadAsync(client, "ftproot/PM/current/PM_1.cfg", "cfg1"u8.ToArray());
        await UploadAsync(client, "ftproot/PM/current/PM_2.cfg", "cfg2"u8.ToArray());
        await UploadAsync(client, "ftproot/PM/history/2026/08/22/PM_1.cfg", "cfg1"u8.ToArray());
        await UploadAsync(client, "ftproot/PM/history/2026/08/22/PM_2.cfg", "cfg2"u8.ToArray());
        await UploadAsync(client, "ftproot/PM/history/2026/08/22/_DONE", ""u8.ToArray());
        await client.Disconnect();
    }

    private static async Task UploadAsync(AsyncFtpClient client, string path, byte[] content)
        => await client.UploadStream(new MemoryStream(content), path, createRemoteDir: true);

    private async Task FtpDeleteAsync(string path)
    {
        using var client = new AsyncFtpClient("127.0.0.1", FtpAdapterFixture.UserName, FtpAdapterFixture.Password, ftp.Port);
        await client.Connect();
        await client.DeleteFile(path);
        await client.Disconnect();
    }
}
