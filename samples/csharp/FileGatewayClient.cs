using System.Net.Http.Json;
using System.Text.Json;

namespace FileGateway.Samples;

/// 서버가 반환한 Problem Details 오류(code/title/status/traceId).
public sealed class FileGatewayException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public string? TraceId { get; }

    public FileGatewayException(int status, string code, string title, string? traceId)
        : base($"{code}: {title} (status={status}, traceId={traceId})")
    {
        Status = status;
        Code = code;
        TraceId = traceId;
    }

    public static async Task<FileGatewayException> FromResponseAsync(HttpResponseMessage resp)
    {
        // IIS/ARR 레벨 502/503 등은 JSON이 아니거나 object가 아닐 수 있다 —
        // 문자열로 먼저 읽고 방어적으로 파싱한다.
        var raw = await resp.Content.ReadAsStringAsync();
        try
        {
            var problem = JsonSerializer.Deserialize<JsonElement>(raw);
            if (problem.ValueKind != JsonValueKind.Object)
                throw new JsonException("error body is not a JSON object");

            return new FileGatewayException(
                (int)resp.StatusCode,
                problem.TryGetProperty("code", out var c) ? c.GetString() ?? "Unknown" : "Unknown",
                problem.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                problem.TryGetProperty("traceId", out var tr) ? tr.GetString() : null);
        }
        catch (JsonException)
        {
            return new FileGatewayException((int)resp.StatusCode, "NonJsonResponse", raw, null);
        }
    }
}

public sealed record DownloadResult(string Path, long Size);

public sealed class FileGatewayClient : IDisposable
{
    private readonly HttpClient _http;

    public FileGatewayClient(string? baseUrl = null, string? apiKey = null)
    {
        baseUrl ??= Environment.GetEnvironmentVariable("FILEGATEWAY_URL")
            ?? throw new InvalidOperationException("FILEGATEWAY_URL not set");
        apiKey ??= Environment.GetEnvironmentVariable("FILEGATEWAY_API_KEY")
            ?? throw new InvalidOperationException("FILEGATEWAY_API_KEY not set");

        // trailing '/'가 없으면 base에 path prefix가 있을 때(예: https://host/gw) 상대 URL이
        // prefix를 버리고 루트로 해석된다(System.Uri 결합 규칙).
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        // 기본 100초 timeout은 헤더 수신뿐 아니라 body 스트림 전체 읽기에도 적용되어
        // 느린 링크의 대용량 다운로드를 중간에 끊는다. 개별 상한은 호출자가 필요시 CancellationToken으로 건다.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    // --- 설비 catalog ---

    public Task<JsonElement> ListEquipmentsAsync() =>
        GetJsonAsync("/api/v1/equipments");

    public Task<JsonElement> GetFileTypesAsync(string equipmentId) =>
        GetJsonAsync($"/api/v1/equipments/{Uri.EscapeDataString(equipmentId)}/file-types");

    // --- 로그 목록 (페이지 단위) ---

    public Task<JsonElement> ListLogsPageAsync(
        string equipmentId,
        string logType,
        string? from = null,
        string? to = null,
        string? subtype = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        int? limit = null,
        string? continuationToken = null)
    {
        var query = new List<string>
        {
            $"equipmentId={Uri.EscapeDataString(equipmentId)}",
            $"logType={Uri.EscapeDataString(logType)}",
        };
        if (from is not null) query.Add($"from={Uri.EscapeDataString(from)}");
        if (to is not null) query.Add($"to={Uri.EscapeDataString(to)}");
        if (subtype is not null) query.Add($"subtype={Uri.EscapeDataString(subtype)}");
        foreach (var (name, value) in attributes ?? new Dictionary<string, string>())
            query.Add($"attr.{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        if (limit is not null) query.Add($"limit={limit}");
        if (continuationToken is not null) query.Add($"continuationToken={Uri.EscapeDataString(continuationToken)}");

        return GetJsonAsync($"/api/v1/logs?{string.Join('&', query)}");
    }

    /// 전체 페이지를 순회하며 item을 하나씩 내보낸다. 조회조건은 고정, 페이지마다 재사용.
    public async IAsyncEnumerable<JsonElement> IterateAllLogsAsync(
        string equipmentId,
        string logType,
        string? from = null,
        string? to = null,
        string? subtype = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        int? limit = null)
    {
        string? token = null;
        while (true)
        {
            var page = await ListLogsPageAsync(equipmentId, logType, from, to, subtype, attributes, limit, token);
            foreach (var item in page.GetProperty("items").EnumerateArray())
                yield return item;

            var next = page.GetProperty("continuationToken");
            if (next.ValueKind == JsonValueKind.Null) yield break;
            token = next.GetString();
        }
    }

    // --- 로그 조건 기반 직접 다운로드 ---

    public Task<DownloadResult> DownloadLogByConditionAsync(
        string equipmentId, string logType, string destDir, string? from = null, string? to = null)
    {
        var query = $"equipmentId={Uri.EscapeDataString(equipmentId)}&logType={Uri.EscapeDataString(logType)}";
        if (from is not null) query += $"&from={Uri.EscapeDataString(from)}";
        if (to is not null) query += $"&to={Uri.EscapeDataString(to)}";
        return DownloadAsync($"/api/v1/logs/download?{query}", destDir, "download.bin");
    }

    // --- 공통 fileId 조회/다운로드 ---

    public Task<JsonElement> GetFileMetadataAsync(string fileId) =>
        GetJsonAsync($"/api/v1/files?fileId={Uri.EscapeDataString(fileId)}");

    public Task<DownloadResult> DownloadByFileIdAsync(string fileId, string fileName, string destDir) =>
        DownloadAsync($"/api/v1/files/download?fileId={Uri.EscapeDataString(fileId)}", destDir, fileName);

    // --- Current Configuration ---

    public Task<JsonElement> ListCurrentConfigurationsAsync(string equipmentId, string configurationType) =>
        GetJsonAsync(
            $"/api/v1/configurations/current?equipmentId={Uri.EscapeDataString(equipmentId)}"
            + $"&configurationType={Uri.EscapeDataString(configurationType)}");
        // Current는 {items,continuationToken} envelope 없는 단순 배열이다.

    public Task<DownloadResult> DownloadCurrentConfigurationAsync(
        string equipmentId, string configurationType, string destDir) =>
        DownloadAsync(
            $"/api/v1/configurations/current/download?equipmentId={Uri.EscapeDataString(equipmentId)}"
            + $"&configurationType={Uri.EscapeDataString(configurationType)}",
            destDir,
            "current.bin");

    // --- Configuration History ---

    public Task<JsonElement> ListHistoryPageAsync(
        string equipmentId,
        string configurationType,
        string from,
        string to,
        int? limit = null,
        string? continuationToken = null)
    {
        var query = $"equipmentId={Uri.EscapeDataString(equipmentId)}"
            + $"&configurationType={Uri.EscapeDataString(configurationType)}"
            + $"&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
        if (limit is not null) query += $"&limit={limit}";
        if (continuationToken is not null) query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        return GetJsonAsync($"/api/v1/configurations/history?{query}");
    }

    // --- 내부 구현 ---

    private async Task<JsonElement> GetJsonAsync(string relativeUrl)
    {
        // BaseAddress가 path prefix를 가질 수 있으므로 선행 '/'는 제거한다 — 그대로 두면
        // System.Uri 결합 규칙상 base의 prefix를 버리고 루트로 해석된다.
        using var resp = await _http.GetAsync(relativeUrl.TrimStart('/'));
        if (!resp.IsSuccessStatusCode)
            throw await FileGatewayException.FromResponseAsync(resp);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<DownloadResult> DownloadAsync(string relativeUrl, string destDir, string fallbackName)
    {
        // Unix에서 Path.GetFileName은 '\'를 구분자로 보지 않는다. 서버 fileName에 경로요소가
        // 섞여 와도 로컬 경로를 벗어나지 않도록 두 구분자 모두 제거한 뒤 파일명만 취한다.
        var safeName = System.IO.Path.GetFileName(fallbackName.Replace('\\', '/'));
        var destPath = System.IO.Path.Combine(destDir, safeName);

        using var resp = await _http.GetAsync(relativeUrl.TrimStart('/'), HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
            throw await FileGatewayException.FromResponseAsync(resp);

        var expected = resp.Content.Headers.ContentLength;
        long written;
        await using (var remoteStream = await resp.Content.ReadAsStreamAsync())
        await using (var fileStream = System.IO.File.Create(destPath))
        {
            await remoteStream.CopyToAsync(fileStream);
            written = fileStream.Length;
        }

        // Content-Length는 서버가 보낸 "예정" 크기다. 스트림 시작 후 끊긴 다운로드를 놓치지 않으려면
        // 실제로 기록된 바이트 수와 비교해야 한다. 잘린 파일을 정상 파일로 오인하지 않도록 남기지 않는다.
        if (expected is { } n && written != n)
        {
            System.IO.File.Delete(destPath);
            throw new IOException($"truncated download: expected {n} bytes, got {written}");
        }

        return new DownloadResult(destPath, written);
    }

    public void Dispose() => _http.Dispose();
}
