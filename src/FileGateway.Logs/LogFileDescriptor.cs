using FileGateway.Core.Files;

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

/// <summary>목록 매치 1건의 API 투영과 물리 위치 쌍. 내부 계약 전용이며 API 응답에 직렬화하지 않는다.</summary>
public sealed record LocatedLogFile(LogFileDescriptor Descriptor, LocatedFile File);
