"""유즈케이스: 조건에 맞는 로그를 목록 조회 없이 바로 다운로드.

1건이면 단일 파일, 2건 이상이면 zip(Content-Type: application/zip)으로 내려오므로
응답 Content-Type으로 구분한다. zip은 Content-Length가 없고, 최대 `limit`건(기본 100,
최대 1000)만 담기며 다음 페이지 token이 없다. 파일을 하나씩 받으려면 목록 조회로
fileId를 확정한 뒤 공통 다운로드(05_files_download_by_id.py)를 사용한다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient, FileGatewayError


def main() -> None:
    client = FileGatewayClient()

    try:
        result = client.download_log_by_condition(
            "EQ-001",
            "EventLog",
            dest_dir=".",
            from_="2026-08-20T09:00:00+09:00",
            to="2026-08-20T10:00:00+09:00",
        )
    except FileGatewayError as err:
        if err.code == "FileNotFound":
            raise SystemExit("no file matched given condition") from err
        raise

    if result.content_type.startswith("application/zip"):
        print(f"multiple files matched — saved as zip {result.path} ({result.size} bytes)")
        print("zip holds at most `limit` entries (default 100, max 1000) — narrow from/to if you need more")
    else:
        print(f"saved {result.path} ({result.size} bytes)")


if __name__ == "__main__":
    main()
