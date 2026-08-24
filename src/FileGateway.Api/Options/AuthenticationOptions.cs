// src/FileGateway.Api/Options/AuthenticationOptions.cs
namespace FileGateway.Api.Options;

/// <summary>"Authentication" 섹션. API Key 원문은 환경변수 등 Secret으로 공급한다(appsettings 비밀 금지).</summary>
public sealed class AuthenticationOptions
{
    public List<ApiKeyEntry> ApiKeys { get; set; } = [];
}

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = "";
    public string CallerId { get; set; } = "";
}
