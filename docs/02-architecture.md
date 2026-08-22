# 전체 아키텍처

## 목표

FileGateway는 클라이언트와 분산 파일 서버 사이의 논리적 파일 제공 계층이다. 설비 직접 수집 시스템과는 책임을 분리한다.

```text
[Separate Collector / Processor]
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
      |
FileGateway.Logs
      |
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

### FileGateway.Logs

- 로그 조회 정책
- LogResolver
- 템플릿/정규식 규칙 해석
- 날짜/시간/subtype/attributes 필터
- `fileId`, continuation token 처리
- 목록 조회와 직접 다운로드가 같은 Resolver를 사용하도록 보장

### FileGateway.Core

도메인/프로토콜에 종속되지 않는 공통 계약과 파일 모델을 둔다.

- `IFileAccess`
- 원격 파일 entry/stat/stream 모델
- 공통 I/O 오류 분류

Core는 `Log`, FTP, MSSQL, IIS를 알지 않는다.

### FileGateway.Infrastructure

- MSSQL Stored Procedure 호출
- 기준정보 memory cache
- FTP/FTPS 파일 접근
- credential/secret 로딩
- 외부 I/O 구현

## 프로젝트 구조

```text
FileGateway
├─ FileGateway.Api
├─ FileGateway.Core
├─ FileGateway.Logs
└─ FileGateway.Infrastructure
```

별도 Application 프로젝트는 MVP 규모에서 추가하지 않는다.

## 핵심 의존성 원칙

1. API는 물리 서버/경로를 외부에 노출하지 않는다.
2. Logs는 FTP 구현을 직접 알지 않는다.
3. Core에는 로그별 탐색/시간 규칙을 넣지 않는다.
4. Infrastructure는 Core/Logs의 계약을 구현한다.
5. 새 로그 종류는 가능한 한 DB 기준정보 변경으로 수용하고 코드 분기를 늘리지 않는다.
6. 향후 Linux 배포를 위해 Core/Logs에서 Windows 전용 API에 직접 의존하지 않는다.
