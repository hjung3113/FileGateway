# FileGateway

분산 파일 서버에 이미 저장된 **설비 로그와 Configuration File**을 여러 애플리케이션/시스템에 조회·다운로드 형태로 제공하는 읽기 전용 File Gateway입니다.

API 소비자는 실제 파일 서버 주소나 물리 경로를 알 필요 없이 `equipmentId`와 논리 조회 조건만 사용합니다. FileGateway는 MSSQL 기준정보를 통해 대상 서버와 파일 탐색 규칙을 해석하고 FTP/FTPS로 파일을 읽어 제공합니다.

> 설비 직접 접속, 로그 수집/가공, Configuration History 생성·복사·보관은 별도 시스템 책임이며 FileGateway 범위가 아닙니다.

## 현재 상태

MVP 구현이 완료되어 통합 검증 단계를 거쳤습니다(단위/통합 테스트 전 통과). 배포는 [`docs/10-testing-and-deployment.md`](docs/10-testing-and-deployment.md)의 "배포 전 필수 확인" 목록을 통과해야 MVP 완료로 간주합니다.

## 실행 / 배포

### 실행

```bash
dotnet build
dotnet test
dotnet run --project src/FileGateway.Api
```

### 비밀 주입

비밀은 파일에 두지 않고 환경변수(또는 IIS/Secret 관리 도구)로만 주입합니다:

- `Authentication__ApiKeys__0__Key` / `Authentication__ApiKeys__0__CallerId` — API Key
- `ConnectionStrings__ReferenceData` — MSSQL 기준정보 연결 문자열
- `FileGateway__Ftp__UserName` / `FileGateway__Ftp__Password` — FTP 계정
- `DataProtection__KeyDirectory` — DataProtection 키 저장 디렉터리(미설정 시 개발용 ephemeral 경고 로그)

### IIS 배포

- .NET Hosting Bundle(ASP.NET Core Module V2) 설치 후 In-process(`web.config` 참조)로 호스팅
- `DataProtection:KeyDirectory`는 App Pool 재시작 후에도 유지되는 경로(예: 전용 로컬 디렉터리)로 지정 — 유실 시 모든 `fileId`가 무효화됨
- FTP Passive 모드 사용 시 파일 서버의 Passive 포트 범위가 방화벽에서 열려 있는지 확인
- FTP 보안: `FileGateway:Ftp:Security` = `Plain | ExplicitTls | ImplicitTls`. 내부 self-signed 인증서 허용은 `AcceptUntrustedCertificates: true`로만(운영 판단 필요)
- 배포 전 필수 확인 목록: [`docs/10-testing-and-deployment.md`](docs/10-testing-and-deployment.md) "배포 전 필수 확인"

구현/변경 작업은 [`docs/INDEX.md`](docs/INDEX.md)를 시작점으로 역할별 설계 문서를 확인합니다.

## API 소비자

- 사용자용 WPF 데스크톱 애플리케이션
- Web Backend / BFF
- 파일을 받아가야 하는 다른 서버/서비스

호출 구현은 .NET, Python 등 일반적인 HTTP 클라이언트 환경을 사용할 수 있습니다. 브라우저가 FileGateway API Key를 직접 보유하는 구조는 전제로 하지 않습니다.

## 주요 기능

- 설비별 제공 가능한 `logType` / `configurationType` 조회
- Hourly / Daily / Continuous 로그 목록 조회
- `subtype` / 동적 attributes 기반 로그 필터
- Current Configuration File 집합 조회
- Configuration Snapshot History 조회
- 논리 `fileId` 기반 파일 metadata 조회 및 streaming download
- 조건 기반 직접 다운로드
- `limit + continuationToken` 기반 목록 pagination
- DB 기준정보 기반 파일 종류/탐색 규칙 확장

기존 계약으로 표현 가능한 새 `logType` 또는 `configurationType`은 DB 기준정보 추가만으로 확장하고, 파일 종류별 코드 분기를 늘리지 않는 것을 기본 원칙으로 합니다.

설비사나 설비 종류에 따라 제공 파일이 달라도 `equipmentId`별 기준정보 차이로 표현합니다.

## 주요 API

```http
GET /api/v1/equipments/{equipmentId}/file-types

GET /api/v1/logs
GET /api/v1/logs/download

GET /api/v1/configurations/current
GET /api/v1/configurations/current/download
GET /api/v1/configurations/history

GET /api/v1/files/{fileId}
GET /api/v1/files/{fileId}/download
```

인증은 HTTPS + `X-Api-Key` header를 사용합니다.

`file-types` API는 실제 FTP 파일 존재 여부를 스캔하는 API가 아니라, 검증 완료된 DB 기준정보를 기준으로 해당 설비에 **어떤 종류의 파일을 제공하도록 정의했는지** 반환합니다.

## 구조

```text
WPF Desktop / Web Backend(BFF) / Other Server or Service
                         |
                       HTTPS
                         |
                  FileGateway.Api
                   /            \
         FileGateway.Logs   FileGateway.Configurations
                   \            /
                    FileGateway.Core
                     ^          ^
                     |          |
                MSSQL/cache   FTP/FTPS
                     \          /
               FileGateway.Infrastructure
                         |
               Distributed File Servers
```

- `FileGateway.Api`: HTTP API, 인증, 요청 검증, 감사로그, Health Check
- `FileGateway.Logs`: 로그 탐색/필터/논리 identity/pagination 의미
- `FileGateway.Configurations`: Current/History 탐색 및 Configuration identity
- `FileGateway.Core`: 프로토콜 비종속 파일 I/O 계약과 공통 token codec 계약
- `FileGateway.Infrastructure`: MSSQL, 기준정보 cache, FTP/FTPS, Secret/Key 공급

MVP는 ASP.NET Core/.NET을 Windows Server + IIS에서 운영하며, 실제 파일 접근은 `IFileAccess` 뒤의 FTP/FTPS Adapter가 담당합니다.

## 주요 기술 선택

- FTP/FTPS Adapter: **FluentFTP**를 `FileGateway.Infrastructure` 내부 구현으로 사용
- MSSQL 접근: **Microsoft.Data.SqlClient** 기본 사용
- 통합테스트 인프라: **Testcontainers for .NET** 활용
- Dapper: SP 결과 매핑이 복잡해질 때만 도입 검토
- Polly: 실제 transient retry/circuit-breaker 요구가 생길 때만 도입 검토

외부 라이브러리는 Core/도메인 계약에 노출하지 않고 Infrastructure/Test 경계에 우선 격리합니다. 구체 패키지 버전은 구현 시점에 지원 .NET 버전과 유지보수 상태를 확인해 고정합니다.

## 설계 원칙

- 물리 서버 주소/경로를 외부 API에 노출하지 않음
- API 소비자 입력으로 raw 파일 경로를 구성하지 않음
- 파일 전체 메모리 적재가 아닌 streaming download
- 목록 조회와 직접 다운로드가 동일 Resolver 규칙 사용
- 논리 시간 슬롯과 물리 디렉터리를 1:1로 가정하지 않음
- FileGateway는 저장된 파일을 읽어 제공할 뿐 생산 중 파일의 원자성/잠금/내용 일관성을 보정하지 않음
- 향후 다른 Site/프로토콜/Linux 확장을 허용하되 MVP에 선구현하지 않음

## 문서

**설계·요구사항·구현 작업을 시작할 때는 먼저 [`docs/INDEX.md`](docs/INDEX.md)를 확인합니다.** 작업 종류별로 어떤 역할 문서를 먼저 읽어야 하는지 정리되어 있습니다.

주요 문서:

- [`00-glossary.md`](docs/00-glossary.md) — 정식 용어
- [`01-requirements.md`](docs/01-requirements.md) — MVP 요구사항/범위
- [`02-architecture.md`](docs/02-architecture.md) — 전체 구조와 책임 경계
- [`03-server-access-core.md`](docs/03-server-access-core.md) — 공통 파일 접근 계약
- [`04a-log-provider.md`](docs/04a-log-provider.md) — Log Provider
- [`04b-configuration-provider.md`](docs/04b-configuration-provider.md) — Configuration Provider
- [`05-api-interface.md`](docs/05-api-interface.md) — 외부 API 계약
- [`06-reference-data.md`](docs/06-reference-data.md) — MSSQL 기준정보/cache
- [`07-extension-and-risks.md`](docs/07-extension-and-risks.md) — 확장성과 주요 리스크
- [`08-agent-tooling.md`](docs/08-agent-tooling.md) — Agent/Skill 운영
- [`09-security-and-operations.md`](docs/09-security-and-operations.md) — 인증/보안/운영
- [`10-testing-and-deployment.md`](docs/10-testing-and-deployment.md) — 테스트/배포

## Agent 개발 환경

프로젝트 공통 지침은 [`AGENTS.md`](AGENTS.md)를 기준으로 하며, 설계 문서는 항상 `docs/INDEX.md`에서 시작합니다.

- `CLAUDE.md` → `AGENTS.md` 심볼릭 링크
- Superpowers runtime skills: `.superpowers/skills`
- Matt Pocock design skills subset: `.mattpocock/skills`
- Claude / Codex / OpenCode / OMP에서 프로젝트 로컬 skill 링크 공유

각 skill의 출처와 프로젝트 로컬 조정 내용은 `.superpowers/UPSTREAM.md`, `.mattpocock/UPSTREAM.md`를 참조합니다.
