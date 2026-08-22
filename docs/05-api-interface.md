# API / 클라이언트 인터페이스

## 원칙

- HTTPS
- JSON metadata + streaming download
- API Key 인증
- 실제 서버/FTP 경로 비노출
- 논리 `fileId` 기반 접근
- 목록과 조건 기반 직접 접근은 동일 Resolver 사용

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

`equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자다.

`from`/`to`는 파일명/경로 메타데이터에서 추출한 로그의 논리 `timestamp` 기준이다.

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

- `fileId`는 **특정 논리 파일 하나**를 가리키는 서명된 opaque 토큰
- 일반 조회조건을 저장한 query token이 아님
- 유효기간 24시간
- 토큰에 물리 FTP host/path를 넣지 않음
- 다운로드 시 현재 기준정보를 다시 조회해 같은 논리 파일의 실제 위치를 해석
- 대상 논리 파일이 더 이상 존재하지 않으면 `FileNotFound`

### 조건 기반 직접 다운로드

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
- 404 `FileNotFound`
- 409 `MultipleFilesMatched`
- 502 `FileServerUnavailable`
- 502 `FileServerProtocolError`
- 503 `ReferenceDataUnavailable`
- 500 `InternalError`

세부 error body 규격은 구현 계획에서 일관된 공통 형식으로 확정한다.

## API 안정성

서버 재배치, 물리 경로 변경, 향후 Site 변경이 API 계약 변경으로 이어지지 않아야 한다.

`Configuration File`은 로그가 아니므로 `/api/v1/logs`에 포함할지 여부를 여기서 암묵적으로 가정하지 않는다. 별도 API 경계는 설계 인터뷰에서 확정한다.
