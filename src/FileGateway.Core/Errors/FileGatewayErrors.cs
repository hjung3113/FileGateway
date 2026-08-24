// src/FileGateway.Core/Errors/FileGatewayErrors.cs
namespace FileGateway.Core.Errors;

/// <summary>오류 코드를 담는 도메인 예외. code→(status,title) 사상은 FileGatewayErrors가 제공한다.</summary>
public sealed class FileGatewayException(string code, string? message = null, Exception? inner = null)
    : Exception(message ?? code, inner)
{
    public string Code { get; } = code;
}

/// <summary>오류 코드→(HTTP status, title) 정적 사전. Global Constraints 오류 표와 1:1(변경 금지).</summary>
public static class FileGatewayErrors
{
    private static readonly IReadOnlyDictionary<string, (int Status, string Title)> Codes =
        new Dictionary<string, (int, string)>
        {
            ["InvalidRequest"] = (400, "Invalid request"),
            ["InvalidFileId"] = (400, "Invalid file id"),
            ["InvalidApiKey"] = (401, "Invalid API key"),
            ["EquipmentNotFound"] = (404, "Equipment not found"),
            ["LogDefinitionNotFound"] = (404, "Log definition not found"),
            ["ConfigurationDefinitionNotFound"] = (404, "Configuration definition not found"),
            ["FileNotFound"] = (404, "File not found"),
            ["MultipleFilesMatched"] = (409, "Multiple files matched"),
            ["FileIdExpired"] = (410, "File id expired"),
            ["FileDefinitionConflict"] = (500, "File definition conflict"),
            ["InternalError"] = (500, "Internal server error"),
            ["FileServerUnavailable"] = (502, "File server unavailable"),
            ["FileServerProtocolError"] = (502, "File server protocol error"),
            ["ReferenceDataUnavailable"] = (503, "Reference data unavailable"),
        };

    public static (int Status, string Title) Map(string code)
        => Codes.TryGetValue(code, out var mapped) ? mapped : (500, "Internal server error");
}
