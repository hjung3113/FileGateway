# FileGateway

분산 파일 서버에 이미 저장된 **설비 로그와 Configuration File**을 클라이언트에 조회·다운로드 형태로 제공하는 읽기 전용 File Gateway입니다.

클라이언트는 실제 파일 서버 주소나 물리 경로를 알 필요 없이 `equipmentId`와 논리 조회 조건만 사용합니다. FileGateway는 MSSQL 기준정보를 통해 대상 서버와 파일 탐색 규칙을 해석하고 FTP/FTPS로 파일을 읽어 제공합니다.

> 설비 직접 접속, 로그 수집/가공, Configuration History 생성·복사·보관은 별도 시스템 책임이며 FileGateway 범위가 아닙니다.

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
Clients (.NET / Python / WPF / Web Backend / Other Server)
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

## 설계 원칙

- 물리 서버 주소/경로를 외부 API에 노출하지 않음
- 클라이언트 입력으로 raw 파일 경로를 구성하지 않음
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
- [`09-security-and-operations.md`](docs/09-security-and-operations.md) — 인증/보안/운영
- [`10-testing-and-deployment.md`](docs/10-testing-and-deployment.md) — 테스트/배포

## Agent 개발 환경

프로젝트 공통 지침은 [`AGENTS.md`](AGENTS.md)를 기준으로 하며, 설계 문서는 항상 `docs/INDEX.md`에서 시작합니다.

- `CLAUDE.md` → `AGENTS.md` 심볼릭 링크
- Superpowers runtime skills: `.superpowers/skills`
- Matt Pocock design skills subset: `.mattpocock/skills`
- Claude / Codex / OpenCode / OMP에서 프로젝트 로컬 skill 링크 공유

각 skill의 출처와 프로젝트 로컬 조정 내용은 `.superpowers/UPSTREAM.md`, `.mattpocock/UPSTREAM.md`를 참조합니다.
