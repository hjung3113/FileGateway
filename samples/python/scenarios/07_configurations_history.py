"""유즈케이스: Configuration Snapshot History를 날짜 범위로 조회.

from/to는 필수(생략 시 400 InvalidRequest). marker 없는 미완료 Snapshot
Set은 결과에 나타나지 않는다 — 즉 목록에 있는 항목은 항상 완료된 것이다.
History 전용 직접 다운로드 endpoint는 없으므로 항상 fileId 경유로 받는다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient


def main() -> None:
    client = FileGatewayClient()

    page = client.list_history_page(
        "EQ-001",
        "PM",
        from_="2026-08-01T00:00:00+09:00",
        to="2026-08-24T00:00:00+09:00",
        limit=100,
    )

    # snapshotTimestamp가 같은 항목들은 같은 날짜 폴더에서 복사된 한 Snapshot Set이다.
    by_snapshot: dict[str, list[dict]] = {}
    for item in page["items"]:
        by_snapshot.setdefault(item["snapshotTimestamp"], []).append(item)

    for snapshot_ts, files in sorted(by_snapshot.items(), reverse=True):
        print(f"snapshot {snapshot_ts}: {len(files)} files")
        for f in files:
            print(f"  {f['fileName']}  {f['size']}B")

    if page["continuationToken"]:
        print("more history pages — same equipmentId/configurationType/from/to로 이어서 조회")


if __name__ == "__main__":
    main()
