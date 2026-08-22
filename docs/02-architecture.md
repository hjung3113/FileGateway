# 전체 아키텍처

## 목표

FileGateway는 클라이언트와 분산 파일 서버 사이의 논리적 파일 제공 계층이다. 설비 직접 수집/가공 및 Configuration History 생성 시스템과는 책임을 분리한다.

```text
[Separate Collector / Processor / History Producer]
                    |
                    v
          Distributed File Servers
                    ^
                    | FTP/FTPS
               FileGateway
                    ^
                    | HTTPS
      Clients / WPF / Web Backend / Other Servers
```

## 애플리케이션 계층

```text
                 FileGateway.Api
                  /           \
     FileGateway.Logs   FileGateway.Configurations
                  \           /
              +-----------------------+
              | FileGateway.Core      |
              | contracts/models      |
              +-----------------------+
                  ^             ^
                  |             |
            MSSQL Adapter   FTP/FTPS Adapter
                  \             /
                FileGateway.Infrastructure
```

### FileGateway.Api

- HTTPS endpoint
- API Key 인증
- 요청 검증
- 감사 로그
- Health Check
- JSON/streaming HTTP 응답
- 공통 protected opaque token codec 사용/dispatch

### FileGateway.Logs

- 로그 조회 정책
- LogResolver
- 템플릿/정규식 규칙 해석
- 날짜/시간/subtype/attributes 필터
- Log logical identity 생성/재해석
- Log pagination 조회조건과 cursor 의미 관리
- 목록 조회와 직접 다운로드가 같은 Resolver를 사용하도록 보장

### FileGateway.Configurations

- Configuration File 전용 조회 정책
- Current Configuration 해석
- Configuration Snapshot History 탐색
- `configurationType` 기반 논리 분류
- Current/Snapshot logical identity 생성/재해석
- Configuration History pagination 조회조건과 cursor 의미 관리
- 히스토리 생성/복사/보관은 수행하지 않음

### FileGateway.Core

도메인/프로토콜에 종속되지 않는 공통 계약과 파일 모델을 둔다.

- `IFileAccess`
- 원격 파일 entry/stat/stream 모델
- 공통 I/O 오류 분류
- `fileId`/`continuationToken`에 사용할 공통 token codec 계약(무결성 보호/payload 비노출/opaque encoding/TTL)

Core는 `Log`, `Configuration`, FTP, MSSQL, IIS를 알지 않는다. Token codec도 Log/Configuration의 logical identity나 pagination 조건을 해석하지 않는다.

### FileGateway.Infrastructure

- MSSQL Stored Procedure 호출
- 기준정보 memory cache
- FTP/FTPS 파일 접근
- credential/secret 로딩
- token 보호 key/secret 공급 및 외부 I/O 구현

## 토큰 책임 경계

`fileId`와 `continuationToken`을 위해 범용 File Provider나 범용 Pagination Provider를 추가하지 않는다.

- 공통 계층: token 무결성 보호, payload 비노출, opaque encoding/decoding, TTL 같은 기계적 codec 책임
- Logs: `Log` logical identity와 Log cursor/조회조건 의미
- Configurations: `ConfigurationCurrent`/`ConfigurationSnapshot` logical identity와 History cursor/조회조건 의미

`fileId`에는 외부에서 보이지 않는 보호된 `resourceKind`를 포함해 공통 `/files/{fileId}`가 해당 feature resolver로 위임할 수 있게 한다.

```text
resourceKind
- Log
- ConfigurationCurrent
- ConfigurationSnapshot
```

## 프로젝트 구조

```text
FileGateway
├─ FileGateway.Api
├─ FileGateway.Core
├─ FileGateway.Logs
├─ FileGateway.Configurations
└─ FileGateway.Infrastructure
```

별도 Application 프로젝트는 MVP 규모에서 추가하지 않는다.

## 핵심 의존성 원칙

1. API는 물리 서버/경로를 외부에 노출하지 않는다.
2. Logs와 Configurations는 FTP 구현을 직접 알지 않는다.
3. Logs와 Configurations의 업무 규칙을 Core에 넣지 않는다.
4. Infrastructure는 Core 및 feature 계층이 필요로 하는 외부 I/O 계약을 구현한다.
5. 새 로그 종류는 가능한 한 DB 기준정보 변경으로 수용하고 코드 분기를 늘리지 않는다.
6. Configuration은 로그 모델에 억지로 포함하지 않는다.
7. FileGateway는 Configuration History를 생성하거나 보관하지 않고 이미 저장된 파일만 제공한다.
8. 향후 Linux 배포를 위해 Core/feature 계층에서 Windows 전용 API에 직접 의존하지 않는다.
9. 토큰 보호/직렬화 책임과 도메인 의미를 분리한다.
10. 로그의 논리 생성 슬롯과 물리 디렉터리를 1:1로 가정하지 않는다.
