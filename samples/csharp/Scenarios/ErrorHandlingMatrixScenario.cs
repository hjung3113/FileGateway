namespace FileGateway.Samples.Scenarios;

/// 유즈케이스: 서버가 반환하는 모든 오류 code를 code 단위로 분기.
/// code 문자열은 API 안정성 계약의 일부이므로 title/detail 텍스트가 아니라
/// code로 분기한다. traceId는 서버 로그와 연계할 운영 추적 값이다.
public static class ErrorHandlingMatrixScenario
{
    private static readonly HashSet<string> RetryableCodes =
        new() { "FileServerUnavailable", "FileServerProtocolError", "ReferenceDataUnavailable" };

    private static void Handle(FileGatewayException ex)
    {
        Console.WriteLine($"[{ex.Status}] {ex.Code}  traceId={ex.TraceId}");

        Console.WriteLine(ex.Code switch
        {
            "InvalidRequest" => "  -> 요청 파라미터/시간범위/continuationToken 조건 확인",
            "InvalidFileId" => "  -> fileId 형식/서명 오류, 재조회 필요",
            "InvalidApiKey" => "  -> X-Api-Key 누락/불일치, 인증정보 확인",
            "EquipmentNotFound" => "  -> equipmentId 오탈자 또는 미등록 설비",
            "LogDefinitionNotFound" or "ConfigurationDefinitionNotFound" =>
                "  -> 기준정보가 삭제됨, fileId 재발급 불가 — 목록부터 새로 조회",
            "FileNotFound" => "  -> 논리 파일이 실제로 없음(삭제/이동)",
            "MultipleFilesMatched" => "  -> 조건에 2건 이상 일치, 목록 조회로 전환해 fileId 선택",
            "FileIdExpired" => "  -> fileId TTL(24h) 경과, 재조회 필요",
            "FileDefinitionConflict" =>
                "  -> 기준정보/실제 파일 상태 불일치(운영자 확인 필요), 클라이언트가 재시도해도 해결 안 됨",
            "InternalError" => "  -> 서버 내부 오류, traceId로 운영팀에 보고",
            _ when RetryableCodes.Contains(ex.Code) => "  -> 일시적 장애 가능성, backoff 후 재시도 고려",
            _ => "  -> 알 수 없는 code, 신규 서버 버전 확인 필요",
        });
    }

    public static async Task RunAsync(FileGatewayClient client)
    {
        try
        {
            await client.GetFileTypesAsync("EQ-DOES-NOT-EXIST");
        }
        catch (FileGatewayException ex)
        {
            Handle(ex);
        }

        try
        {
            await client.ListLogsPageAsync(
                "EQ-001", "EventLog",
                from: "2026-08-21T00:00:00+09:00", to: "2026-08-20T00:00:00+09:00"); // from >= to
        }
        catch (FileGatewayException ex)
        {
            Handle(ex);
        }
    }
}
