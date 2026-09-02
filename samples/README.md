# FileGateway 클라이언트 샘플

Python(`requests`)과 C#(`HttpClient`)로 작성한 유즈케이스별 예제입니다. 전체 API 계약은 [`../docs/05-api-interface.md`](../docs/05-api-interface.md)가 기준입니다.

## 공통 전제

두 언어 모두 아래 환경변수로 접속 정보를 읽습니다.

```bash
export FILEGATEWAY_URL=https://gateway.example
export FILEGATEWAY_API_KEY=...
```

## 유즈케이스 목록

| # | 시나리오 | Python | C# |
|---|---|---|---|
| 1 | 전체 설비 목록 → 첫 설비 제공 파일 종류 조회 | `scenarios/01_file_types.py` | `Scenarios/FileTypesScenario.cs` |
| 2 | 로그 목록 + continuationToken 전체 페이지 순회 | `scenarios/02_logs_list_pagination.py` | `Scenarios/LogsListPaginationScenario.cs` |
| 3 | subtype/attribute로 로그 필터 | `scenarios/03_logs_filter_subtype_attributes.py` | `Scenarios/LogsFilterScenario.cs` |
| 4 | 로그 조건 기반 직접 다운로드 (+ 409 fallback) | `scenarios/04_logs_direct_download.py` | `Scenarios/LogsDirectDownloadScenario.cs` |
| 5 | fileId로 metadata 조회 후 streaming 다운로드 | `scenarios/05_files_download_by_id.py` | `Scenarios/FilesDownloadByIdScenario.cs` |
| 6 | Current Configuration 조회/다운로드 (다중 파일) | `scenarios/06_configurations_current.py` | `Scenarios/ConfigurationsCurrentScenario.cs` |
| 7 | Configuration History 조회 | `scenarios/07_configurations_history.py` | `Scenarios/ConfigurationsHistoryScenario.cs` |
| 8 | 오류 code 전체 분기 매트릭스 | `scenarios/08_error_handling_matrix.py` | `Scenarios/ErrorHandlingMatrixScenario.cs` |

모든 시나리오는 `equipmentId=EQ-001` 등 예시 값을 사용합니다. 실제 환경의 설비/로그종류로 바꿔서 실행하세요.

## Python 실행

```bash
cd samples/python
pip install -r requirements.txt
python scenarios/01_file_types.py
```

공통 로직은 `filegateway_client.py`(`FileGatewayClient`)에 있습니다. 오류는 `FileGatewayError`(`.code`/`.status`/`.trace_id`)로 던져집니다.

## C# 실행

```bash
cd samples/csharp
dotnet run -- file-types
dotnet run -- logs-pagination
# ... Program.cs의 scenarios 목록 참조
```

공통 로직은 `FileGatewayClient.cs`에 있습니다. 오류는 `FileGatewayException`(`.Code`/`.Status`/`.TraceId`)으로 던져집니다.

## 공통 패턴

- **인증**: `X-Api-Key` header. query string/URL에 API Key를 넣지 않습니다.
- **오류 분기**: `title`/`detail` 텍스트가 아니라 `code`로 분기합니다 (`FileNotFound`, `MultipleFilesMatched`, `FileIdExpired` 등).
- **streaming 다운로드**: 파일 전체를 메모리에 올리지 않고 청크 단위로 디스크에 씁니다. `Content-Length`와 실제로 받은 바이트 수를 비교해 잘린 다운로드(streaming 시작 후 원격 I/O 오류)를 감지합니다.
- **fileId**: opaque token, 24시간 TTL, query parameter로 전달(URL 세그먼트 길이 제한 회피).
- **pagination**: `limit + continuationToken`. 토큰을 유지한 채 조회조건(equipmentId/logType/from/to/subtype/attr.\*)을 바꾸면 `400 InvalidRequest`입니다.
- **경로 안전성**: 서버가 응답한 `fileName`을 로컬 경로에 그대로 쓰지 않고 파일명 부분만 취해서 씁니다(경로요소 제거).
