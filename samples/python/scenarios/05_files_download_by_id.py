"""유즈케이스: 목록에서 얻은 fileId로 metadata 확인 후 streaming 다운로드.

fileId는 24시간 유효한 opaque token이다. 물리 경로가 바뀌어도 같은 논리
파일이면 그대로 유효하고, 삭제/만료 시 오류 code로 원인을 구분할 수 있다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient, FileGatewayError


def main() -> None:
    client = FileGatewayClient()

    page = client.list_logs_page("EQ-001", "EventLog")
    if not page["items"]:
        raise SystemExit("no matching log")

    file_id = page["items"][0]["fileId"]

    try:
        meta = client.get_file_metadata(file_id)
        print(f"metadata: {meta['fileName']} ({meta['size']} bytes)")

        result = client.download_by_file_id(file_id, meta["fileName"], dest_dir=".")
        print(f"saved {result.path} ({result.size} bytes)")
    except FileGatewayError as err:
        if err.code == "FileIdExpired":
            raise SystemExit("fileId expired (24h TTL) — re-list to get a fresh one") from err
        if err.code in ("LogDefinitionNotFound", "ConfigurationDefinitionNotFound"):
            raise SystemExit(f"reference data deleted: {err.code}") from err
        if err.code == "FileNotFound":
            raise SystemExit("logical file no longer exists on remote storage") from err
        raise


if __name__ == "__main__":
    main()
