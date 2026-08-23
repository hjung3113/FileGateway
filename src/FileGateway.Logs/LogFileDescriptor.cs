namespace FileGateway.Logs;

/// <summary>매칭된 물리 로그 파일 1건의 API 투영. FileId는 Task 11(LogQueryService)이 발급하는 보호 토큰이다.</summary>
public sealed record LogFileDescriptor(
    string FileId,
    string EquipmentId,
    string LogType,
    string? Subtype,
    DateTimeOffset? Timestamp,
    string FileName,
    long Size,
    bool IsContinuous,
    IReadOnlyDictionary<string, string> Attributes);
