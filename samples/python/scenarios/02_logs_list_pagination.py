"""유즈케이스: Hourly/Daily 로그 목록을 continuationToken으로 전체 페이지 순회.

같은 조회조건을 유지한 채 다음 페이지를 요청하는 패턴을 보여준다.
조건(equipmentId/logType/from/to/subtype/attr.*)을 바꾸려면 토큰 없이
첫 페이지부터 새로 조회해야 한다(섞으면 400 InvalidRequest).
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient


def main() -> None:
    client = FileGatewayClient()

    total = 0
    for item in client.iter_all_logs(
        "EQ-001",
        "EventLog",
        from_="2026-08-20T00:00:00+09:00",
        to="2026-08-21T00:00:00+09:00",
        limit=50,  # 페이지 크기는 페이지마다 바꿔도 됨(조건 아님)
    ):
        total += 1
        print(f"{item['timestamp']}  {item['fileName']}  {item['size']}B")

    print(f"total {total} files")


if __name__ == "__main__":
    main()
