using System.Net.Http.Json;
using System.Text.Json;

namespace FileGateway.IntegrationTests.Api;

/// <summary>
/// Logs/Configurations 엔드포인트는 HttpRequest 수동 파싱을 쓰기 때문에, 명시적으로 선언하지 않으면
/// 쿼리 파라미터가 OpenAPI 문서에서 통째로 누락된다(Issue #19-4). 회귀 방지: 각 엔드포인트의
/// 필수/선택 쿼리 파라미터가 /openapi/v1.json에 실제로 나타나는지 검증한다.
/// </summary>
public sealed class OpenApiExposureTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OpenApiExposureTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/logs", "equipmentId", true)]
    [InlineData("/api/v1/logs", "logType", true)]
    [InlineData("/api/v1/logs", "from", false)]
    [InlineData("/api/v1/logs", "limit", false)]
    [InlineData("/api/v1/logs/download", "equipmentId", true)]
    [InlineData("/api/v1/configurations/current", "equipmentId", true)]
    [InlineData("/api/v1/configurations/current", "configurationType", true)]
    [InlineData("/api/v1/configurations/current/download", "equipmentId", true)]
    [InlineData("/api/v1/configurations/history", "from", true)]
    [InlineData("/api/v1/configurations/history", "to", true)]
    [InlineData("/api/v1/configurations/history", "limit", false)]
    [InlineData("/api/v1/files", "fileId", true)]
    [InlineData("/api/v1/files/download", "fileId", true)]
    public async Task Query_parameter_is_declared_with_expected_required_flag(
        string path, string parameterName, bool expectedRequired)
    {
        var document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var parameters = document.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("parameters");

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
}
