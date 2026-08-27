"""유즈케이스: 조건에 파일이 정확히 1건일 때 목록 조회 없이 바로 다운로드.

2건 이상 일치하면 409 MultipleFilesMatched — 이때는 목록 조회로 전환해
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
        print(f"saved {result.path} ({result.size} bytes)")
        return
    except FileGatewayError as err:
        if err.code == "MultipleFilesMatched":
            print("multiple files matched — falling back to list + explicit fileId")
        elif err.code == "FileNotFound":
            raise SystemExit("no file matched given condition") from err
        else:
            raise

    page = client.list_logs_page(
        "EQ-001", "EventLog", from_="2026-08-20T09:00:00+09:00", to="2026-08-20T10:00:00+09:00"
    )
    for item in page["items"]:
        result = client.download_by_file_id(item["fileId"], item["fileName"], dest_dir=".")
        print(f"saved {result.path} ({result.size} bytes)")


if __name__ == "__main__":
    main()
