# FileGateway Design Spec

Date: 2026-08-22
Status: Approved

> 이 문서는 확정 설계의 통합 스냅샷이다. 구현/변경 시에는 `docs/INDEX.md`가 안내하는 역할별 문서를 최신 기준으로 사용한다.

## 1. 목적과 책임 경계

FileGateway는 **분산 파일 서버에 이미 저장된 설비 로그와 Configuration File을 조회·다운로드로 제공하는 읽기 전용 Gateway**다.

FileGateway가 하지 않는 일:

- 설비 직접 접속
- 로그 수집/가공
- Configuration Current 생성/변경
- Configuration History 생성/복사/보관
- 생산 중 파일의 원자성/잠금/내용 일관성 보정

이 기능들은 별도 생산 시스템의 책임이다.

## 2. MVP 기술/운영

- ASP.NET Core/.NET
- Windows Server + IIS
- HTTPS
- `X-Api-Key` 인증
- MSSQL Stored Procedure 기준정보
- 프로세스 memory cache
- FTP/FTPS Adapter 1개
- 모든 MVP 파일 서버 동일 credential
- JSON metadata + streaming download
- 주요 파일 크기 대부분 100MB 이하
- 파일 서버 수십~수백, 동시 다운로드 수십 건 수준 고려

## 3. 프로젝트 구조

```text
FileGateway.Api
  ├─ FileGateway.Logs
  └─ FileGateway.Configurations
          ↓
     FileGateway.Core
          ↑
FileGateway.Infrastructure
  ├─ MSSQL
  └─ FTP/FTPS
```

- `Api`: HTTP/auth/validation/audit/health/streaming 응답
- `Logs`: 로그 조회/탐색/필터/Log identity/pagination 의미
- `Configurations`: Current/History 조회/Configuration identity/History pagination 의미
- `Core`: `IFileAccess`, 원격 file/stat/stream, 공통 I/O 오류, 공통 token codec 계약
- `Infrastructure`: MSSQL/cache/FTP/secret/token 보호 key 공급

별도 Application 프로젝트나 범용 File Provider/Pagination Provider는 MVP에 추가하지 않는다.

## 4. 공통 식별/시간 규칙

### Equipment

`equipmentId`는 하나의 FileGateway 배포 범위에서 유일한 안정적 논리 설비 식별자다.

### Time

- timezone 없는 논리 시각: `Asia/Seoul`
- API 시각: UTC offset 포함 ISO-8601
- 범위: 반개구간 `[from, to)`
- FTP modified time을 Log timestamp나 Configuration snapshot timestamp로 사용하지 않음

### 파일명 비교

MVP Windows/IIS FTP에서는 파일명 관련 비교를 case-insensitive로 한다.

적용 대상:

- glob matching
- logical identity의 `fileName`
- 정렬
- pagination cursor

원래 casing은 응답에 보존한다. `PM1.cfg`와 `pm1.cfg`처럼 case-insensitive 기준 같은 서로 다른 원격 파일이 동시에 발견되면 `FileDefinitionConflict`다.

`subtype`/`attributes`는 case-sensitive다.

## 5. Log 모델

### 정의

```text
EquipmentLogDefinition
- equipmentId
- logType
- serverId
- generationType: Hourly | Daily | Continuous
- discoveryRule
- metadataRule
```

MVP에서 `(equipmentId, logType)`은 정확히 하나의 정의와 하나의 `discoveryRule`을 가진다.

`logType`은 업무 종류이고 `generationType`은 파일 생성/생명주기 축이다.

### DiscoveryRule

```text
- pathTemplate
- filePattern
- cardinality: Single | Multiple
```

- `pathTemplate`: 조회조건/논리 슬롯으로 탐색할 디렉터리 계산
- `filePattern`: 디렉터리 내 후보 파일명 glob (`*`, `?`)
- FTP 서버 wildcard 의미에 의존하지 않음
- root부터 무제한 recursive scan 금지

**논리 시간 슬롯과 물리 디렉터리는 1:1이 아니다.**

예를 들어 한 폴더에 하루치 시간별 파일이 모두 모일 수 있다. 여러 슬롯이 같은 디렉터리를 계산하면 한 번만 목록 조회하고 MetadataRule로 각 파일의 논리 시간을 해석한다. 날짜/시간별 디렉터리 구조도 동일 모델로 지원한다.

계산된 디렉터리가 없으면 정상 결과 0개다. 여러 디렉터리 조회 중 실제 FTP I/O 오류가 하나라도 발생하면 부분 결과를 반환하지 않고 요청 전체를 실패 처리한다.

### MetadataRule

- `Template | Regex`
- 입력: FTP root 제외 후 `/`로 정규화된 `relative path + fileName`
- `timestamp`, `subtype`, `attribute.<key>` 추출
- 일반 규칙은 Template 우선, 복잡한 예외만 Regex named group
- `filePattern`에 선택된 후보가 필수 metadata로 파싱되지 않으면 `FileDefinitionConflict`

### Cardinality

전체 조회 개수가 아니라 **논리 생성 슬롯당** invariant다.

- Hourly: 시간 슬롯
- Daily: 날짜 슬롯
- Continuous: 현재 슬롯
- `Single`: 슬롯당 최대 1개
- `Multiple`: 슬롯당 여러 파일 허용

`Single` 슬롯에서 2개 이상 발견되면 `FileDefinitionConflict`다.

### Generation Type

Hourly/Daily:

- `from`,`to` 없음 → 최근 24시간
- `from`만 → `[from, from + 2일)`
- `to`만 → `InvalidRequest`
- 둘 다 → 지정 `[from,to)`
- `from >= to` → `InvalidRequest`
- `Logs.MaxQueryRange` 적용, 최소 설정값 2일 이상
- Daily timestamp는 Site local `00:00`

Continuous:

- 현재 파일만 조회
- `from` 또는 `to`가 있으면 `InvalidRequest`
- 최근 24시간 기본값 적용 안 함
- 명확한 논리 시간이 없으면 `timestamp=null`
- 정렬 `fileName ASC`
- cursor `fileName`
- 다운로드 시작 시점 크기까지만 전송

Hourly/Daily 정렬은 `timestamp DESC`, 동일 시각 `fileName ASC`; cursor는 `timestamp + fileName`이다.

## 6. Configuration 모델

Configuration은 Log가 아니며 `logType/generationType/subtype/attributes` 모델에 넣지 않는다.

### Configuration Type / Current

`equipmentId + configurationType`은 **Current Configuration File 집합**을 가리킨다.

예: `configurationType=PM`에 PM1/PM2/PM3/PM4 파일이 함께 존재할 수 있다. 개별 파일을 별도 subtype/configurationType으로 세분화하지 않는다.

Current File logical identity:

```text
equipmentId + configurationType + fileName
```

- Current 조회는 단순 배열
- pagination 없음
- 정렬 `fileName ASC`
- 0개 → `200 []`
- `fileId`는 특정 바이트 버전을 고정하지 않고 다운로드 시점의 현재 내용 가리킴

Current 직접 다운로드:

```http
GET /api/v1/configurations/current/download?equipmentId=...&configurationType=...
```

- 0개 → `FileNotFound`
- 1개 → 다운로드
- 2개 이상 → `MultipleFilesMatched`

여러 파일이면 목록에서 `fileId`를 얻어 공통 다운로드 API를 사용한다.

### Configuration History

별도 생산 시스템이 자정에 Current 파일 집합을 날짜 폴더로 복사하며 Current 원본은 유지한다.

한 날짜의 파일 집합은 `Configuration Snapshot Set`이고 같은 Set의 파일들은 동일 `snapshotTimestamp`를 공유한다. 현재 운영 계획에서는 Site local 자정이다.

History 생산자는 복사 완료 후 marker 파일을 생성한다.

- marker 이름/위치는 `historyRule`
- FileGateway는 marker **존재 여부만** 확인
- marker 내용은 읽거나 해석하지 않음
- marker 없는 부분 Snapshot Set은 노출하지 않음

Snapshot File identity:

```text
equipmentId + configurationType + snapshotTimestamp + fileName
```

History:

- `from`,`to` 모두 필수
- `[from,to)`
- `from >= to` → `InvalidRequest`
- `Configurations.HistoryMaxQueryRange` 적용
- 개별 Snapshot File 목록 반환
- 정렬 `snapshotTimestamp DESC`, 동일 시각 `fileName ASC`
- pagination 사용
- 별도 History direct-download endpoint 없음

Snapshot `fileId` 재접근 시 완료 marker를 다시 확인한다. marker가 사라졌으면 실제 snapshot 파일이 남아 있어도 `FileNotFound`다.

## 7. API 계약

### 인증

```http
X-Api-Key: <key>
```

- query string key 금지
- 누락/오류 모두 `401 InvalidApiKey`
- 여러 API Key 동시 활성화 가능
- 각 key는 `callerId`와 연결
- MVP 권한 범위는 모든 활성 key가 동일
- key rotation 시 신/구 key overlap 가능

### 주요 endpoints

```http
GET /api/v1/logs
GET /api/v1/logs/download?... 

GET /api/v1/configurations/current
GET /api/v1/configurations/current/download
GET /api/v1/configurations/history

GET /api/v1/files/{fileId}
GET /api/v1/files/{fileId}/download
```

`HEAD /api/v1/files/{fileId}`는 MVP에 두지 않는다.

### 목록 pagination

Log와 Configuration History:

```json
{
  "items": [],
  "continuationToken": null
}
```

- offset/page 사용 안 함
- `limit` 기본/최대값 운영 설정
- `limit`은 페이지마다 변경 가능
- continuation token은 원래 결과 집합 조건에 바인딩
- 서버에 이전 결과 전체를 저장하지 않는 stateless cursor
- 원격 파일 집합의 완전한 snapshot은 보장하지 않음

### 공통 metadata

```http
GET /api/v1/files/{fileId}
```

최소 공통 필드만 반환:

```text
fileId
fileName
size
```

Log/Configuration 업무 metadata는 각 feature API가 소유한다.

### 다운로드

```http
GET /api/v1/files/{fileId}/download
```

- 실제 stream 시작 직전 stat한 크기를 `Content-Length`로 사용
- `Content-Type: application/octet-stream`
- header-safe한 logical `fileName`으로 `Content-Disposition: attachment`
- 물리 host/path 비노출
- streaming 시작 전 FTP 오류 → 정상 JSON 오류 응답
- streaming 시작 후 I/O 오류 → 연결/stream 중단, 성공으로 처리하지 않음
- client cancel → `ClientCancelled` 운영 분류

## 8. File ID / Token

`fileId`는 24시간 TTL의 **protected opaque token**이다.

내부 `resourceKind`:

```text
Log
ConfigurationCurrent
ConfigurationSnapshot
```

identity:

```text
Log:
  equipmentId + logType + timestamp + fileName

ConfigurationCurrent:
  equipmentId + configurationType + fileName

ConfigurationSnapshot:
  equipmentId + configurationType + snapshotTimestamp + fileName
```

- 물리 host/path 저장 안 함
- 현재 기준정보로 다시 물리 위치 해석
- 서버/경로 이동 후 같은 logical file이 존재하면 기존 `fileId` 사용 가능
- payload 내용은 클라이언트에 노출하지 않음
- 공통 token 계층은 보호/encoding/TTL만 담당
- 도메인 identity 의미는 Logs/Configurations가 담당

보호 key는 IIS/프로세스 재시작으로 소실되지 않는 방식으로 공급/보관한다. 회전 시 이전 key를 최대 기존 token TTL 동안 검증 가능하게 유지한다.

Continuation token도 protected opaque token이며 만료/형식/보호 검증 실패는 `400 InvalidRequest`로 통합한다.

## 9. 오류 계약

ASP.NET Core Problem Details 계열 JSON + 안정적인 machine-readable `code` + `traceId`를 사용한다.

```json
{
  "type": "about:blank",
  "title": "File not found",
  "status": 404,
  "code": "FileNotFound",
  "traceId": "..."
}
```

대표 오류:

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
- 500 `InternalError`
- 502 `FileServerUnavailable`
- 502 `FileServerProtocolError`
- 503 `ReferenceDataUnavailable`

FTP host/path, credential, DB/SP 내부 진단, stack trace는 외부 오류 body에 노출하지 않는다.

## 10. 기준정보/cache

Stored Procedure가 제공하는 정보:

- equipment/server mapping
- host/rootPath
- Log definitions
- Configuration definitions
- discovery/metadata/current/history/marker rules

credential은 DB에서 받지 않는다.

### Cache

- memory cache
- TTL은 강제 만료가 아니라 refresh 시도 시점
- request-driven lazy refresh
- background refresh 없음
- refresh는 프로세스당 single-flight
- last-good가 있으면 refresh 중 다른 요청은 기존 cache 사용
- 새 기준정보 **전체 구조/문법/invariant 검증 성공 후 atomic 교체**
- 하나라도 validation 실패하면 새 snapshot 전체 거부
- last-good가 있으면 stale로 계속 사용
- 최초부터 usable cache가 없으면 `ReferenceDataUnavailable`

기준정보 validation에서 FTP 디렉터리/파일/marker 실재 여부는 확인하지 않는다. 실제 저장소 상태는 요청 시 확인한다.

MVP에는 max-stale 차단 시간이 없으므로 stale cache 장기 사용은 운영 리스크로 관측한다.

## 11. 보안/경로

- `rootPath`가 서버별 접근 보안 경계
- 정규화 후 root 밖 경로 금지
- `..`, 절대/rooted path 탈출 금지
- 클라이언트 raw path 입력 금지
- API Key/FTP credential/token payload/물리 경로 비로깅
- Content-Disposition filename header-safe 처리

## 12. 원격 I/O / 운영

- 계산된 디렉터리 없음 → 정상 결과 0개
- 실제 FTP 연결/인증/프로토콜 실패와 구분
- 한 요청의 일부 디렉터리만 FTP 실패해도 부분 성공 반환 안 함
- FileGateway 전체 FTP 동시 작업 한도 설정
- 파일 서버별 FTP 동시 작업 한도 설정
- timeout/cancel 설정
- 무제한 retry 금지
- ready check에서 전체 FTP 서버 순회 금지

Health:

```text
/health/live
/health/ready
```

- usable 기준정보가 전혀 없으면 ready 실패
- last-good stale cache가 있어 처리 가능하면 DB 장애만으로 ready를 즉시 실패시키지 않음

## 13. MVP 제외

- 설비 직접 수집/가공
- Configuration History 생성/복사/보관
- 생산 파일 원자성/잠금/내용 일관성 보정
- Linux 실제 배포
- SMB/SFTP Adapter
- Site별 credential profile
- Range/Resume
- 여러 파일 자동 ZIP
- API Key별 세밀 권한
- Web UI/WPF 클라이언트 구현
- 분산 cache/HA
- 범용 다중 discovery rule

## 14. 구현 전 문서 우선순위

1. `docs/INDEX.md`
2. 해당 역할별 문서
3. `docs/00-glossary.md`의 canonical language
4. 이 통합 Spec은 승인 시점 전체 맥락 확인용
