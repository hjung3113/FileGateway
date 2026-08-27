"""유즈케이스: 설비가 제공하는 logType/configurationType 조회.

호출 전 실제 어떤 로그/Configuration을 조회 가능한지 확인할 때 사용한다.
FTP를 스캔하지 않고 기준정보 snapshot만 반환하므로 빠르다.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # filegateway_client.py는 상위 디렉터리

from filegateway_client import FileGatewayClient, FileGatewayError


def main(equipment_id: str) -> None:
    client = FileGatewayClient()
    try:
        result = client.get_file_types(equipment_id)
    except FileGatewayError as err:
        if err.code == "EquipmentNotFound":
            raise SystemExit(f"no such equipment: {equipment_id}") from err
        raise

    print(f"equipment {result['equipmentId']}:")
    for log in result["logs"]:
        print(f"  log: {log['logType']} ({log['generationType']})")
    for cfg in result["configurations"]:
        print(f"  configuration: {cfg['configurationType']}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "EQ-001")
