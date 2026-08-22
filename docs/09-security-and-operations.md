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

## fileId

- 서명된 opaque token
- TTL 24시간
- 논리 식별정보만 포함
- 물리 host/path는 포함하지 않음
- 다운로드 시 기준정보를 다시 조회

## 감사 로그

최소 필드:

- timestamp
- callerId/API Key 식별자
- client IP
- endpoint
- equipment
- logType
- fileId(가능한 경우)
- fileName/fileSize(다운로드 시)
- 성공/실패와 오류 분류
- elapsedMs

API Key/FTP credential/물리 경로/요청 본문 전체는 기록하지 않는다.

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

- `live`: 프로세스 생존 여부
- `ready`: 기본 설정과 핵심 의존성 상태
- ready 요청마다 수십~수백 FTP 서버 전체를 순회하지 않는다.
- 개별 파일 서버 상태는 실제 요청 시 평가한다.

## 오류 관측성

다음 원인을 구분해 로깅/메트릭이 가능해야 한다.

- 인증 실패
- 기준정보 없음/DB 장애
- 파일 서버 연결/인증/프로토콜 오류
- 경로 없음
- 파일 없음
- multiple match
- timeout/cancel
- streaming I/O 실패
