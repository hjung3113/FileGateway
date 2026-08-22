# API / 클라이언트 인터페이스

## 원칙

- HTTPS
- JSON metadata + streaming download
- API Key 인증
- 실제 서버/FTP 경로 비노출
- 논리 `fileId` 기반 접근
- 로그와 Configuration은 별도 feature API로 구분
- 각 feature의 목록과 조건 기반 직접 접근은 동일 Resolver 규칙을 사용

## API v1

### 로그 목록

```http
GET /api/v1/logs
```

주요 query:

- `equipmentId` (필수)
- `logType` (필수)
- `from`, `to` (선택, 없으면 최근 24시간)
- `subtype` (선택)
- `attr.<name>=<value>` (선택)
- `limit` (선택)
- `continuationToken` (선택)

`equipmentId + logType`은 정확히 하나의 로그 정의를 식별한다. 한 요청에서 한 설비의 여러 `logType`을 동시에 탐색하지 않는다.

`from`/`to`는 파일명/경로 메타데이터에서 추출한 로그의 논리 `timestamp` 기준 반개구간 `[from, to)`다. `from`은 포함하고 `to`는 제외한다.

Timezone 정보가 없는 논리 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다. API의 시간 값은 UTC offset이 포함된 ISO-8601 형식을 사용한다. Daily 로그의 `timestamp`는 해당 날짜의 Site local `00:00`이다.

`subtype` 및 `attr.<name>` 값은 정확한 문자열 일치(case-sensitive)로 비교한다.

응답 item의 핵심 필드:

- fileId
- fileName
- equipmentId
- logType
- subtype
- timestamp
- size
- isContinuous
- attributes

시간 기반 로그 목록의 기본 정렬은 `timestamp DESC`, 동일 timestamp에서는 `fileName ASC`다.

목록은 `limit + opaque continuationToken` 방식으로 페이지네이션한다. offset/page 방식은 사용하지 않는다.

`continuationToken`은 발급된 원래 조회조건의 다음 페이지를 가리킨다. 토큰을 사용하면서 `equipmentId`, `logType`, `from/to`, `subtype`, attributes 등 결과 집합을 바꾸는 조회조건을 변경하면 `InvalidRequest`를 반환한다. 다른 조건으로 조회하려면 continuation token 없이 첫 페이지부터 새로 조회한다.

### Current Configuration 조회

```http
GET /api/v1/configurations/current?equipmentId=...&configurationType=...
```

- `equipmentId`, `configurationType` 모두 필수
- 하나의 Current Configuration 논리 슬롯을 조회
- 시간 필터를 사용하지 않음
- `subtype`/`attributes`는 MVP에서 사용하지 않음
- Current를 History 결과에 포함하지 않음

### Current Configuration 직접 다운로드

```http
GET /api/v1/configurations/current/download?equipmentId=...&configurationType=...
```

- 목록/metadata 조회를 선행하지 않고 현재 설정파일을 직접 다운로드
- 조회와 동일한 Current Resolver 규칙 사용
- 다운로드 시점의 현재 파일 내용을 제공

### Configuration History 목록

```http
GET /api/v1/configurations/history
```

주요 query:

- `equipmentId` (필수)
- `configurationType` (필수)
- `from` (필수)
- `to` (필수)
- `limit` (선택)
- `continuationToken` (선택)

- snapshot 논리 시각 기준 `[from, to)` 조회
- `from`/`to`가 없으면 임의 기본 기간 또는 전체 History로 대체하지 않고 `InvalidRequest`
- 생성 완료된 snapshot은 불변으로 취급
- History 목록은 `limit + opaque continuationToken`으로 페이지네이션
- History의 continuation token도 원래 `equipmentId + configurationType + from/to` 조회조건에 종속되며 조건 변경 시 `InvalidRequest`
- Current Configuration은 결과에 포함하지 않음

### 파일 정보

```http
GET /api/v1/files/{fileId}
HEAD /api/v1/files/{fileId}
```

- GET: 현재 존재 여부를 검증하고 크기/메타데이터 반환
- HEAD: 파일 존재 여부 확인 용도

### fileId 다운로드

```http
GET /api/v1/files/{fileId}/download
```

- `fileId`는 서명된 opaque 토큰
- 일반 조회조건을 저장한 query token이 아님
- 유효기간 24시간
- 토큰에 물리 FTP host/path를 넣지 않음
- 다운로드 시 현재 기준정보를 다시 조회하여 논리 identity의 현재 물리 위치를 해석
- 물리 서버/경로가 변경돼도 같은 논리 파일이 새 위치에 존재하면 기존 `fileId`로 정상 접근

논리 identity는 개념적으로 다음 값을 사용한다.

```text
Log:
  equipmentId + logType + timestamp + fileName

Configuration History:
  equipmentId + configurationType + snapshotTimestamp + fileName

Current Configuration:
  equipmentId + configurationType + current
```

`subtype`/`attributes`는 파일을 다시 식별하기 위한 핵심 identity로 사용하지 않는다.

Current Configuration `fileId`는 특정 물리 버전이 아니라 현재 파일 슬롯을 가리키므로 토큰 발급 후 내용이 바뀌어도 다운로드 시점의 현재 내용을 제공한다.

`fileId` 처리 오류는 다음 원인을 구분한다.

- 형식 오류 또는 서명 검증 실패 → `InvalidFileId` (400)
- TTL 24시간 경과 → `FileIdExpired` (410)
- 로그 기준정보가 삭제되어 재해석 불가 → `LogDefinitionNotFound` (404)
- Configuration 기준정보가 삭제되어 재해석 불가 → `ConfigurationDefinitionNotFound` (404)
- 기준정보는 정상이나 대상 논리 파일/Current 슬롯이 실제로 없음 → `FileNotFound` (404)

### 로그 조건 기반 직접 다운로드

```http
GET /api/v1/logs/download?equipmentId=...&logType=...&...
```

내부적으로 목록과 동일 Resolver를 실행한다.

- 0개 일치: `FileNotFound`
- 1개 일치: 다운로드
- 2개 이상 정상 파일이 사용자 조건에 일치: `MultipleFilesMatched` (409)
- 기준정보의 `cardinality=Single`인데 실제 탐색 결과가 2개 이상: `FileDefinitionConflict` (500)

`MultipleFilesMatched`는 정상 파일 집합에 사용자 조건이 여러 건 일치한 경우에만 사용한다. 정의상 Single인데 여러 파일이 발견된 상태와 혼용하지 않는다.

여러 파일을 자동 ZIP으로 묶는 기능은 MVP에서 제공하지 않는다.

## 대표 오류

- 400 `InvalidRequest`
- 400 `InvalidFileId`
- 401 `InvalidApiKey`
- 404 `EquipmentNotFound`
- 404 `LogDefinitionNotFound`
- 404 `ConfigurationDefinitionNotFound`
- 404 `FileNotFound`
- 409 `MultipleFilesMatched`
- 410 `FileIdExpired`
- 500 `FileDefinitionConflict`
- 502 `FileServerUnavailable`
- 502 `FileServerProtocolError`
- 503 `ReferenceDataUnavailable`
- 500 `InternalError`

세부 error body 규격은 구현 계획에서 일관된 공통 형식으로 확정한다.

## API 안정성

서버 재배치, 물리 경로 변경, 향후 Site 변경이 API 계약 변경으로 이어지지 않아야 한다.
