using System.Net.Http.Json;
using System.Text.Json;

namespace FileGateway.IntegrationTests.Api;

/// <summary>
/// Logs/Configurations 엔드포인트는 HttpRequest 수동 파싱을 쓰기 때문에, 명시적으로 선언하지 않으면
/// 쿼리 파라미터가 OpenAPI 문서에서 통째로 누락된다(Issue #19-4). 회귀 방지: 각 엔드포인트가 실제로
/// 받는 쿼리 파라미터 전체(누락도 초과도 없이)가 /openapi/v1.json에 정확히 나타나는지 검증한다.
/// </summary>
public sealed class OpenApiExposureTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OpenApiExposureTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/logs", "equipmentId", true)]
    [InlineData("/api/v1/logs", "logType", true)]
    [InlineData("/api/v1/logs", "from", false)]
    [InlineData("/api/v1/logs", "to", false)]
    [InlineData("/api/v1/logs", "subtype", false)]
    [InlineData("/api/v1/logs", "limit", false)]
    [InlineData("/api/v1/logs", "continuationToken", false)]
    [InlineData("/api/v1/logs/download", "equipmentId", true)]
    [InlineData("/api/v1/logs/download", "logType", true)]
    [InlineData("/api/v1/logs/download", "from", false)]
    [InlineData("/api/v1/logs/download", "to", false)]
    [InlineData("/api/v1/logs/download", "subtype", false)]
    [InlineData("/api/v1/logs/download", "limit", false)]
    [InlineData("/api/v1/logs/download", "continuationToken", false)]
    [InlineData("/api/v1/configurations/current", "equipmentId", true)]
    [InlineData("/api/v1/configurations/current", "configurationType", true)]
    [InlineData("/api/v1/configurations/current/download", "equipmentId", true)]
    [InlineData("/api/v1/configurations/current/download", "configurationType", true)]
    [InlineData("/api/v1/configurations/history", "equipmentId", true)]
    [InlineData("/api/v1/configurations/history", "configurationType", true)]
    [InlineData("/api/v1/configurations/history", "from", true)]
    [InlineData("/api/v1/configurations/history", "to", true)]
    [InlineData("/api/v1/configurations/history", "limit", false)]
    [InlineData("/api/v1/configurations/history", "continuationToken", false)]
    [InlineData("/api/v1/files", "fileId", true)]
    [InlineData("/api/v1/files/download", "fileId", true)]
    public async Task Query_parameter_is_declared_with_expected_required_flag(
        string path, string parameterName, bool expectedRequired)
    {
        var parameters = await GetParametersAsync(path);

        var matches = parameters.EnumerateArray()
            .Where(p => p.GetProperty("name").GetString() == parameterName)
            .ToList();
        Assert.True(matches.Count > 0, $"{path} is missing parameter {parameterName}");
        Assert.True(matches.Count == 1, $"{path} declares {parameterName} more than once");
        var match = matches[0];
        var required = match.TryGetProperty("required", out var r) && r.GetBoolean();
        Assert.Equal(expectedRequired, required);
        Assert.True(
            match.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString()),
            $"{path} parameter {parameterName} has no description");
    }

    // 위 이론이 개별 파라미터를 빠짐없이 검증하더라도, 선언되지 않은 여분의 파라미터가 섞여 들어오는
    // 것은 못 잡는다 — 경로별 파라미터 이름 집합이 정확히 일치하는지 별도로 확인한다.
    [Theory]
    [InlineData("/api/v1/logs", new[] { "equipmentId", "logType", "from", "to", "subtype", "limit", "continuationToken" })]
    [InlineData("/api/v1/logs/download", new[] { "equipmentId", "logType", "from", "to", "subtype", "limit", "continuationToken" })]
    [InlineData("/api/v1/configurations/current", new[] { "equipmentId", "configurationType" })]
    [InlineData("/api/v1/configurations/current/download", new[] { "equipmentId", "configurationType" })]
    [InlineData("/api/v1/configurations/history", new[] { "equipmentId", "configurationType", "from", "to", "limit", "continuationToken" })]
    [InlineData("/api/v1/files", new[] { "fileId" })]
    [InlineData("/api/v1/files/download", new[] { "fileId" })]
    public async Task Path_declares_exactly_the_expected_parameter_set(string path, string[] expectedNames)
    {
        var parameters = await GetParametersAsync(path);
        var actualNames = parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToHashSet();
        Assert.Equal(expectedNames.ToHashSet(), actualNames);
    }

    // attr.<name>=<value>는 동적 키라 고정 파라미터로 선언할 수 없다 — 리터럴 "attr.*" 파라미터로
    // 새는 대신 operation 설명에 규칙이 남아있는지 확인한다.
    [Theory]
    [InlineData("/api/v1/logs")]
    [InlineData("/api/v1/logs/download")]
    public async Task Logs_operation_documents_the_attribute_filter_wildcard_without_a_literal_parameter(string path)
    {
        var document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var get = document.GetProperty("paths").GetProperty(path).GetProperty("get");

        var parameters = get.GetProperty("parameters");
        Assert.DoesNotContain(parameters.EnumerateArray(), p => p.GetProperty("name").GetString() == "attr.*");

        var description = get.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        Assert.Contains("attr.<name>=<value>", description);
    }

    private async Task<JsonElement> GetParametersAsync(string path)
    {
        var document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        return document.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("parameters");
    }
}
