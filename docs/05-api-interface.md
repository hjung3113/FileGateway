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
- `logType` (선택)
- `from`, `to` (선택, 없으면 최근 24시간)
- `subtype` (선택)
- `attr.<name>=<value>` (선택)
- `limit` (선택)
- `continuationToken` (선택)

`equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자이며 하나의 FileGateway 배포 범위 안에서 유일하다.

`from`/`to`는 파일명/경로 메타데이터에서 추출한 로그의 논리 `timestamp` 기준 반개구간 `[from, to)`다. `from`은 포함하고 `to`는 제외한다.

Timezone 정보가 없는 논리 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다. API의 시간 값은 UTC offset이 포함된 ISO-8601 형식을 사용한다.

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

목록은 `limit + opaque continuationToken` 방식으로 페이지네이션한다. offset/page 방식은 사용하지 않는다.

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
- 로그/Configuration Snapshot `fileId`는 특정 논리 파일 하나를 가리킴
- Current Configuration `fileId`는 특정 `equipmentId + configurationType`의 현재 파일 슬롯을 가리킴
- Current Configuration은 토큰 발급 후 내용이 바뀌어도 다운로드 시점의 현재 내용을 제공
- 대상 논리 파일 또는 Current 슬롯이 더 이상 존재하지 않으면 `FileNotFound`

### 로그 조건 기반 직접 다운로드

```http
GET /api/v1/logs/download?equipmentId=...&logType=...&...
```

내부적으로 목록과 동일 Resolver를 실행한다.

- 0개 일치: `FileNotFound`
- 1개 일치: 다운로드
- 2개 이상 일치: `MultipleFilesMatched` (409)

여러 파일을 자동 ZIP으로 묶는 기능은 MVP에서 제공하지 않는다.

## 대표 오류

- 400 `InvalidRequest`
- 401 `InvalidApiKey`
- 404 `EquipmentNotFound`
- 404 `LogDefinitionNotFound`
- 404 `ConfigurationDefinitionNotFound`
- 404 `FileNotFound`
- 409 `MultipleFilesMatched`
- 502 `FileServerUnavailable`
- 502 `FileServerProtocolError`
- 503 `ReferenceDataUnavailable`
- 500 `InternalError`

세부 error body 규격은 구현 계획에서 일관된 공통 형식으로 확정한다.

## API 안정성

서버 재배치, 물리 경로 변경, 향후 Site 변경이 API 계약 변경으로 이어지지 않아야 한다.
