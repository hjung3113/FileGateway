# API / 클라이언트 인터페이스

## 원칙

- HTTPS
- JSON metadata + streaming download
- API Key 인증
- 실제 서버/FTP 경로 비노출
- 논리 `fileId` 기반 접근
- 로그와 Configuration은 별도 feature API로 구분
- 각 feature의 목록과 조건 기반 직접 접근은 동일 Resolver 규칙을 사용
- 클라이언트가 raw 물리 경로를 전달하지 않음

## API v1

### 로그 목록

```http
GET /api/v1/logs
```

주요 query:

- `equipmentId` (필수)
- `logType` (필수)
- `from`, `to` (선택)
- `subtype` (선택)
- `attr.<name>=<value>` (선택)
- `limit` (선택)
- `continuationToken` (선택)

`equipmentId + logType`은 정확히 하나의 로그 정의를 식별한다. 한 요청에서 한 설비의 여러 `logType`을 동시에 탐색하지 않는다.

`from`/`to`는 파일명/경로 메타데이터에서 추출한 로그의 논리 `timestamp` 기준 반개구간 `[from, to)`다. `from`은 포함하고 `to`는 제외한다.

시간 범위 입력 규칙:

- `from`, `to` 모두 없음 → 최근 24시간
- `from`만 있음 → `[from, from + 2일)`
- `to`만 있음 → `InvalidRequest`
- `from`, `to` 모두 있음 → 지정한 `[from, to)`
- `from >= to` → `InvalidRequest`

시간 기반 로그 조회에는 설정 가능한 `Logs.MaxQueryRange`를 적용하며 초과 요청은 `InvalidRequest`다. `from` 단독 요청이 2일 범위를 의미하므로 `Logs.MaxQueryRange`는 최소 2일 이상이어야 한다.

Timezone 정보가 없는 논리 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다. API의 시간 값은 UTC offset이 포함된 ISO-8601 형식을 사용한다. Daily 로그의 `timestamp`는 해당 날짜의 Site local `00:00`이다. Continuous 로그에 명확한 논리 시각이 없으면 `timestamp`는 `null`이며 현재 시각이나 FTP modified time으로 대체하지 않는다.

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

`size`는 목록/metadata 조회 시점의 관측값이다. Continuous 로그나 Current Configuration처럼 변경 가능한 파일은 이후 다운로드 시점 크기와 다를 수 있다.

로그 목록 정렬은 `generationType`별로 고정한다.

- Hourly/Daily: `timestamp DESC`, 동일 timestamp에서는 `fileName ASC`
- Continuous: `fileName ASC`

`equipmentId + logType`은 하나의 `generationType`만 가지므로 시간 기반 로그와 `timestamp=null`인 Continuous 로그를 같은 목록 정의 안에서 혼합하지 않는다.

페이지네이션 목록 응답은 다음 envelope를 사용한다.

```json
{
  "items": [],
  "continuationToken": null
}
```

- `items`: 현재 페이지의 파일 목록
- `continuationToken`: 다음 페이지가 있으면 opaque token, 마지막 페이지면 `null`
- 결과가 없으면 `200 OK`, `items=[]`, `continuationToken=null`

목록은 `limit + opaque continuationToken` 방식으로 페이지네이션한다. offset/page 방식은 사용하지 않는다. `limit`의 기본값과 최댓값은 운영 설정으로 두며 최대값 초과 시 `InvalidRequest`다.

`continuationToken`은 발급된 원래 조회조건의 다음 페이지를 가리킨다. 토큰을 사용하면서 `equipmentId`, `logType`, `from/to`, `subtype`, attributes 등 **결과 집합을 바꾸는 조회조건**을 변경하면 `InvalidRequest`를 반환한다. 다른 조건으로 조회하려면 continuation token 없이 첫 페이지부터 새로 조회한다.

`limit`은 결과 집합 조건이 아니라 해당 응답의 페이지 크기이므로 continuation token을 유지한 채 페이지마다 변경할 수 있다.

페이지네이션은 원격 파일 집합의 완전한 snapshot을 보장하지 않는다. 안정적인 정렬과 cursor를 사용하지만 페이지 사이에 파일이 추가/삭제되면 결과가 변할 수 있다.

Log continuation token은 서버에 이전 FTP 결과 전체를 저장하지 않는 stateless cursor다. 토큰은 원래 조회조건과 generationType에 맞는 마지막 반환 위치를 보존한다.

- Hourly/Daily cursor: `timestamp + fileName`
- Continuous cursor: `fileName`

탐색 규칙의 `filePattern`에 후보로 일치한 파일을 필수 metadata 규칙으로 해석하지 못하면 조용히 제외하지 않고 `FileDefinitionConflict`(500)로 처리한다.

조건 기반 존재 여부 전용 HEAD endpoint는 추가하지 않는다. 조건 기반 존재 확인은 목록 조회를 사용하고, 특정 `fileId`의 현재 상태/존재 확인은 공통 `GET /api/v1/files/{fileId}`를 사용한다.

### Current Configuration 조회

```http
GET /api/v1/configurations/current?equipmentId=...&configurationType=...
```

- `equipmentId`, `configurationType` 모두 필수
- 동일 `equipmentId + configurationType` 아래 PM1/PM2/PM3/PM4처럼 여러 Current Configuration File이 존재할 수 있음
- 개별 파일을 `subtype`/`attributes`로 세분화하지 않음
- 시간 필터를 사용하지 않음
- Current를 History 결과에 포함하지 않음
- 결과는 개별 Current Configuration File들의 **단순 배열**
- 결과가 없으면 `200 OK`와 빈 배열
- Current는 `limit`/`continuationToken` 없이 현재 파일 전체를 한 번에 반환
- 기본 정렬은 `fileName ASC`

Current item의 핵심 필드:

- `fileId`
- `fileName`
- `equipmentId`
- `configurationType`
- `size`

Current Configuration File의 logical identity는 `equipmentId + configurationType + fileName`이다. `fileName`이 바뀌면 다른 논리 파일로 취급한다.

### Current Configuration 직접 다운로드

```http
GET /api/v1/configurations/current/download?equipmentId=...&configurationType=...
```

Current 조회와 동일한 Resolver 규칙을 사용한다.

- 0개 일치 → `FileNotFound`
- 1개 일치 → 해당 Current Configuration File 다운로드
- 2개 이상 일치 → `MultipleFilesMatched` (409)

여러 Current 파일이 있는 경우 목록에서 원하는 파일의 `fileId`를 얻은 뒤 공통 `/api/v1/files/{fileId}/download`를 사용한다. 직접 다운로드 endpoint에 `fileName`이나 Configuration 전용 subtype 조건을 추가하지 않는다.

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
- `from >= to`면 `InvalidRequest`
- `from`/`to`가 없으면 임의 기본 기간 또는 전체 History로 대체하지 않고 `InvalidRequest`
- `Configurations.HistoryMaxQueryRange`를 초과하면 `InvalidRequest`
- 별도 시스템이 자정에 Current 파일 집합을 날짜 폴더로 복사하며 Current 원본은 그대로 유지
- 같은 날짜/시점에 복사된 Snapshot File들은 동일한 `snapshotTimestamp`를 공유
- 현재 운영 계획에서 `snapshotTimestamp`는 해당 날짜의 Site local `00:00`
- History 생산자가 완료 조건/marker를 제공하며, 완료가 확인된 Snapshot Set만 조회 결과에 포함
- 복사 중인 부분 Snapshot Set은 노출하지 않음
- 생성 완료된 Snapshot File은 불변으로 취급
- History는 Snapshot Set을 중첩 객체로 반환하지 않고 개별 Snapshot File 목록으로 반환
- History item의 핵심 필드: `fileId`, `fileName`, `equipmentId`, `configurationType`, `snapshotTimestamp`, `size`
- 기본 정렬은 `snapshotTimestamp DESC`, 동일 시각에서는 `fileName ASC`
- History 목록은 Log와 동일한 `{ items, continuationToken }` pagination envelope 사용
- 결과가 없으면 `200 OK`, `items=[]`, `continuationToken=null`
- History 목록은 `limit + opaque continuationToken`으로 페이지네이션
- `limit`의 기본값/최댓값은 운영 설정으로 두며 최대값 초과 시 `InvalidRequest`
- History의 continuation token도 원래 `equipmentId + configurationType + from/to` 조회조건에 종속되며 조건 변경 시 `InvalidRequest`
- `limit`은 페이지마다 변경 가능
- 페이지 사이에 원격 History 파일이 추가/삭제되면 결과 변화가 가능하며 완전한 snapshot은 보장하지 않음
- Current Configuration은 결과에 포함하지 않음

Configuration History continuation token도 stateless cursor이며 원래 조회조건과 마지막 반환 위치인 `snapshotTimestamp + fileName`을 보존한다.

Configuration History 전용 조건 기반 직접 다운로드 endpoint는 MVP에서 만들지 않는다. 원하는 Snapshot File의 `fileId`를 얻은 뒤 공통 `/api/v1/files/{fileId}/download`를 사용한다.

### continuationToken 공통 계약

- token 서명/검증/opaque encoding/TTL은 공통 token codec을 사용한다.
- Log와 Configuration History는 각각 자신의 조회조건과 cursor 의미를 소유한다.
- 서버에 이전 페이지 결과 전체를 저장하지 않는다.
- TTL은 설정 가능하며 구체적인 값은 운영 설정에서 정한다.
- 만료, 형식 오류, 서명 검증 실패/변조, 해당 endpoint에서 해석할 수 없는 token은 모두 `400 InvalidRequest`로 처리한다.
- `fileId`의 `FileIdExpired`처럼 continuation token 전용 410 오류를 만들지 않는다.

### 파일 정보

```http
GET /api/v1/files/{fileId}
```

MVP에서는 `/api/v1/files/{fileId}`에 HEAD endpoint를 두지 않는다. GET metadata가 이미 실제 원격 stat/존재 확인과 파일 크기 조회를 수행하므로 별도 HEAD 계약을 만들지 않는다.

GET은 다음 순서로 실제 대상 상태를 검증한다.

1. `fileId` 검증
2. 토큰 내부 `resourceKind`에 따라 Logs 또는 Configurations의 identity resolver로 위임
3. 현재 기준정보로 논리 identity 재해석
4. 실제 원격 파일 stat/존재 여부 확인

공통 `/files/{fileId}` endpoint는 feature 업무 metadata를 재구성하는 API가 아니라 공통 파일 상태 확인 용도다.

GET JSON의 핵심 필드는 다음 최소 공통 정보만 둔다.

- `fileId`
- `fileName`
- `size`

`logType`, `timestamp`, `subtype`, `attributes`, `configurationType`, `snapshotTimestamp` 같은 업무 metadata는 각각 Logs/Configurations API가 소유한다.

`size`는 해당 GET 조회 시점의 실제 원격 파일 크기 관측값이며 이후 변경 가능한 파일의 크기를 고정하지 않는다.

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

`fileId` 내부에는 클라이언트에 노출되지 않는 서명된 `resourceKind`가 포함된다.

```text
Log
ConfigurationCurrent
ConfigurationSnapshot
```

클라이언트는 `resourceKind`를 파싱하거나 외부 요청 파라미터로 전달하지 않는다.

논리 identity는 다음 값을 사용한다.

```text
Log:
  equipmentId + logType + timestamp + fileName

Configuration Snapshot File:
  equipmentId + configurationType + snapshotTimestamp + fileName

Current Configuration File:
  equipmentId + configurationType + fileName
```

공통 token 계층은 identity의 업무 의미를 해석하지 않는다. Logs와 Configurations가 각 identity 생성/재해석을 소유한다.

`subtype`/`attributes`는 파일을 다시 식별하기 위한 핵심 identity로 사용하지 않는다.

Current Configuration File의 `fileId`는 특정 바이트 버전을 고정하지 않는다. 같은 논리 identity의 파일 내용이 변경돼도 다운로드 시점의 현재 내용을 제공한다. 파일명이 바뀌면 다른 논리 파일이므로 기존 `fileId`로 새 이름의 파일을 가리키지 않는다.

다운로드 응답은 스트림 시작 직전에 실제 파일 크기를 확인하고 그 값을 `Content-Length`로 사용한다.

- 일반 로그 / Configuration Snapshot File: 해당 파일의 시작 직전 크기
- Continuous 로그: 다운로드 시작 시점 크기를 전송 상한으로 고정
- Current Configuration File: 다운로드 시작 시점의 현재 파일 크기

Continuous 로그는 다운로드 중 파일이 커져도 시작 크기 이후의 추가 내용은 보내지 않는다. 반대로 truncate/rotation으로 `Content-Length`만큼 읽지 못하면 정상 완료가 아니라 streaming I/O 실패다. 새 파일로 이어 붙이거나 자동 재시도하지 않는다.

FileGateway는 **저장소에서 읽을 수 있는 파일을 제공하는 역할만** 가진다. Current Configuration 및 Hourly/Daily 로그가 생산 중 어떤 방식으로 갱신되는지, 파일 교체가 원자적인지, 읽는 동안 내용 일관성이 보장되는지는 생산 시스템의 책임이다. FileGateway는 이를 위해 별도 snapshot 복사, 잠금, 버전 고정 또는 생산 완료 판정을 추가하지 않는다. 외부 변경으로 스트림 길이 불일치/I/O 실패가 발생하면 일반 streaming failure 규칙을 적용한다.

다운로드 응답의 기본 헤더는 다음 원칙을 따른다.

- `Content-Type: application/octet-stream`
- `Content-Disposition: attachment`에 header-safe하게 처리한 논리 `fileName` 사용
- 물리 서버명/경로는 헤더에 노출하지 않음

스트리밍 시작 전 원격 stream open/FTP 처리에 실패하면 아직 일반 HTTP 오류 응답이 가능하므로 기존 오류 매핑에 따라 `FileServerUnavailable` 또는 `FileServerProtocolError` 등 JSON 오류를 반환한다.

스트리밍 시작 후 원격 I/O 오류가 발생하면 이미 시작된 응답을 JSON 오류로 변경하지 않는다. 응답 스트림을 중단하고 연결을 종료하며 서버에서는 streaming I/O failure로 기록한다.

클라이언트가 연결을 끊거나 요청을 취소한 경우에는 `ClientCancelled`로 기록하고 파일 서버/streaming 장애와 구분한다.

`fileId` 처리 오류는 다음 원인을 구분한다.

- 형식 오류 또는 서명 검증 실패 → `InvalidFileId` (400)
- TTL 24시간 경과 → `FileIdExpired` (410)
- 로그 기준정보가 삭제되어 재해석 불가 → `LogDefinitionNotFound` (404)
- Configuration 기준정보가 삭제되어 재해석 불가 → `ConfigurationDefinitionNotFound` (404)
- 기준정보는 정상이나 대상 논리 파일이 실제로 없음 → `FileNotFound` (404)

### 로그 조건 기반 직접 다운로드

```http
GET /api/v1/logs/download?equipmentId=...&logType=...&...
```

내부적으로 목록과 동일 Resolver를 실행한다.

- 0개 일치: `FileNotFound`
- 1개 일치: 다운로드
- 2개 이상 정상 파일이 사용자 조건에 일치: `MultipleFilesMatched` (409)
- 기준정보의 `cardinality=Single`인데 하나의 논리 생성 슬롯에서 실제 탐색 결과가 2개 이상: `FileDefinitionConflict` (500)
- 후보 파일이 필수 metadata 규칙으로 해석되지 않음: `FileDefinitionConflict` (500)

`MultipleFilesMatched`는 정상 파일 집합에 사용자 조건이 여러 건 일치한 경우에만 사용한다. 정의상 Single인데 여러 파일이 발견되거나 정의된 후보를 해석하지 못한 상태와 혼용하지 않는다.

여러 파일을 자동 ZIP으로 묶는 기능은 MVP에서 제공하지 않는다.

## 경로/헤더 안전성

- 모든 실제 파일 접근 경로는 기준정보의 해당 서버 `rootPath` 아래로 정규화되어야 한다.
- `..`, 절대 경로, rooted path 등을 통해 root 밖으로 나가는 기준정보는 실제 접근에 사용하지 않는다.
- 클라이언트 입력으로 raw 물리 경로를 구성하지 않는다.
- `Content-Disposition`의 파일명은 HTTP header-safe하게 처리한다.
- FTP host/path 및 내부 기준정보는 성공/오류 응답 모두에서 노출하지 않는다.

## 공통 오류 응답

HTTP 오류 body는 ASP.NET Core Problem Details 계열의 공통 JSON 형식을 사용하고, 클라이언트가 안정적으로 분기할 수 있는 `code`와 운영 추적용 `traceId`를 포함한다.

예시:

```json
{
  "type": "about:blank",
  "title": "File not found",
  "status": 404,
  "code": "FileNotFound",
  "traceId": "..."
}
```

- `code`는 아래 정의된 안정적인 machine-readable 오류 코드다.
- `title`/`detail`은 클라이언트 안전한 일반 설명만 제공한다.
- FTP host/path, credential, 실제 기준정보 값, Stored Procedure/DB 진단, stack trace 등 내부 정보는 응답에 포함하지 않는다.
- 상세 원인은 `traceId`로 연계되는 서버 로그에서 확인한다.

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

`ClientCancelled`는 서버가 새 HTTP 오류 응답을 반환하는 코드가 아니라, 클라이언트가 이미 연결을 종료/취소한 요청의 운영상 종료 분류다.

## API 안정성

서버 재배치, 물리 경로 변경, 향후 Site 변경이 API 계약 변경으로 이어지지 않아야 한다.
