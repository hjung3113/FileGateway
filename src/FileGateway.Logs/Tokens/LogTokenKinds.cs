// src/FileGateway.Logs/Tokens/LogTokenKinds.cs
namespace FileGateway.Logs.Tokens;

/// <summary>로그 파일 종류별 opaque token purpose 상수.</summary>
public static class LogTokenKinds
{
    public const string FileIdPurpose = "fg.fileid.log";
    public const string ContinuationPurpose = "fg.page.log";
}
