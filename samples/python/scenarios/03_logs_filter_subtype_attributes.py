"""유즈케이스: subtype / 동적 attribute로 로그 좁혀서 조회.

subtype과 attr.<name> 값은 정확한 문자열 일치(case-sensitive)로 비교된다.
attribute.<key> 매핑 이름공간과 attr.<name> query prefix는 서로 다른
이름공간이므로 값 하나가 두 API 모두에서 같은 이름이라고 가정하지 않는다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient


def main() -> None:
    client = FileGatewayClient()

    page = client.list_logs_page(
        "EQ-001",
        "TraceLog",
        subtype="Warning",
        attributes={"line": "L1", "station": "ST3"},
        limit=100,
    )

    for item in page["items"]:
        print(f"{item['fileName']}  subtype={item['subtype']}  attrs={item['attributes']}")

    if page["continuationToken"]:
        print("more pages available — same subtype/attributes 유지한 채 continuationToken으로 이어서 조회")


if __name__ == "__main__":
    main()
