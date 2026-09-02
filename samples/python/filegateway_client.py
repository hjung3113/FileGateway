"""FileGateway API 공통 클라이언트.

인증 header, 오류(Problem Details) 매핑, streaming download 헬퍼를 제공한다.
scenarios/*.py는 이 모듈만 재사용하고 requests 세부사항을 직접 다루지 않는다.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any, Iterator
from urllib.parse import quote

import requests


class FileGatewayError(Exception):
    """서버가 반환한 Problem Details 오류(code/title/status/traceId)."""

    def __init__(self, status: int, code: str, title: str, trace_id: str | None):
        super().__init__(f"{code}: {title} (status={status}, traceId={trace_id})")
        self.status = status
        self.code = code
        self.title = title
        self.trace_id = trace_id

    @classmethod
    def from_response(cls, resp: requests.Response) -> "FileGatewayError":
        # IIS/ARR 레벨 502/503 등은 JSON이 아니거나 object가 아닐 수 있다 —
        # 실패해도 원인 파악 가능하게 방어적으로 파싱한다.
        try:
            body = resp.json()
            if not isinstance(body, dict):
                raise ValueError("error body is not a JSON object")
            return cls(
                status=resp.status_code,
                code=body.get("code", "Unknown"),
                title=body.get("title", ""),
                trace_id=body.get("traceId"),
            )
        except ValueError:
            return cls(
                status=resp.status_code,
                code="NonJsonResponse",
                title=resp.text[:200],
                trace_id=None,
            )


@dataclass
class DownloadResult:
    path: str
    size: int


class FileGatewayClient:
    def __init__(self, base_url: str | None = None, api_key: str | None = None):
        self.base_url = (base_url or os.environ["FILEGATEWAY_URL"]).rstrip("/")
        self._headers = {"X-Api-Key": api_key or os.environ["FILEGATEWAY_API_KEY"]}

    def _get_json(self, path: str, params: dict[str, Any], timeout: float = 30) -> dict:
        resp = requests.get(f"{self.base_url}{path}", headers=self._headers, params=params, timeout=timeout)
        if not resp.ok:
            raise FileGatewayError.from_response(resp)
        return resp.json()

    # --- 설비 catalog ---

    def list_equipments(self) -> dict:
        return self._get_json("/api/v1/equipments", {})

    def get_file_types(self, equipment_id: str) -> dict:
        # path segment이므로 '/' 등도 인코딩해야 세그먼트 경계를 벗어나지 않는다.
        return self._get_json(f"/api/v1/equipments/{quote(equipment_id, safe='')}/file-types", {})

    # --- 로그 목록 (페이지 단위, 호출자가 continuationToken을 직접 관리) ---

    def list_logs_page(
        self,
        equipment_id: str,
        log_type: str,
        *,
        from_: str | None = None,
        to: str | None = None,
        subtype: str | None = None,
        attributes: dict[str, str] | None = None,
        limit: int | None = None,
        continuation_token: str | None = None,
    ) -> dict:
        params: dict[str, Any] = {"equipmentId": equipment_id, "logType": log_type}
        if from_ is not None:
            params["from"] = from_
        if to is not None:
            params["to"] = to
        if subtype is not None:
            params["subtype"] = subtype
        for name, value in (attributes or {}).items():
            params[f"attr.{name}"] = value
        if limit is not None:
            params["limit"] = limit
        if continuation_token is not None:
            params["continuationToken"] = continuation_token
        return self._get_json("/api/v1/logs", params)

    def iter_all_logs(self, equipment_id: str, log_type: str, **kwargs: Any) -> Iterator[dict]:
        """전체 페이지를 순회하며 item을 하나씩 내보낸다. 조회조건은 kwargs로 고정, 페이지마다 재사용."""
        token = kwargs.pop("continuation_token", None)
        while True:
            page = self.list_logs_page(equipment_id, log_type, continuation_token=token, **kwargs)
            yield from page["items"]
            token = page["continuationToken"]
            if token is None:
                return

    # --- 로그 조건 기반 직접 다운로드 ---

    def download_log_by_condition(
        self,
        equipment_id: str,
        log_type: str,
        dest_dir: str,
        *,
        from_: str | None = None,
        to: str | None = None,
    ) -> DownloadResult:
        params: dict[str, Any] = {"equipmentId": equipment_id, "logType": log_type}
        if from_ is not None:
            params["from"] = from_
        if to is not None:
            params["to"] = to
        return self._download("/api/v1/logs/download", params, dest_dir, "download.bin")

    # --- 공통 fileId 조회/다운로드 ---

    def get_file_metadata(self, file_id: str) -> dict:
        return self._get_json("/api/v1/files", {"fileId": file_id})

    def download_by_file_id(self, file_id: str, file_name: str, dest_dir: str) -> DownloadResult:
        return self._download("/api/v1/files/download", {"fileId": file_id}, dest_dir, file_name)

    # --- Current Configuration ---

    def list_current_configurations(self, equipment_id: str, configuration_type: str) -> list[dict]:
        return self._get_json(
            "/api/v1/configurations/current",
            {"equipmentId": equipment_id, "configurationType": configuration_type},
        )  # type: ignore[return-value]  # Current는 envelope 없는 단순 배열

    def download_current_configuration(
        self, equipment_id: str, configuration_type: str, dest_dir: str
    ) -> DownloadResult:
        return self._download(
            "/api/v1/configurations/current/download",
            {"equipmentId": equipment_id, "configurationType": configuration_type},
            dest_dir,
            "current.bin",
        )

    # --- Configuration History ---

    def list_history_page(
        self,
        equipment_id: str,
        configuration_type: str,
        from_: str,
        to: str,
        *,
        limit: int | None = None,
        continuation_token: str | None = None,
    ) -> dict:
        params: dict[str, Any] = {
            "equipmentId": equipment_id,
            "configurationType": configuration_type,
            "from": from_,
            "to": to,
        }
        if limit is not None:
            params["limit"] = limit
        if continuation_token is not None:
            params["continuationToken"] = continuation_token
        return self._get_json("/api/v1/configurations/history", params)

    # --- streaming download 공통 구현 ---

    def _download(self, path: str, params: dict[str, Any], dest_dir: str, fallback_name: str) -> DownloadResult:
        # os.path.basename은 POSIX에서 '\'를 구분자로 보지 않는다. 서버 fileName에 경로요소가
        # 섞여 와도 로컬 경로를 벗어나지 않도록 두 구분자 모두 제거한 뒤 basename을 취한다.
        safe_name = os.path.basename(fallback_name.replace("\\", "/"))
        dest_path = os.path.join(dest_dir, safe_name)
        with requests.get(
            f"{self.base_url}{path}",
            headers=self._headers,
            params=params,
            stream=True,
            timeout=(10, 60),  # (connect, read-per-chunk) — 전체 다운로드 상한 아님
        ) as resp:
            if not resp.ok:
                raise FileGatewayError.from_response(resp)
            expected = int(resp.headers.get("Content-Length", -1))
            written = 0
            with open(dest_path, "wb") as f:
                for chunk in resp.iter_content(chunk_size=1024 * 64):
                    f.write(chunk)
                    written += len(chunk)
            if expected >= 0 and written != expected:
                # 다운로드 시작 후 원격 I/O 오류는 JSON 오류로 전환되지 않고 스트림이 끊긴다.
                # 잘린 파일을 정상 파일로 오인하지 않도록 남기지 않는다.
                os.remove(dest_path)
                raise IOError(f"truncated download: expected {expected} bytes, got {written}")
        return DownloadResult(path=dest_path, size=written)
