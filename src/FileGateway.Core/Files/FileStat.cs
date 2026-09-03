namespace FileGateway.Core.Files;

/// <summary>StatFileAsync 결과. ActualName은 서버가 응답한 실제 파일명으로,
/// 요청 경로에 쓴 이름과 casing이 다를 수 있다(Windows/IIS FTP는 case-insensitive).</summary>
public sealed record FileStat(long Size, string ActualName);
