# 보안 및 운영 설계

## 외부 인증

MVP는 HTTPS + API Key를 사용한다.

- API Key 원문을 로그에 남기지 않는다.
- Key 식별자/CallerId만 감사 로그에 사용한다.
- MVP에서는 API Key별 설비 권한을 세분화하지 않는다.
- 키 회전이 가능하도록 Secret/설정을 코드와 분리한다.

## 파일 서버 credential

- 모든 MVP 파일 서버는 동일 credential 사용
- DB/SP에는 비밀번호를 저장/반환하지 않음
- Secret/환경변수 또는 배포 환경의 안전한 설정 공급 방식 사용
- 다른 Site에서 계정이 달라질 경우 credential profile 모델을 후속 검토

## 물리 정보 보호

외부에 노출하지 않는다.

- FTP host/IP
- root/path
- credential
- DB 내부 키/테이블 구조

클라이언트가 raw path를 전달하는 endpoint는 만들지 않는다.

## 토큰 보안

`fileId`와 `continuationToken`은 모두 클라이언트가 내부 내용을 신뢰하거나 수정할 수 없는 서명된 opaque token으로 취급한다.

공통 token codec은 서명/검증, opaque encoding/decoding, TTL 처리를 담당하고 Log/Configuration의 업무 의미는 각 feature가 소유한다. 서명 key/secret은 코드와 분리된 안전한 설정 공급 방식을 사용한다.

### fileId

- 논리 파일 identity를 가리키는 token
- 일반 조회조건 자체를 나타내는 query token이 아님
- TTL 24시간
- 물리 host/path는 포함하지 않음
- 서명된 내부 `resourceKind`: `Log | ConfigurationCurrent | ConfigurationSnapshot`
- 접근 시 현재 기준정보로 물리 위치를 다시 해석
- 물리 서버/경로 변경 자체는 기존 fileId를 무효화하지 않음

오류를 다음처럼 구분한다.

- 변조/서명 실패/형식 오류: `InvalidFileId`
- TTL 경과: `FileIdExpired`
- 기준정보 정의 삭제: 해당 `*DefinitionNotFound`
- 기준정보는 정상이나 실제 파일 없음: `FileNotFound`

### continuationToken

- 목록 페이지네이션을 위한 stateless cursor
- 서버에 이전 FTP 결과 전체를 보관하지 않음
- 원래 결과 집합 조건과 마지막 반환 위치를 서명된 형태로 보존
- TTL은 설정 가능하며 구체적인 값은 운영 설정에서 정함
- 만료/변조/서명 실패/형식 오류는 모두 `InvalidRequest`(400)
- fileId와 달리 continuation token 전용 410 오류는 두지 않음

## 감사 로그

최소 필드:

- timestamp
- callerId/API Key 식별자
- client IP
- endpoint
- equipmentId
- logType(로그 요청인 경우)
- configurationType(Configuration 요청인 경우)
- fileId(가능한 경우)
- fileName/fileSize(다운로드 시)
- 성공/실패와 오류 분류
- elapsedMs

API Key/FTP credential/물리 경로/요청 본문 전체와 token의 내부 payload는 기록하지 않는다.

## 기준정보 장애 운영

- 기준정보 캐시 TTL은 강제 폐기 시간이 아니라 갱신 재시도 기준이다.
- TTL 경과 후 요청 시 lazy refresh를 수행한다.
- 갱신 실패 시 마지막 정상 캐시가 있으면 stale 상태로 계속 사용한다.
- 프로세스 시작 후 정상 기준정보를 한 번도 읽지 못해 캐시가 없으면 파일 요청은 `ReferenceDataUnavailable`(503)을 반환한다.
- stale 캐시 사용 여부와 마지막 정상 갱신 시각을 로그/메트릭으로 관측 가능하게 한다.
- MVP에서는 별도 background refresh worker를 두지 않는다.

## 다운로드/스트리밍 운영

- metadata/HEAD의 파일 크기는 조회 시점 관측값이다.
- 다운로드는 스트림 시작 직전에 확인한 파일 크기를 `Content-Length`로 사용한다.
- Continuous 로그와 Current Configuration처럼 변경 가능한 파일도 시작 시점 크기를 해당 응답의 전송 기준으로 사용한다.
- Continuous 파일이 다운로드 중 커지는 것은 오류가 아니며 시작 시점 크기까지만 전송한다.
- Continuous 로그 다운로드 중 truncate/rotation으로 시작 시점 크기까지 읽지 못하면 streaming I/O 실패로 기록한다.
- 스트리밍 시작 전 FTP 오류는 일반 HTTP 오류 응답으로 반환할 수 있다.
- 스트리밍 시작 후 FTP/I/O 오류는 이미 시작된 응답을 성공 처리하거나 JSON 오류로 바꾸지 않고 스트림/연결을 중단한다.
- 클라이언트 연결 종료/요청 취소는 `ClientCancelled`로 분류하고 파일 서버 장애나 streaming I/O failure와 구분한다.
- 다운로드 기본 Content-Type은 `application/octet-stream`이며 논리 `fileName`을 attachment 파일명으로 사용한다.

## 장애/timeout

- FTP 연결/명령/stream timeout은 설정 가능
- 무제한 retry 금지
- 클라이언트 취소를 원격 작업에 전달
- 특정 파일 서버 장애를 전체 FileGateway 장애로 확대하지 않음
- 동시 다운로드 수는 설정으로 제한 가능하게 설계

구체적인 timeout/동시성 숫자는 실제 네트워크 테스트 후 운영 설정으로 확정한다.

## Health Check

```text
/health/live
/health/ready
```

- `live`: 프로세스 생존 여부다. 기준정보 DB 장애만으로 실패시키지 않는다.
- `ready`: 요청을 처리할 최소 기준정보를 확보했는지 포함해 핵심 의존성 상태를 판단한다.
- 프로세스 시작 후 기준정보를 한 번도 확보하지 못했고 DB도 사용할 수 없으면 `live`는 정상, `ready`는 실패다.
- 마지막 정상 기준정보 캐시가 존재하는 상태에서 DB가 일시 장애인 경우 stale 캐시로 요청 처리가 가능하므로 `ready`를 즉시 실패시키지 않는다.
- ready 요청마다 수십~수백 FTP 서버 전체를 순회하지 않는다.
- 개별 파일 서버 상태는 실제 요청 시 평가한다.

## 오류 관측성

다음 원인을 구분해 로깅/메트릭이 가능해야 한다.

- 인증 실패
- invalid/expired fileId
- invalid/expired continuationToken
- 기준정보 없음/DB 장애
- stale 기준정보 사용
- 파일 정의 충돌(`FileDefinitionConflict`)
- 파일 서버 연결/인증/프로토콜 오류
- 경로 없음
- 파일 없음
- multiple match
- timeout
- `ClientCancelled`
- streaming I/O 실패

클라이언트 취소는 서버 장애율/파일 서버 실패율에 포함하지 않는다.
