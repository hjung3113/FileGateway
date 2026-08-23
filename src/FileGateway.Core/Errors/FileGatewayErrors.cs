// src/FileGateway.Core/Errors/FileGatewayErrors.cs
namespace FileGateway.Core.Errors;

/// <summary>오류 코드를 담는 도메인 예외. code→(status,title) 사상은 Task 14에서 이 파일에 추가된다.</summary>
public sealed class FileGatewayException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
