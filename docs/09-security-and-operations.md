# 보안 및 운영 설계

## 외부 인증

MVP는 HTTPS + API Key를 사용한다.

API Key는 HTTP header 하나로만 전달한다.

```http
X-Api-Key: <key>
```

- query string으로 API Key를 전달하지 않는다.
- header 누락과 잘못된 key는 모두 `401 InvalidApiKey`로 처리한다.
- API Key 원문을 로그에 남기지 않는다.
- 여러 API Key를 동시에 활성화할 수 있으며 각 key는 안정적인 `callerId`와 연결한다.
- MVP에서는 모든 활성 API Key의 권한 범위가 동일하며 API Key별 설비 권한을 세분화하지 않는다.
- 여러 key 동시 활성화로 호출자별 감사 추적과 신/구 key overlap 회전을 지원한다.
- Key 식별자/`callerId`만 감사 로그에 사용한다.
- 키 회전이 가능하도록 Secret/설정을 코드와 분리한다.

## 파일 서버 credential

- 모든 MVP 파일 서버는 동일 credential 사용
- DB/SP에는 비밀번호를 저장/반환하지 않음
- Secret/환경변수 또는 배포 환경의 안전한 설정 공급 방식 사용
- 다른 Site에서 계정이 달라질 경우 credential profile 모델을 후속 검토

## 물리 정보 보호 / 경로 경계

외부에 노출하지 않는다.

- FTP host/IP
- root/path
- credential
- DB 내부 키/테이블 구조

클라이언트가 raw path를 전달하는 endpoint는 만들지 않는다.

`ServerDefinition.rootPath`를 파일 접근의 보안 경계로 사용한다.

- 모든 path template/rule 해석 결과를 정규화한 뒤 root 아래인지 검증한다.
- `..`, 절대 경로, rooted path 등으로 root 밖을 접근하지 못하게 한다.
- Log, Current Configuration, Configuration History, History 완료 marker 모두 같은 경계를 적용한다.
- 경계 위반 기준정보는 실제 파일 서버 접근에 사용하지 않는다.

다운로드 `Content-Disposition`에 사용하는 논리 `fileName`은 HTTP header-safe하게 처리한다. 파일명이 응답 헤더 구조를 깨거나 임의 헤더를 삽입할 수 없어야 하며 물리 경로는 포함하지 않는다.

## 토큰 보안

`fileId`와 `continuationToken`은 모두 클라이언트가 내부 내용을 신뢰하거나 수정할 수 없고 내부 payload도 노출되지 않는 **protected opaque token**으로 취급한다.

공통 token codec은 ASP.NET Core DataProtection 기반으로 무결성 보호, payload 비노출, opaque encoding/decoding, TTL 처리를 담당하고 Log/Configuration의 업무 의미는 각 feature가 소유한다. token 보호 key/secret은 코드와 분리된 안전한 설정 공급 방식을 사용한다.

### fileId

- 논리 파일 identity를 가리키는 token
- 일반 조회조건 자체를 나타내는 query token이 아님
- TTL 24시간
- 물리 host/path는 포함하지 않음
- 보호된 내부 `resourceKind`: `Log | ConfigurationCurrent | ConfigurationSnapshot`
- 접근 시 현재 기준정보로 물리 위치를 다시 해석
- 물리 서버/경로 변경 자체는 기존 fileId를 무효화하지 않음

오류를 다음처럼 구분한다.

- 변조/보호 검증 실패/형식 오류: `InvalidFileId`
- TTL 경과: `FileIdExpired`
- 기준정보 정의 삭제: 해당 `*DefinitionNotFound`
- 기준정보는 정상이나 실제 파일 없음: `FileNotFound`

Configuration Snapshot `fileId`는 실제 파일뿐 아니라 해당 물리 batch(날짜 폴더 복사 단위)의 완료 marker도 재확인한다. marker가 사라졌다면 Snapshot File이 남아 있어도 `FileNotFound`로 처리한다. marker가 완성하는 단위는 물리 batch이지 개별 Snapshot Set이 아니며 한 batch에 여러 Snapshot Set이 포함될 수 있다.

### token protection key rotation / persistence

- DataProtection가 key ring을 관리하며 자동 rotation을 수행한다. 기본 key 수명은 90일로 `fileId` TTL 24시간 이상이다.
- IIS 배포에서는 DataProtection key ring을 파일 시스템에 persist해 프로세스 재시작 후에도 기존 token을 검증할 수 있게 한다.
- 새 token은 현재(active) 보호 key로만 발급한다.
- 보호 key는 IIS 프로세스 재시작만으로 교체되거나 소실되지 않는 방식으로 공급/보관한다.
- 정상적인 애플리케이션/IIS 재시작 후에도 발급된 24시간 `fileId`가 TTL 동안 계속 검증 가능해야 한다.
- key 교체 후에도 이미 발급된 token이 TTL 동안 갑자기 무효화되지 않도록 이전 검증 key를 함께 유지한다.
- 이전 key는 그 key로 발급된 token의 최대 TTL이 모두 경과한 뒤 제거할 수 있다.
- key 식별/선택 및 구체적인 보호 알고리즘은 구현 세부사항이며 외부 API에는 노출하지 않는다.

### continuationToken

- 목록 페이지네이션을 위한 stateless cursor
- 서버에 이전 FTP 결과 전체를 보관하지 않음
- 원래 결과 집합 조건과 마지막 반환 위치를 보호된 형태로 보존
- TTL은 설정 가능하며 구체적인 값은 운영 설정에서 정함
- 만료/변조/보호 검증 실패/형식 오류는 모두 `InvalidRequest`(400)
- fileId와 달리 continuation token 전용 410 오류는 두지 않음

## 감사 로그

감사 파이프라인 순서는 `Audit → ErrorMapping → ApiKey → endpoints`이며 `Audit`이 최외곽이다. ErrorMapping은 `FileGatewayException` 처리 시 `HttpContext.Items["Audit.ErrorCode"]`에 code를 남기고, 최종 HTTP status는 `Response.StatusCode`에서 확정한다. 따라서 Audit은 성공·실패 요청 모두 완결된 status와 errorCode를 기록한다.

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
- refresh는 프로세스당 하나만 실행하는 **single-flight** 방식으로 동기화한다.
- last-known-good cache가 있으면 refresh 중인 다른 요청은 기존 cache로 계속 처리한다.
- 최초 로딩이라 usable cache가 없으면 동시 요청들은 동일한 최초 refresh 결과를 공유한다.
- 새 기준정보는 **전체 검증이 성공한 뒤 cache 전체를 atomic 교체**한다.
- 기준정보 검증은 정의의 구조·문법·invariant만 대상으로 하며 refresh 과정에서 FTP 서버의 디렉터리/파일/marker 존재를 확인하지 않는다.
- 조회 실패 또는 검증 실패 시 일부 새 정의만 적용하지 않는다.
- 갱신 실패/검증 실패 + 마지막 정상 cache가 있으면 last-known-good 전체를 stale 상태로 계속 사용한다.
- 최초 로딩에서 조회 또는 검증에 실패해 정상 cache가 없으면 파일 요청은 `ReferenceDataUnavailable`(503)을 반환한다.
- 하나의 잘못된 정의가 있으면 해당 refresh 전체를 거부한다.
- stale cache 사용 여부, 마지막 정상 갱신 시각, refresh/validation 실패 원인을 로그/메트릭으로 관측 가능하게 한다.
- MVP에서는 별도 background refresh worker를 두지 않는다.

## 원격 조회 운영

- 기준정보 refresh나 readiness 확인을 위해 수십~수백 FTP 서버를 선제 순회하지 않는다.
- 실제 디렉터리/파일/marker 존재 여부는 해당 파일 요청에서 확인한다.
- 계산된 목록 조회 디렉터리가 존재하지 않으면 정상적인 결과 0개로 처리하고 파일 서버 장애율에 포함하지 않는다.
- 한 요청이 여러 디렉터리를 조회하는 중 하나라도 실제 FTP 연결/인증/명령/프로토콜 I/O 오류가 발생하면 부분 결과를 성공 응답으로 반환하지 않고 요청 전체를 해당 파일 서버 오류로 실패 처리한다.
- 파일 서버 연결/인증/프로토콜 오류는 정상적인 경로 없음과 구분한다.
- case-insensitive 기준 동일 파일명이 둘 이상 발견된 경우는 임의 dedupe하지 않고 `FileDefinitionConflict`로 기록한다.
- FTP 작업 동시성은 **FileGateway 전체 한도**와 **파일 서버별 한도**를 각각 운영 설정으로 둔다.
- 구체적인 동시성 숫자는 실제 환경 테스트 후 정하며 무제한 병렬 접근을 허용하지 않는다.

## 다운로드/스트리밍 운영

- metadata의 파일 크기는 조회 시점 관측값이다.
- 공통 `GET /api/v1/files?fileId=...` metadata 조회도 실제 원격 stat을 수행한다.
- 다운로드는 스트림 시작 직전에 확인한 파일 크기를 `Content-Length`로 사용한다.
- Continuous 로그와 Current Configuration처럼 변경 가능한 파일도 시작 시점 크기를 해당 응답의 전송 기준으로 사용한다.
- Continuous 파일이 다운로드 중 커지는 것은 오류가 아니며 시작 시점 크기까지만 전송한다.
- Continuous 로그 다운로드 중 truncate/rotation으로 시작 시점 크기까지 읽지 못하면 streaming I/O 실패로 기록한다.
- 스트리밍 시작 전 FTP 오류는 일반 HTTP 오류 응답으로 반환할 수 있다.
- 스트리밍 시작 후 FTP/I/O 오류는 이미 시작된 응답을 성공 처리하거나 JSON 오류로 바꾸지 않고 스트림/연결을 중단한다.
- 클라이언트 연결 종료/요청 취소는 `ClientCancelled`로 분류하고 파일 서버 장애나 streaming I/O failure와 구분한다.
- 다운로드 기본 Content-Type은 `application/octet-stream`이며 header-safe한 논리 `fileName`을 attachment 파일명으로 사용한다.

FileGateway는 이미 저장소에 보이는 파일을 읽어 제공하는 시스템이다. Current Configuration 및 Hourly/Daily 로그의 생산 방식, 원자적 replace 여부, 쓰기 중 읽기 일관성은 생산 시스템 책임이며 FileGateway는 이를 위해 snapshot 복사, 파일 잠금, 버전 고정 또는 별도 생산 완료 판정을 수행하지 않는다. 외부 변경으로 읽기 길이 불일치나 I/O 실패가 발생하면 일반 streaming failure로 처리한다.

Configuration History는 예외적으로 History 생산자가 생성한 **완료 marker 파일이 존재하는 물리 batch(날짜 폴더 복사 단위)만 탐색 대상**으로 삼는다. marker의 내용은 읽거나 해석하지 않는다. 이는 snapshot 생성 책임을 FileGateway가 가진다는 뜻이 아니라, 불완전한 복사 결과를 읽지 않기 위한 조회 조건이다. 한 물리 batch에는 metadata rule 추출 시각 기준으로 여러 Snapshot Set이 포함될 수 있다.

## 장애/timeout

- FTP 연결/명령/stream timeout은 설정 가능
- 무제한 retry 금지
- 클라이언트 취소를 원격 작업에 전달
- 특정 파일 서버 장애를 전체 FileGateway 장애로 확대하지 않음
- FTP 전체/서버별 동시성 제한을 운영 설정으로 적용

구체적인 timeout/동시성 숫자는 실제 네트워크 테스트 후 운영 설정으로 확정한다.

## Health Check

```text
/health/live
/health/ready
```

- Health endpoint는 인증 없이 접근하며 `/api/*`에만 `X-Api-Key`를 적용한다.
- `live`: 프로세스 생존 여부다. 기준정보 DB 장애만으로 실패시키지 않는다.
- `ready`: 요청을 처리할 최소 **검증 완료 기준정보**를 확보했는지 포함해 핵심 의존성 상태를 판단한다.
- `ready`는 `GetSnapshotAsync(ct)`를 호출해 최초 기준정보 로딩(single-flight)을 실제로 유발하고 DB/SP 조회·검증 결과를 반영한다. usable cache가 없으면 이 호출이 DB/SP 로딩을 유발한다.
- 프로세스 시작 후 검증 완료 기준정보를 한 번도 확보하지 못했고 DB/SP 조회 또는 검증도 실패하면 `live`는 정상, `ready`는 실패다.
- 마지막 정상 기준정보 cache가 존재하는 상태에서 DB 장애 또는 새 기준정보 검증 실패가 발생해도 stale cache로 요청 처리가 가능하므로 `ready`는 200을 유지한다.
- ready 요청마다 수십~수백 FTP 서버 전체를 순회하지 않는다.
- 개별 파일 서버 상태는 실제 요청 시 평가한다.

## 오류 관측성

다음 원인을 구분해 로깅/메트릭이 가능해야 한다.

- 인증 실패
- invalid/expired fileId
- invalid/expired continuationToken
- 기준정보 없음/DB 장애
- 기준정보 validation 실패
- stale 기준정보 사용
- 파일 정의 충돌(`FileDefinitionConflict`)
- 파일 서버 연결/인증/프로토콜 오류
- 경로 없음
- 파일 없음
- multiple match
- timeout
- `ClientCancelled`
- streaming I/O 실패

클라이언트 취소와 정상적인 목록 디렉터리 부재는 서버 장애율/파일 서버 실패율에 포함하지 않는다.
