"""유즈케이스: Current Configuration File 집합 조회/다운로드.

같은 equipmentId+configurationType 아래 PM1~PM4처럼 여러 파일이 있을 수
있다. 직접 다운로드는 파일이 정확히 1개일 때만 성공하고, 여러 개면
409 MultipleFilesMatched이므로 목록에서 fileId를 골라야 한다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient, FileGatewayError


def main() -> None:
    client = FileGatewayClient()

    items = client.list_current_configurations("EQ-001", "PM")
    for item in items:
        print(f"{item['fileName']}  {item['size']}B  fileId={item['fileId']}")

    try:
        result = client.download_current_configuration("EQ-001", "PM", dest_dir=".")
        print(f"single file downloaded: {result.path}")
    except FileGatewayError as err:
        if err.code == "MultipleFilesMatched":
            print(f"{len(items)} current files exist — download each by fileId:")
            for item in items:
                r = client.download_by_file_id(item["fileId"], item["fileName"], dest_dir=".")
                print(f"  saved {r.path} ({r.size} bytes)")
        elif err.code == "FileNotFound":
            raise SystemExit("no current configuration file for this equipment/type") from err
        else:
            raise


if __name__ == "__main__":
    main()
