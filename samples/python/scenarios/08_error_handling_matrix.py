"""유즈케이스: 서버가 반환하는 모든 오류 code를 code 단위로 분기.

code 문자열은 API 안정성 계약의 일부이므로 title/detail 텍스트가 아니라
code로 분기한다. traceId는 서버 로그와 연계할 운영 추적 값이다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient, FileGatewayError

RETRYABLE_CODES = {"FileServerUnavailable", "FileServerProtocolError", "ReferenceDataUnavailable"}


def handle(err: FileGatewayError) -> None:
    print(f"[{err.status}] {err.code}  traceId={err.trace_id}")

    if err.code == "InvalidRequest":
        print("  -> 요청 파라미터/시간범위/continuationToken 조건 확인")
    elif err.code == "InvalidFileId":
        print("  -> fileId 형식/서명 오류, 재조회 필요")
    elif err.code == "InvalidApiKey":
        print("  -> X-Api-Key 누락/불일치, 인증정보 확인")
    elif err.code == "EquipmentNotFound":
        print("  -> equipmentId 오탈자 또는 미등록 설비")
    elif err.code in ("LogDefinitionNotFound", "ConfigurationDefinitionNotFound"):
        print("  -> 기준정보가 삭제됨, fileId 재발급 불가 — 목록부터 새로 조회")
    elif err.code == "FileNotFound":
        print("  -> 논리 파일이 실제로 없음(삭제/이동)")
    elif err.code == "MultipleFilesMatched":
        print("  -> 조건에 2건 이상 일치, 목록 조회로 전환해 fileId 선택")
    elif err.code == "FileIdExpired":
        print("  -> fileId TTL(24h) 경과, 재조회 필요")
    elif err.code == "FileDefinitionConflict":
        print("  -> 기준정보/실제 파일 상태 불일치(운영자 확인 필요), 클라이언트가 재시도해도 해결 안 됨")
    elif err.code in RETRYABLE_CODES:
        print("  -> 일시적 장애 가능성, backoff 후 재시도 고려")
    elif err.code == "InternalError":
        print("  -> 서버 내부 오류, traceId로 운영팀에 보고")
    else:
        print("  -> 알 수 없는 code, 신규 서버 버전 확인 필요")


def main() -> None:
    client = FileGatewayClient()

    for bad_equipment_id in ("EQ-DOES-NOT-EXIST",):
        try:
            client.get_file_types(bad_equipment_id)
        except FileGatewayError as err:
            handle(err)

    try:
        client.list_logs_page("EQ-001", "EventLog", from_="2026-08-21T00:00:00+09:00", to="2026-08-20T00:00:00+09:00")
    except FileGatewayError as err:
        handle(err)  # from >= to -> InvalidRequest


if __name__ == "__main__":
    main()
