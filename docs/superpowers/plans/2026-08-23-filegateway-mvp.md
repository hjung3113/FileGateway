# FileGateway MVP 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 분산 파일 서버(FTP/FTPS)의 설비 로그와 Configuration File을 논리 식별자 기반으로 조회/스트리밍 다운로드하는 읽기 전용 FileGateway MVP를 구축한다.

**Architecture:** 5개 프로젝트(Api / Logs / Configurations / Core / Infrastructure). Core는 프로토콜 비종속 파일 I/O 계약·token codec 계약·공통 오류만 갖고, Logs/Configurations가 각 도메인의 탐색/식별/pagination 의미를 소유하며, Infrastructure가 FluentFTP Adapter와 MSSQL 기준정보 cache를 구현한다. Api는 X-Api-Key 인증 + Problem Details 오류 계약 + streaming 다운로드만 담당한다.

**Tech Stack:** .NET 10 (LTS), ASP.NET Core Minimal API, FluentFTP, Microsoft.Data.SqlClient, xUnit, Testcontainers.MsSql, FubarDev.FtpServer(테스트 전용 in-proc FTP)

**Spec:** `docs/superpowers/specs/2026-08-22-filegateway-design.md` (통합 스냅샷) + `docs/INDEX.md`가 안내하는 역할별 문서. 세부 계약은 아래 각 Task와 `docs/00-glossary.md` 용어를 따른다.

---

## 확정 결정 사항

설계 문서가 "구현 계획에서 확정"로 남긴 항목을 이 계획에서 다음과 같이 잠근다. Task 구현은 이 결정을 변경하지 않는다.

1. **.NET 대상 프레임워크**: `net10.0` (LTS). Task 1에서 SDK 버전을 확인하고 `global.json`으로 고정한다.
2. **추가 프레임워크 금지**: MediatR, AutoMapper, Polly, Dapper, 별도 validation/logging abstraction 도입 금지. FluentFTP / Microsoft.Data.SqlClient / Testcontainers / FubarDev.FtpServer 외 신규 패키지 금지.
3. **`pathTemplate` 문법**: `/` 구분 상대 경로 + 리터럴 + 토큰 `{yyyy}` `{MM}` `{dd}` `{HH}`. 토큰은 논리 슬롯의 Site local(Asia/Seoul) 구성요소로 치환된다. 토큰 없는 고정 경로도 허용(Continuous/flat 디렉터리). `..`, rooted 경로, `:` 포함 금지.
4. **`filePattern` glob 문법**: 파일명 전용. `*` = `/` 없는 임의 run, `?` = 임의 1문자. 문자 클래스 없음. 패턴에 `/` 금지. 대소문자 무시(case-insensitive) 매칭.
5. **`MetadataRule` 문법**:
   - 입력: FTP root 제외, `/` 정규화 relative path + fileName (예: `Logs/2026/08/22/18/Event_A.zip`).
   - `Template` mode: 전체 relative path에 대한 패턴. 리터럴 + 토큰 `{yyyy}` `{MM}` `{dd}` `{HH}` `{mm}` `{subtype}` `{attribute.<key>}`. 날짜 토큰은 고정폭 숫자, `{subtype}`/`{attribute.k}`는 `/` 아닌 run. 토큰명이 곧 mapping이다(별도 mappings 불필요).
   - `Regex` mode: 전체 relative path에 anchored한 regex named group. `mappings`가 group→target을 지정: target ∈ `timestamp`(`format` 필수, .NET DateTime 형식 문자열), `subtype`, `attribute.<key>` — 역할별 문서(04a)의 표기 그대로. 주의: API query parameter 접두어 `attr.<name>`(05 문서 고정)와 mapping target `attribute.<key>`(04a 문서 고정)는 **서로 다른 이름공간**이며 서로 치환하지 않는다.
   - 토큰/그룹 요건: Hourly는 timestamp 완전성(년월일시) 필요, Daily는 날짜 토큰만(시/분 토큰·format 금지, timestamp는 Site local `00:00`), Continuous는 timestamp 선택(없으면 `null`).
   - 해석 실패 후보는 제외하지 않고 `FileDefinitionConflict`.
6. **token codec**: ASP.NET Core DataProtection 기반. payload JSON을 purpose protector로 보호 후 Base64Url. payload에 `exp`(IssuedAt+Ttl) 포함, decode 시 만료/변조/형식 오류를 구분(`Expired` vs `Invalid`). purpose는 각 token 종류마다 독립 문자열. key ring은 DataProtection가 관리(자동 rotation, 기본 수명 90일 ≥ fileId TTL 24시간) — IIS 배포 시 파일 시스템 persist로 재시작 내구성 확보.
7. **token purpose 문자열**: Logs fileId `fg.fileid.log`, Logs cursor `fg.page.log`, ConfigurationCurrent fileId `fg.fileid.cfgcurrent`, ConfigurationSnapshot fileId `fg.fileid.cfgsnapshot`, History cursor `fg.page.cfghistory`.
8. **SP 계약**: 단일 SP `FileGateway_GetReferenceData`가 순서대로 4개 result set 반환: Equipments(ServerId 아님, EquipmentId), Servers(ServerId, Host, RootPath), LogDefinitions(EquipmentId, LogType, ServerId, GenerationType, PathTemplate, FilePattern, Cardinality, MetadataMode, MetadataPattern, MetadataMappings), ConfigurationDefinitions(EquipmentId, ConfigurationType, ServerId, CurrentPathTemplate, CurrentFilePattern, HistoryPathTemplate, HistoryFilePattern, HistoryMarkerPathTemplate). `MetadataMappings`은 JSON 배열 `[{"group":"...","target":"...","format":"..."}]` (format 선택). SP/스키마 스크립트는 `db/`에 테스트·개발용 계약 구현으로 제공하고 운영 DB 내부 구조는 이 계약만 지키면 자유다.
9. **시각 표현**: 내부 비교는 UTC instant, 경계/슬롯 계산은 Asia/Seoul. API 입출력 시각은 offset 포함 ISO-8601(`DateTimeOffset` round-trip "O"). 입력에 offset 없으면 Asia/Seoul로 해석.
10. **기준정보 접근 인터페이스**: Infrastructure의 `ReferenceDataCache`가 `IReferenceDataView.GetSnapshotAsync(ct)` 하나로 노출하고, Api/Logs/Configurations는 `ReferenceDataSnapshot`의 조회 메서드만 사용한다(정의 provider 인터페이스 다발을 만들지 않는다).
11. **Health endpoint는 인증 없음**, `/api/*`만 X-Api-Key 대상.
12. **파일명 비교 구현**: `StringComparer.OrdinalIgnoreCase`로 단일화(정렬·identity·cursor·glob 공용).
13. **FTP/FTPS 전송 보안**: `FtpOptions.Security` = `Plain | ExplicitTls | ImplicitTls`(기본 `Plain`), `FtpOptions.AcceptUntrustedCertificates`(기본 `false`)를 두고 `FtpConfig`에 반영한다(EncryptionMode/인증서 검증). FTP/FTPS Adapter 계약(03 문서)을 코드로 만족하며, 실제 FTPS 연동·인증서 검증은 Task 21 수동 게이트에서 확인한다.
14. **`/health/ready`의 최초 로딩 유도**: ready는 `GetSnapshotAsync`를 호출해 최초 기준정보 로딩(single-flight)을 실제로 유발하고 DB/SP 조회·검증 결과를 반영한다(09 문서). usable cache가 있으면 stale여도 200. FTP 서버 순회는 하지 않는다. — lazy-load만으로는 readiness만 받는 신규 프로세스가 영구 503이 되는 것을 방지한다.
15. **감사 파이프라인 순서**: `Audit → ErrorMapping → ApiKey → endpoints`. ErrorMapping이 `FileGatewayException` 처리 시 `HttpContext.Items["Audit.ErrorCode"]`에 code를 남기고 최종 HTTP status는 `Response.StatusCode`에서 확정되므로, Audit(최외곽)은 성공·실패 요청 모두 완결된 status/errorCode를 기록한다.
16. **설계문서 동기화 선행**: 이 계획이 새로 확정한 계약(3~15)은 Task 0에서 역할별 설계문서에 반영한다. `docs/INDEX.md`의 "역할별 문서가 현재 구현 기준" 규칙을 유지하기 위해 구현 Task는 Task 0 완료 후 시작한다.
17. **테스트 인프라 버전 고정**: Testcontainers MSSQL 이미지는 `latest` 금지, 실행 시점의 구체 CU 태그로 고정한다(예: `mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04`). Testcontainers/FubarDev/FluentFTP 패키지 버전도 csproj에 고정된 값으로 기록한다.
18. **MVP 완료 게이트 이중화**: Task 1~20의 자동화 게이트(`dotnet build && dotnet test`)는 구현 완료 조건일 뿐 MVP 완료가 아니다. MVP 완료는 Task 21의 수동 배포 검증 체크리스트(10 문서 "배포 전 필수 확인" + "MVP 완료 기준")까지 통과해야 한다.

## Global Constraints

모든 Task의 요구사항에 암묵적으로 포함된다. 출처는 통합 Spec/역할별 문서.

- 파일명 비교는 case-insensitive, 원본 casing은 응답에 보존. `subtype`/`attributes` 비교는 case-sensitive.
- 시간 범위는 반개구간 `[from, to)`. `from >= to` → `InvalidRequest`. `to` 단독 → `InvalidRequest`. `from` 단독 → `[from, from+2일)`. 둘 다 없음 → 최근 24시간. `Logs.MaxQueryRange` ≥ 2일(시작 시 검증), 초과 → `InvalidRequest`.
- Continuous는 `from`/`to` 입력 시 `InvalidRequest`, 최근 24시간 기본값 미적용, `timestamp` 없으면 `null`.
- Configuration History는 `from`/`to` 모두 필수, `Configurations.HistoryMaxQueryRange` 초과 → `InvalidRequest`, Current를 포함하지 않는다.
- 정렬: Hourly/Daily `timestamp DESC` + case-insensitive `fileName ASC`. Continuous `fileName ASC`. Current `fileName ASC`. History `snapshotTimestamp DESC` + `fileName ASC`.
- pagination은 `limit + opaque stateless continuationToken`. `limit`은 결과집합 조건이 아니라 페이지 크기(페이지마다 변경 허용). 토큰 유지 중 결과집합 조건 변경 → `InvalidRequest`. continuation token 오류(만료/변조/형식)는 전부 `400 InvalidRequest`(410 없음).
- fileId: TTL 24시간 protected opaque token, 물리 host/path 미포함, `resourceKind`(purpose) 내포. 오류 구분: 형식/서명 → `InvalidFileId`(400), 만료 → `FileIdExpired`(410), 정의 삭제 → `LogDefinitionNotFound`/`ConfigurationDefinitionNotFound`(404), 파일 부재 → `FileNotFound`(404).
- 오류 코드 표(변경 금지): 400 `InvalidRequest`, 400 `InvalidFileId`, 401 `InvalidApiKey`, 404 `EquipmentNotFound`, 404 `LogDefinitionNotFound`, 404 `ConfigurationDefinitionNotFound`, 404 `FileNotFound`, 409 `MultipleFilesMatched`, 410 `FileIdExpired`, 500 `FileDefinitionConflict`, 500 `InternalError`, 502 `FileServerUnavailable`, 502 `FileServerProtocolError`, 503 `ReferenceDataUnavailable`. body는 Problem Details 계열 + `code` + `traceId`.
- root 아래 경계: 모든 계산 경로는 `rootPath` 아래, `..`/절대/rooted 탈출 금지, 클라이언트 입력으로 물리 경로 조합 금지, 물리 host/path/credential/token payload/기준정보 내부 값은 응답·로그에 비노출.
- 무제한 recursive scan 금지. 계산된 디렉터리 부재는 정상 0개, 파일 서버 연결/인증/프로토콜 오류와 구분. 한 요청에서 일부 디렉터리만 FTP I/O 실패하면 부분 결과 반환 금지(전체 실패).
- 감사 로그는 성공/실패 요청 모두 최종 HTTP status와 안정적 오류 분류(errorCode)를 포함한다(순서/경로는 확정 결정 15).
- `/health/ready`는 최초 기준정보 로딩을 유발하고 DB/SP 결과를 반영하며(확정 결정 14), 어떤 health endpoint도 FTP 서버를 순회하지 않는다.
- cardinality는 슬롯당 invariant. `Single` 슬롯 2개 이상, metadata 해석 실패, case-insensitive 동일 파일명 복수 → `FileDefinitionConflict`.
- 다운로드: 시작 직전 크기 = `Content-Length` = 전송 상한. 시작 후 I/O 오류는 응답 중단(JSON 전환 금지). 클라이언트 취소는 `ClientCancelled`로 운영 분류. `Content-Type: application/octet-stream`, header-safe `Content-Disposition: attachment`.
- 기준정보: 전체 검증 성공 후 atomic 교체, 실패 시 last-known-good stale 사용, 최초 usable 없음 → `ReferenceDataUnavailable`. refresh는 single-flight, FTP 실재 확인 금지, background worker 없음.
- History marker는 존재 여부만 확인(내용 미해석), marker 없는 Snapshot Set 미노출, Snapshot fileId 재접근 시 marker 재확인(없으면 `FileNotFound`).
- API Key는 `X-Api-Key` header만, query string 금지, 누락/오류 모두 `401 InvalidApiKey`, 여러 key(callerId 매핑) 동시 활성, 원문 비로깅.
- HEAD `/files/{fileId}` 없음, Range/Resume 없음, 자동 ZIP 없음, History 전용 직접다운로드 없음.
- 모든 비밀(API Key, FTP credential, DB 연결, token key)은 Secret/환경변수 공급. `appsettings.json`에 비밀 금지.
- AGENTS.md 원칙(YAGNI, 변경 범위 최소화) 준수. 요청되지 않은 기능·추상화 추가 금지.

## File Structure

```text
FileGateway.sln
global.json
db/
  mvp-schema.sql                      # 테스트/개발용 계약 테이블
  mvp-stored-procedure.sql            # FileGateway_GetReferenceData 계약 구현
src/
  FileGateway.Core/
    Errors/FileGatewayErrors.cs       # 오류 코드 registry + FileGatewayException
    Files/FileServerConnection.cs     # ServerId/Host/RootPath
    Files/RemoteFileEntry.cs          # Name/Size
    Files/RemoteDirectoryListing.cs   # Exists/Files
    Files/RemoteOpenRead.cs           # Stream/Length
    Files/FileAccessException.cs      # FileAccessError 분류
    Files/IFileAccess.cs
    Files/GlobPattern.cs
    Files/FileNameComparison.cs
    Files/LocatedFile.cs
    Streams/ExactLengthStream.cs
    Time/SiteTime.cs                  # Asia/Seoul 기준 시각 유틸
    Time/EffectiveRange.cs            # [from,to) 범위 값(양 feature 공용)
    Tokens/ITokenCodec.cs             # TokenPayload/TokenDecodeResult 포함
  FileGateway.Logs/
    Definitions/Models.cs             # GenerationType/Cardinality/규칙/EquipmentLogDefinition
    Definitions/LogDefinitionValidator.cs
    Tokens/LogTokenKinds.cs
    Internal/PathTemplate.cs
    Internal/SlotExpansion.cs
    Internal/MetadataRuleParser.cs
    Internal/LogCursor.cs
    LogListQuery.cs                   # query/유효 range 정규화
    LogFileDescriptor.cs
    ILogQueryService.cs               # List/ResolveSingle/LocateByFileId + PagedResult/SingleFileMatch
    LogQueryService.cs                # Resolver+cursor+fileId 조립 (DI 진입점)
  FileGateway.Configurations/
    Definitions/Models.cs             # CurrentRule/HistoryRule/EquipmentConfigurationDefinition
    Definitions/ConfigurationDefinitionValidator.cs
    Tokens/ConfigurationTokenKinds.cs
    Internal/CurrentResolver.cs
    Internal/HistoryResolver.cs
    Internal/HistoryCursor.cs
    ConfigurationItems.cs
    IConfigurationQueryService.cs
    ConfigurationQueryService.cs
  FileGateway.Infrastructure/
    Ftp/FtpOptions.cs
    Ftp/FtpConcurrencyLimiter.cs
    Ftp/FtpFileAccess.cs              # FluentFTP Adapter
    Tokens/DataProtectionTokenCodec.cs
    ReferenceData/ReferenceDataRaw.cs # SP 결과 DTO
    ReferenceData/IReferenceDataSource.cs
    ReferenceData/SpReferenceDataSource.cs
    ReferenceData/ReferenceDataSnapshot.cs   # 검증 완료 스냅샷 + 조회 메서드
    ReferenceData/ReferenceDataSnapshotBuilder.cs
    ReferenceData/IReferenceDataView.cs
    ReferenceData/ReferenceDataCache.cs       # single-flight/atomic/stale
  FileGateway.Api/
    Program.cs
    Options/FileGatewayOptions.cs
    Auth/ApiKeyMiddleware.cs
    Errors/ErrorMappingMiddleware.cs
    Audit/AuditMiddleware.cs
    Endpoints/CatalogEndpoints.cs
    Endpoints/LogEndpoints.cs
    Endpoints/ConfigurationEndpoints.cs
    Endpoints/FileEndpoints.cs
    Endpoints/HealthEndpoints.cs
    Downloading/DownloadResult.cs
    appsettings.json                  # 비민감 설정만
tests/
  FileGateway.UnitTests/
    TestUtils/FakeFileAccess.cs       # Task 3에서 생성, 이후 공용
    Core/*, Logs/*, Configurations/*, ReferenceData/*
  FileGateway.IntegrationTests/
    DatabaseFixture.cs                # Task 7에서 생성, Task 19 재사용
    Ftp/FtpFileAccessTests.cs, FtpAdapterFixture.cs
    ReferenceData/SpReaderTests.cs
    Api/EndToEndTests.cs
```

코드 블록은 핵심 구현 전체를 담는다. `using` 지시문과 `namespace` 선언은 각 파일 관례에 따라 추가하고, record는 필요시 `sealed`.

---

### Task 0: 설계문서 동기화 (모든 구현 Task의 선행 조건)

**Files:**
- Modify: `docs/00-glossary.md`, `docs/02-architecture.md`, `docs/03-server-access-core.md`, `docs/04a-log-provider.md`, `docs/04b-configuration-provider.md`, `docs/06-reference-data.md`, `docs/09-security-and-operations.md`, `docs/10-testing-and-deployment.md`

**Interfaces:**
- Consumes: 이 계획의 "확정 결정 사항" 1~18
- Produces: 역할별 설계문서가 계획에서 확정한 계약과 동일해진다. `docs/INDEX.md`의 "역할별 문서가 현재 구현 기준" 규칙이 유지되어, 구현 agent가 문서와 계획 사이에서 갈리지 않는다. 신규 문서를 만들지 않고 기존 문서의 해당 절만 수정한다(INDEX 등록 불필요).

- [ ] **Step 1: 문서별 반영**

| 문서 | 반영 내용 |
|---|---|
| `00-glossary.md` | Token Codec 항목에 DataProtection 기반 구현·purpose 문자열·key ring 관리 방식 명시. File Name Comparison에 `StringComparer.OrdinalIgnoreCase` 구현 고정 |
| `02-architecture.md` | .NET 10(net10.0) 고정, 패키지 집합 확정(FluentFTP/Microsoft.Data.SqlClient/Testcontainers.MsSql/FubarDev.FtpServer — 테스트 전용) 및 버전 고정 원칙, 프로젝트 구조에 `db/` 계약 스크립트 언급 |
| `03-server-access-core.md` | `IFileAccess` 구체 시그니처 확정(계획 Task 3), `FtpOptions.Security`(Plain/ExplicitTls/ImplicitTls)·인증서 정책·동시성 lease 계약(스트림이 permit 소유), 연결 후 명령 오류도 동일 매핑 |
| `04a-log-provider.md` | pathTemplate 토큰 문법(`{yyyy}{MM}{dd}{HH}`), Template 메타데이터 토큰(`{subtype}`,`{attribute.<key>}`,날짜 토큰), Regex mapping target 문법(group→`timestamp(format)`/`subtype`/`attribute.<key>`), Daily/Hourly/Continuous별 필수 토큰 규칙, 중복 판정의 "동일 탐색 결과(디렉터리) 범위" 명시 |
| `04b-configuration-provider.md` | History 하한 경계([from,to) 정확 적용 — from이 자정이 아니면 그날 자정 Set 제외), currentRule/historyRule pathTemplate 토큰 문법 |
| `06-reference-data.md` | SP 4-result-set 계약(컬럼 목록·순서·MetadataMappings JSON 형식), `db/` 스크립트의 테스트/개발용 계약 구현 지위 |
| `09-security-and-operations.md` | `/health/ready` 최초 로딩 유도 계약(usable cache 없으면 ready가 DB/SP 로딩을 유발, stale면 200), 감사 파이프라인 순서와 `Audit.ErrorCode` 경로, token 보호 key의 DataProtection 파일 persist·rotation 정책 |
| `10-testing-and-deployment.md` | 테스트 이미지/패키지 버전 고정(latest 금지), MVP 완료 = 자동화 게이트 + 수동 배포 검증(Task 21 절차) 이중 게이트 명시 |

각 문서는 해당 절의 기존 서술 방식을 유지하고, "구현 계획에서 확정" 상태였던 부분을 확정 계약으로 교체한다. 문서 간 표기 충돌(query `attr.<name>` vs mapping `attribute.<key>` 이름공간 구분 등)도 함께 정리한다.

- [ ] **Step 2: 검증 및 커밋**

Run: `docs/INDEX.md`의 각 행이 안내하는 문서를 spot-check — 계획의 확정 결정 1~18과 역할별 문서가 모순되는 문장이 없는지 확인.

```bash
git add docs && git commit -m "docs: align role documents with locked implementation contracts"
```

---

### Task 1: 솔루션 scaffold + Core 경로 정규화/경계

**Files:**
- Create: `FileGateway.sln`, `global.json`, `src/FileGateway.Core/FileGateway.Core.csproj`, `tests/FileGateway.UnitTests/FileGateway.UnitTests.csproj` 외 4개 src 프로젝트(csproj)
- Create: `src/FileGateway.Core/Paths/RemotePath.cs`
- Test: `tests/FileGateway.UnitTests/Core/RemotePathTests.cs`

**Interfaces:**
- Consumes: 없음(첫 Task)
- Produces: `static string RemotePath.Normalize(string path)`, `static string Combine(string root, string relative)`, `static bool IsRooted(string path)`, `static bool IsSafeDefinitionPath(string path)`, `static bool IsUnderRoot(string root, string path)` — 전 경로 비교는 case-insensitive

- [ ] **Step 1: 솔루션/프로젝트 생성**

```bash
dotnet --version   # 10.x 확인
dotnet new sln -n FileGateway
dotnet new classlib -n FileGateway.Core -o src/FileGateway.Core -f net10.0
dotnet new classlib -n FileGateway.Logs -o src/FileGateway.Logs -f net10.0
dotnet new classlib -n FileGateway.Configurations -o src/FileGateway.Configurations -f net10.0
dotnet new classlib -n FileGateway.Infrastructure -o src/FileGateway.Infrastructure -f net10.0
dotnet new web -n FileGateway.Api -o src/FileGateway.Api -f net10.0
dotnet new xunit -n FileGateway.UnitTests -o tests/FileGateway.UnitTests -f net10.0
dotnet new xunit -n FileGateway.IntegrationTests -o tests/FileGateway.IntegrationTests -f net10.0
dotnet sln add src/**/*.csproj tests/**/*.csproj
dotnet add src/FileGateway.Logs reference src/FileGateway.Core
dotnet add src/FileGateway.Configurations reference src/FileGateway.Core
dotnet add src/FileGateway.Infrastructure reference src/FileGateway.Core src/FileGateway.Logs src/FileGateway.Configurations
dotnet add src/FileGateway.Api reference src/FileGateway.Logs src/FileGateway.Configurations src/FileGateway.Infrastructure
dotnet add tests/FileGateway.UnitTests reference src/FileGateway.Api src/FileGateway.Logs src/FileGateway.Configurations src/FileGateway.Infrastructure
dotnet add tests/FileGateway.IntegrationTests reference src/FileGateway.Api src/FileGateway.Logs src/FileGateway.Configurations src/FileGateway.Infrastructure
dotnet new globaljson --sdk-version <현재 SDK 버전> --roll-forward latestFeature
```

각 classlib의 `Class1.cs`/Api의 불필요 템플릿 파일 삭제. csproj에 `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`.

- [ ] **Step 2: 실패 테스트 작성**

```csharp
// tests/FileGateway.UnitTests/Core/RemotePathTests.cs
namespace FileGateway.UnitTests.Core;

public class RemotePathTests
{
    [Theory]
    [InlineData("a/b/c", "a/b/c")]
    [InlineData("/a//b/", "a/b")]
    [InlineData(@"a\b\c", "a/b")]
    [InlineData("a/./b", "a/./b")]
    public void Normalize_unifies_separators_and_trims(string input, string expected)
        => Assert.Equal(expected, RemotePath.Normalize(input));

    [Fact]
    public void Combine_joins_root_and_relative()
        => Assert.Equal("ftproot/Logs/2026", RemotePath.Combine("ftproot", "Logs/2026"));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\x")]
    [InlineData("a/../b")]
    public void IsSafeDefinitionPath_rejects_unsafe(string path)
        => Assert.False(RemotePath.IsSafeDefinitionPath(path));

    [Theory]
    [InlineData("Logs/{yyyy}")]
    [InlineData("a/b/c")]
    public void IsSafeDefinitionPath_accepts_relative(string path)
        => Assert.True(RemotePath.IsSafeDefinitionPath(path));

    [Fact]
    public void IsUnderRoot_accepts_child_and_rejects_sibling_or_escape()
    {
        Assert.True(RemotePath.IsUnderRoot("ftproot", "ftproot/Logs/x.log"));
        Assert.True(RemotePath.IsUnderRoot("FTPRoot", "ftproot/x"));   // case-insensitive
        Assert.False(RemotePath.IsUnderRoot("ftproot", "ftproot2/x"));  // prefix 함정
        Assert.False(RemotePath.IsUnderRoot("ftproot", "other/x"));
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~RemotePathTests"`
Expected: FAIL — `RemotePath` 없음(컴파일 오류)

- [ ] **Step 4: 구현**

```csharp
// src/FileGateway.Core/Paths/RemotePath.cs
namespace FileGateway.Core.Paths;

public static class RemotePath
{
    public static string Normalize(string path)
        => string.Join("/",
             path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Combine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return Normalize(root);
        if (IsRooted(relative)) throw new ArgumentException("relative path must not be rooted", nameof(relative));
        if (!IsSafeDefinitionPath(relative)) throw new ArgumentException("unsafe relative path", nameof(relative));
        return Normalize(root + "/" + relative);
    }

    public static bool IsRooted(string path)
    {
        var p = path.Trim();
        return p.StartsWith('/') || p.StartsWith('\\') || p.Contains(':');
    }

    public static bool IsSafeDefinitionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsRooted(path)) return false;
        foreach (var seg in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (seg is "." or "..") return false;
        return true;
    }

    public static bool IsUnderRoot(string root, string path)
    {
        var r = Normalize(root);
        var p = Normalize(path);
        return (p + "/").StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests`
Expected: PASS (전체)

```bash
git add -A && git commit -m "chore: scaffold solution and add remote path boundary in core"
```

---

### Task 2: Core 파일명 비교 + glob matcher

**Files:**
- Create: `src/FileGateway.Core/Files/FileNameComparison.cs`, `src/FileGateway.Core/Files/GlobPattern.cs`
- Test: `tests/FileGateway.UnitTests/Core/GlobPatternTests.cs`

**Interfaces:**
- Consumes: Task 1 없음(독립)
- Produces: `static class FileNameComparison { StringComparer Comparer; bool Equals(string,string); int Compare(string,string); }`, `GlobPattern(string pattern) { bool Matches(string fileName); static void Validate(string pattern); }` — glob은 case-insensitive, `*`/`?`만 지원, `/` 포함 패턴 거부

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Core;

public class GlobPatternTests
{
    [Theory]
    [InlineData("*.zip", "Event_A.ZIP", true)]      // case-insensitive
    [InlineData("*.zip", "Event_A.zip", true)]
    [InlineData("Event_*.log", "Event_2026.log", true)]
    [InlineData("Event_*.log", "Trace_2026.log", false)]
    [InlineData("PM?.cfg", "PM1.cfg", true)]
    [InlineData("PM?.cfg", "PM12.cfg", false)]
    [InlineData("*.log", "sub/Event.log", false)]    // *는 / 안 넘음(파일명 전용)
    [InlineData("Event.log", "event.LOG", true)]
    public void Matches_applies_case_insensitive_glob(string pattern, string name, bool expected)
        => Assert.Equal(expected, new GlobPattern(pattern).Matches(name));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    public void Validate_rejects_invalid(string pattern)
        => Assert.Throws<ArgumentException>(() => GlobPattern.Validate(pattern));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~GlobPatternTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Core/Files/FileNameComparison.cs
namespace FileGateway.Core.Files;

public static class FileNameComparison
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    public static bool Same(string a, string b) => Comparer.Equals(a, b);
    public static int Compare(string a, string b) => Comparer.Compare(a, b);
}

// src/FileGateway.Core/Files/GlobPattern.cs
namespace FileGateway.Core.Files;

public sealed class GlobPattern(string pattern)
{
    public string Pattern { get; } = pattern;

    public static void Validate(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) throw new ArgumentException("empty file pattern");
        if (p.Contains('/')) throw new ArgumentException("file pattern must not contain '/'");
    }

    public bool Matches(string fileName) => Match(Pattern, 0, fileName, 0, out _);

    // 표준 two-pointer backtracking matcher (case-insensitive)
    private static bool Match(string p, int pi, string s, int si, out int end)
    {
        end = si;
        while (pi < p.Length)
        {
            var c = p[pi];
            if (c == '*')
            {
                for (var k = si; k <= s.Length; k++)
                    if (Match(p, pi + 1, s, k, out end)) return true;
                return false;
            }
            if (si >= s.Length) return false;
            if (c != '?' && !FileNameComparison.Same(c.ToString(), s[si].ToString())) return false;
            pi++; si++;
        }
        end = si;
        return si == s.Length;
    }
}
```

(`*`가 `/`를 넘지 않는 것은 Matches 입력이 파일명(슬래시 없음)이고 정의 검증에서 패턴의 `/`를 금지해 구조적으로 보장한다. 위 matcher는 전체 소비만 확인.)

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~GlobPatternTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(core): case-insensitive filename comparison and glob matcher"
```

---

### Task 3: Core 파일 접근 계약 + 오류 분류 + 전송 상한 스트림

**Files:**
- Create: `src/FileGateway.Core/Files/FileServerConnection.cs`, `RemoteFileEntry.cs`, `RemoteDirectoryListing.cs`, `RemoteOpenRead.cs`, `FileAccessException.cs`, `IFileAccess.cs`, `LocatedFile.cs`
- Create: `src/FileGateway.Core/Streams/ExactLengthStream.cs`
- Test: `tests/FileGateway.UnitTests/Core/ExactLengthStreamTests.cs`, `tests/FileGateway.UnitTests/TestUtils/FakeFileAccess.cs`

**Interfaces:**
- Produces (이후 모든 Task가 사용):
  - `record FileServerConnection(string ServerId, string Host, string RootPath)`
  - `record RemoteFileEntry(string Name, long Size)`
  - `record RemoteDirectoryListing(bool Exists, IReadOnlyList<RemoteFileEntry> Files)` + `static RemoteDirectoryListing Missing`
  - `record RemoteOpenRead(Stream Stream, long Length)`
  - `record LocatedFile(FileServerConnection Server, string RelativePath, string FileName, long Size)`
  - `enum FileAccessError { ConnectionFailed, AuthenticationFailed, Timeout, ProtocolError, FileNotFound, IoFailure }`
  - `class FileAccessException(FileAccessError Error, string Message, Exception? Inner = null) : Exception`
  - `interface IFileAccess { Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct); Task<long> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct); Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct); Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct); }` — `StatFileAsync`는 부재 시 `FileAccessException(FileNotFound)`, `FileExistsAsync`는 부재 시 `false`(전송 오류는 throw)
  - `class ExactLengthStream(Stream source, long declaredLength) : Stream` — 상한 도달 시 정상 종료, 이전 EOF 시 `EndOfStreamException`

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Core;

public class ExactLengthStreamTests
{
    private static MemoryStream Source(byte[] data) => new(data);

    [Fact]
    public async Task Reads_exactly_declared_length_when_source_grew()
    {
        await using var capped = new ExactLengthStream(Source("0123456789"u8.ToArray()), 5);
        using var ms = new MemoryStream();
        await capped.CopyToAsync(ms);
        Assert.Equal(5, ms.Length);
    }

    [Fact]
    public async Task Reads_all_when_lengths_match()
    {
        await using var capped = new ExactLengthStream(Source("abc"u8.ToArray()), 3);
        using var ms = new MemoryStream();
        await capped.CopyToAsync(ms);
        Assert.Equal("abc"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public async Task Throws_when_source_ends_before_declared_length()
    {
        await using var capped = new ExactLengthStream(Source("ab"u8.ToArray()), 5);
        await Assert.ThrowsAsync<EndOfStreamException>(() => capped.CopyToAsync(new MemoryStream()));
    }

    [Fact]
    public async Task Zero_declared_length_returns_empty()
    {
        await using var capped = new ExactLengthStream(Source("ab"u8.ToArray()), 0);
        Assert.Equal(0, await capped.ReadAsync(new byte[8]));
    }
}
```

`FakeFileAccess`(`tests/FileGateway.UnitTests/TestUtils/FakeFileAccess.cs`) — 이후 Task 8~13의 Logs/Configurations 단위테스트 공용:

```csharp
namespace FileGateway.UnitTests.TestUtils;

/// <summary>경로(대소문자 무시) → 파일 집합 in-memory IFileAccess. 디렉터리는 파일 경로의 부모로 유추.</summary>
public sealed class FakeFileAccess : IFileAccess
{
    private readonly Dictionary<string, byte[]> _files = new(FileNameComparison.Comparer);

    public void AddFile(string relativePath, byte[] content) => _files[relativePath] = content;
    public void RemoveFile(string relativePath) => _files.Remove(relativePath);

    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
    {
        var prefix = RemotePath.Normalize(dir) + "/";
        if (!_files.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(RemoteDirectoryListing.Missing);
        var entries = _files.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                         && !kv.Key[prefix.Length..].Contains('/'))
                            .Select(kv => new RemoteFileEntry(kv.Key[(kv.Key.LastIndexOf('/') + 1)..], kv.Value.Length))
                            .ToList();
        return Task.FromResult(new RemoteDirectoryListing(true, entries));
    }

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(path, out var v)
            ? v.Length
            : throw new FileAccessException(FileAccessError.FileNotFound, "not found"));

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.ContainsKey(path));

    public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(path, out var v)
            ? new RemoteOpenRead(new MemoryStream(v, writable: false), v.Length)
            : throw new FileAccessException(FileAccessError.FileNotFound, "not found"));
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~ExactLengthStreamTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Core/Streams/ExactLengthStream.cs
namespace FileGateway.Core.Streams;

/// <summary>선언 길이까지만 전송하고(파일 growth 무시), 선언 길이 전에 소스가 끝나면 실패(truncate/rotation).</summary>
public sealed class ExactLengthStream(Stream source, long declaredLength) : Stream
{
    private long _remaining = declaredLength;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => declaredLength;
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_remaining == 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await source.ReadAsync(buffer[..toRead], ct);
        if (read == 0)
            throw new EndOfStreamException(
                $"remote stream ended after {declaredLength - _remaining} of {declaredLength} declared bytes");
        _remaining -= read;
        return read;
    }

    public override async ValueTask DisposeAsync() { await source.DisposeAsync(); base.DisposeAsync().AsTask().Dispose(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int ReadTimeout { get => source.ReadTimeout; set => source.ReadTimeout = value; }
}
```

나머지 계약 파일들은 위 Interfaces에 적은 시그니처 그대로 record/interface/enum 정의.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests` / Expected: PASS

```bash
git add -A && git commit -m "feat(core): protocol-agnostic file access contract and exact-length transfer stream"
```

---

### Task 4: Core token codec 계약 + Infrastructure DataProtection 구현

**Files:**
- Create: `src/FileGateway.Core/Tokens/ITokenCodec.cs` (TokenPayload/TokenValidity/TokenDecodeResult 포함)
- Create: `src/FileGateway.Infrastructure/Tokens/DataProtectionTokenCodec.cs`
- Infrastructure csproj: 패키지 없음(DataProtection은 Framework 참조. csproj에 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 추가)
- Test: `tests/FileGateway.UnitTests/Tokens/TokenCodecTests.cs` (Infrastructure 참조 이미 있음)

**Interfaces:**
- Produces:
  - `record TokenPayload(string Purpose, IReadOnlyDictionary<string,string> Claims, DateTimeOffset IssuedAt, TimeSpan Ttl)`
  - `enum TokenValidity { Valid, Invalid, Expired }`
  - `record TokenDecodeResult(TokenValidity Validity, TokenPayload? Payload)`
  - `interface ITokenCodec { string Protect(TokenPayload payload); TokenDecodeResult Unprotect(string token, string expectedPurpose); }` — protector purpose를 payload.Purpose별로 분리(확정 결정 #7). codec이 expected purpose 불일치 토큰(`Invalid`)으로 거부하며, 호출자는 각 feature의 purpose 상수(`fg.fileid.log` 등)를 전달한다. (2026-08-23 PR #2 리뷰 보강으로 개정 — 이후 Task의 `Unprotect(token)` 예제 코드는 모두 이 시그니처로 읽는다)
  - `class DataProtectionTokenCodec(IDataProtectionProvider provider) : ITokenCodec`

- [ ] **Step 1: 실패 테스트**

```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.UnitTests.Tokens;

public class TokenCodecTests
{
    private static ITokenCodec CreateCodec(string? keyDir = null)
    {
        var services = new ServiceCollection();
        var b = services.AddDataProtection();
        if (keyDir != null) b.PersistKeysToFileSystem(new DirectoryInfo(keyDir));
        return new DataProtectionTokenCodec(services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    }

    private static TokenPayload Sample(DateTimeOffset? issued = null) => new(
        "fg.fileid.log",
        new Dictionary<string, string> { ["equipmentId"] = "EQ-001", ["fileName"] = "Event_A.zip" },
        issued ?? DateTimeOffset.UtcNow,
        TimeSpan.FromHours(24));

    [Fact]
    public void Round_trips_claims()
    {
        var codec = CreateCodec();
        var result = codec.Unprotect(codec.Protect(Sample()));
        Assert.Equal(TokenValidity.Valid, result.Validity);
        Assert.Equal("EQ-001", result.Payload!.Claims["equipmentId"]);
        Assert.Equal("fg.fileid.log", result.Payload.Purpose);
    }

    [Fact]
    public void Token_does_not_expose_payload_plaintext()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample());
        Assert.DoesNotContain("EQ-001", token, StringComparison.Ordinal);
        Assert.DoesNotContain("Event_A.zip", token, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("AAAA-zzz")]
    public void Tampered_or_malformed_token_is_invalid(string token)
        => Assert.Equal(TokenValidity.Invalid, CreateCodec().Unprotect(token).Validity);

    [Fact]
    public void Modified_ciphertext_is_invalid()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample());
        var bytes = Microsoft.AspNetCore.DataProtection.Base64Url.DecodeFromChars(token.ToCharArray());
        bytes[10] ^= 0xFF;
        var tampered = Microsoft.AspNetCore.DataProtection.Base64Url.EncodeToString(bytes);
        Assert.Equal(TokenValidity.Invalid, codec.Unprotect(tampered).Validity);
    }

    [Fact]
    public void Expired_token_reports_expired_not_invalid()
    {
        var codec = CreateCodec();
        var token = codec.Protect(Sample(DateTimeOffset.UtcNow.AddHours(-25)));
        Assert.Equal(TokenValidity.Expired, codec.Unprotect(token).Validity);
    }

    [Fact]
    public void New_codec_instance_with_same_key_directory_validates_prior_tokens()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fg-keys-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var token = CreateCodec(dir).Protect(Sample());
        // "재시작/rotation 후 동일 key ring" 시뮬레이션: 새 provider 인스턴스
        Assert.Equal(TokenValidity.Valid, CreateCodec(dir).Unprotect(token).Validity);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~TokenCodecTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Core/Tokens/ITokenCodec.cs
namespace FileGateway.Core.Tokens;

public sealed record TokenPayload(
    string Purpose,
    IReadOnlyDictionary<string, string> Claims,
    DateTimeOffset IssuedAt,
    TimeSpan Ttl);

public enum TokenValidity { Valid, Invalid, Expired }

public sealed record TokenDecodeResult(TokenValidity Validity, TokenPayload? Payload);

public interface ITokenCodec
{
    string Protect(TokenPayload payload);
    TokenDecodeResult Unprotect(string token);
}

// src/FileGateway.Infrastructure/Tokens/DataProtectionTokenCodec.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileGateway.Core.Tokens;
using Microsoft.AspNetCore.DataProtection;

namespace FileGateway.Infrastructure.Tokens;

public sealed class DataProtectionTokenCodec(IDataProtectionProvider provider) : ITokenCodec
{
    private const string ProtectorPurpose = "filegateway.tokens.v1";

    private sealed record EncodedToken(
        string Purpose, IReadOnlyDictionary<string, string> Claims,
        DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    public string Protect(TokenPayload payload)
    {
        var inner = new EncodedToken(
            payload.Purpose, payload.Claims, payload.IssuedAt, payload.IssuedAt.Add(payload.Ttl));
        var json = JsonSerializer.SerializeToUtf8Bytes(inner);
        var protectedBytes = provider.CreateProtector(ProtectorPurpose).Protect(json);
        return Base64Url.EncodeToString(protectedBytes);
    }

    public TokenDecodeResult Unprotect(string token)
    {
        try
        {
            byte[] bytes;
            try { bytes = Base64Url.DecodeFromChars(token.ToCharArray()); }
            catch (FormatException) { return Invalid(); }
            var json = provider.CreateProtector(ProtectorPurpose).Unprotect(bytes);
            var inner = JsonSerializer.Deserialize<EncodedToken>(json);
            if (inner is null) return Invalid();
            if (inner.ExpiresAt <= DateTimeOffset.UtcNow) return new(TokenValidity.Expired, null);
            return new(TokenValidity.Valid,
                new TokenPayload(inner.Purpose, inner.Claims, inner.IssuedAt, inner.ExpiresAt - inner.IssuedAt));
        }
        catch (CryptographicException) { return Invalid(); }
        catch (JsonException) { return Invalid(); }
    }

    private static TokenDecodeResult Invalid() => new(TokenValidity.Invalid, null);
}
```

주의: `Base64Url`은 `Microsoft.AspNetCore.DataProtection` 네임스페이스에 있다. FrameworkReference는 Infrastructure에, 테스트 프로젝트에는 이미 전이 참조로 동작한다(동작하지 않으면 테스트 csproj에도 FrameworkReference 추가).

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~TokenCodecTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(tokens): protected opaque token codec with data protection"
```

---

### Task 5: Infrastructure FTP Adapter (FluentFTP)

**Files:**
- Infrastructure csproj: `dotnet add src/FileGateway.Infrastructure package FluentFTP`
- Create: `src/FileGateway.Infrastructure/Ftp/FtpOptions.cs`, `FtpConcurrencyLimiter.cs`, `FtpFileAccess.cs`
- Test: `tests/FileGateway.IntegrationTests/Ftp/FtpAdapterFixture.cs`, `tests/FileGateway.IntegrationTests/Ftp/FtpFileAccessTests.cs`
- IntegrationTests csproj: `dotnet add package FubarDev.FtpServer` + `dotnet add package FubarDev.FtpServer.FileSystem.InMemory`

**Interfaces:**
- Consumes: Task 3 `IFileAccess`, `FileAccessException`, Task 1 `RemotePath`
- Produces:
  - `enum FtpSecurity { Plain, ExplicitTls, ImplicitTls }`
  - `class FtpOptions { string? UserName; string? Password; FtpSecurity Security = FtpSecurity.Plain; bool AcceptUntrustedCertificates = false; int ConnectTimeoutSeconds = 15; int ReadTimeoutSeconds = 60; int MaxConcurrentGlobal = 50; int MaxConcurrentPerServer = 5; int? HostPortOverride; static FtpConfig ToFtpConfig(FtpOptions o); }` — `ToFtpConfig`는 timeout/EncryptionMode/인증서 검증을 `FtpConfig`에 반영하는 순수 매핑(단위테스트 대상)
  - `sealed class FtpLease : IAsyncDisposable` + `class FtpConcurrencyLimiter(FtpOptions options)` — `Task<FtpLease> AcquireAsync(FileServerConnection server, CancellationToken ct)`(전체+서버별 permit 확보, Dispose로 해제)와 `Task<T> RunAsync<T>(FileServerConnection server, Func<CancellationToken, Task<T>> op, CancellationToken ct)`(단기 명령용: lease를 잡고 op 완료 후 해제)
  - `class FtpFileAccess(FtpOptions options, FtpConcurrencyLimiter limiter) : IFileAccess` — `OpenReadAsync`의 반환 스트림은 **client와 lease를 소유**하고 `DisposeAsync`에서 함께 해제한다(다운로드가 진행되는 동안 동시성 한도가 유지된다). 연결 이후 FTP 명령 오류도 `ConnectAsync`와 동일한 매핑으로 변환한다.

- [ ] **Step 1: FTP 테스트 서버 fixture**

```csharp
// tests/FileGateway.IntegrationTests/Ftp/FtpAdapterFixture.cs
using FubarDev.FtpServer;
using FubarDev.FtpServer.FileSystem.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace FileGateway.IntegrationTests.Ftp;

public sealed class FtpAdapterFixture : IAsyncLifetime
{
    public const string UserName = "fgtest", Password = "fgpass";
    private readonly ServiceCollection _services = new();
    private ServiceProvider? _provider;
    private IFtpServer? _server;

    public int Port { get; private set; }
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task InitializeAsync()
    {
        _services.AddFtpServer(sb => sb.UseInMemoryFileSystem().UseCustomMembership(
            new DictionaryMembershipProvider(new Dictionary<string, string> { [UserName] = Password })));
        _provider = _services.BuildServiceProvider();
        _server = _provider.GetRequiredService<IFtpServer>();
        Port = 21000 + Random.Shared.Next(0, 2000);
        _server.Start(Port);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _server?.Stop();
        _provider?.Dispose();
        return Task.CompletedTask;
    }
}
```

(`UseCustomMembership`/`DictionaryMembershipProvider` 제공 여부는 패키지 버전에 따라 확인하고, 없으면 `IMembershipProvider` 구현체 10줄로 직접 제공한다. in-memory FS는 fixture 시작 전 시드 대신, 아래 AddFile 후 실제 업로드로 시드한다 — 이 Task 테스트 안에서 파일을 PUT/업로드하는 헬퍼를 만든다.)

시드 헬퍼: fixture에 `Task SeedAsync(IDictionary<string,byte[]> files)` 추가 — FluentFTP 자체로 `UploadFileAsync`( MemoryStream )하거나 fixture 제공 업로드 API 사용. 테스트마다 `client.CreateDirectory/UploadStream`으로 셋업하는 것이 계약 검증에 방해되지 않으므로, 테스트 body에서 직접 `AsyncFtpClient`로 시드한다.

- [ ] **Step 2: 실패 테스트**

```csharp
// tests/FileGateway.IntegrationTests/Ftp/FtpFileAccessTests.cs
using FluentFTP;

namespace FileGateway.IntegrationTests.Ftp;

[Collection("ftp")]
public class FtpFileAccessTests(FtpAdapterFixture ftp) : IClassFixture<FtpAdapterFixture>
{
    private static FileServerConnection Server(int port) => new("S1", "127.0.0.1", "ftproot");
    // fixture Port를 host 문자열 대신 FtpOptions에 전달한다(아래 Create 참조)

    private static (FtpFileAccess Access, FtpOptions Opt) Create(FtpAdapterFixture f)
    {
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password };
        return (new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt)), opt);
    }

    private static async Task Seed(FtpAdapterFixture f, string path, byte[] content)
    {
        using var client = new AsyncFtpClient("127.0.0.1", FtpAdapterFixture.UserName, FtpAdapterFixture.Password, f.Port);
        await client.Connect();
        await client.UploadStreamAsync(new MemoryStream(content), path, createRemoteDir: true);
    }

    private static FtpOptions WithPort(FtpAdapterFixture f, FtpOptions o) { o.HostPortOverride = f.Port; return o; }

    [Fact]
    public async Task ListFiles_returns_entries_when_directory_exists()
    {
        await Seed(ftp, "ftproot/Logs/2026/08/22/18/Event_A.zip", "abc"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListFilesAsync(Server(ftp.Port), "Logs/2026/08/22/18", CancellationToken.None);
        Assert.True(listing.Exists);
        var file = Assert.Single(listing.Files);
        Assert.Equal("Event_A.zip", file.Name);
        Assert.Equal(3, file.Size);
    }

    [Fact]
    public async Task ListFiles_reports_missing_directory_as_not_exists()
    {
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var listing = await access.ListFilesAsync(Server(ftp.Port), "Logs/nope", CancellationToken.None);
        Assert.False(listing.Exists);
        Assert.Empty(listing.Files);
    }

    [Fact]
    public async Task StatFile_throws_FileNotFound_for_missing_file()
    {
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => access.StatFileAsync(Server(ftp.Port), "ftproot/missing.bin", CancellationToken.None));
        Assert.Equal(FileAccessError.FileNotFound, ex.Error);
    }

    [Fact]
    public async Task FileExists_distinguishes_missing_and_present()
    {
        await Seed(ftp, "ftproot/Logs/present.bin", "x"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        Assert.True(await access.FileExistsAsync(Server(ftp.Port), "Logs/present.bin", CancellationToken.None));
        Assert.False(await access.FileExistsAsync(Server(ftp.Port), "Logs/absent.bin", CancellationToken.None));
    }

    [Fact]
    public async Task OpenRead_returns_stream_and_length()
    {
        await Seed(ftp, "ftproot/Logs/data.bin", "0123456789"u8.ToArray());
        var (access, opt) = Create(ftp); WithPort(ftp, opt);
        var open = await access.OpenReadAsync(Server(ftp.Port), "Logs/data.bin", CancellationToken.None);
        await using var s = open.Stream;
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        Assert.Equal(10, ms.Length);
        Assert.Equal(10, open.Length);
    }

    [Fact]
    public async Task Wrong_credentials_map_to_AuthenticationFailed()
    {
        var opt = new FtpOptions { UserName = "nobody", Password = "bad" };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));
        var ex = await Assert.ThrowsAsync<FileAccessException>(
            () => access.ListFilesAsync(Server(ftp.Port), "Logs", CancellationToken.None));
        Assert.Equal(FileAccessError.AuthenticationFailed, ex.Error);
    }

    [Fact]
    public async Task Unreachable_host_maps_to_ConnectionFailed()
    {
        var opt = new FtpOptions { ConnectTimeoutSeconds = 2 };
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));
        var ex = await Assert.ThrowsAsync<FileAccessException>(() => access.ListFilesAsync(
            new FileServerConnection("S1", "127.0.0.1", "ftproot"), "Logs", CancellationToken.None));
        // 127.0.0.1:21 거부 → ConnectionFailed. 옵션의 포트 오버라이드 없이 기본 21 사용.
        Assert.Equal(FileAccessError.ConnectionFailed, ex.Error);
    }

    [Fact]
    public void FtpConfig_maps_security_and_certificate_policy()
    {
        var plain = FtpOptions.ToFtpConfig(new FtpOptions());
        Assert.Equal(FtpEncryptionMode.None, plain.EncryptionMode);
        Assert.False(plain.ValidateAnyCertificate);

        var ftps = FtpOptions.ToFtpConfig(new FtpOptions
            { Security = FtpSecurity.ExplicitTls, AcceptUntrustedCertificates = true });
        Assert.Equal(FtpEncryptionMode.Explicit, ftps.EncryptionMode);
        Assert.True(ftps.ValidateAnyCertificate);

        var implicitFtps = FtpOptions.ToFtpConfig(new FtpOptions { Security = FtpSecurity.ImplicitTls });
        Assert.Equal(FtpEncryptionMode.Implicit, implicitFtps.EncryptionMode);
    }

    [Fact]
    public async Task Open_stream_holds_concurrency_lease_until_disposed()
    {
        await Seed(ftp, "ftproot/Logs/a.bin", "12345"u8.ToArray());
        await Seed(ftp, "ftproot/Logs/b.bin", "67890"u8.ToArray());
        var opt = new FtpOptions { UserName = FtpAdapterFixture.UserName, Password = FtpAdapterFixture.Password,
                                   MaxConcurrentPerServer = 1 };
        WithPort(ftp, opt);
        var access = new FtpFileAccess(opt, new FtpConcurrencyLimiter(opt));

        var first = await access.OpenReadAsync(Server(ftp.Port), "Logs/a.bin", CancellationToken.None);
        // 첫 스트림이 살아있는 동안 같은 서버의 두 번째 open은 permit 대기로 timeout/fail해야 한다
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", cts.Token));

        await first.Stream.DisposeAsync(); // lease 해제
        var second = await access.OpenReadAsync(Server(ftp.Port), "Logs/b.bin", CancellationToken.None);
        await second.Stream.DisposeAsync();
    }
}
```

주의: 테스트의 `FtpOptions.HostPortOverride`(기본 21)는 테스트 편의용 필드로 `FtpOptions`에 추가(`int? HostPortOverride = null`).

- [ ] **Step 3: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~FtpFileAccessTests"` / Expected: FAIL

- [ ] **Step 4: 구현**

```csharp
// src/FileGateway.Infrastructure/Ftp/FtpOptions.cs
using FluentFTP;

namespace FileGateway.Infrastructure.Ftp;

public enum FtpSecurity { Plain, ExplicitTls, ImplicitTls }

public sealed class FtpOptions
{
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public FtpSecurity Security { get; set; } = FtpSecurity.Plain;
    public bool AcceptUntrustedCertificates { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int ReadTimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentGlobal { get; set; } = 50;
    public int MaxConcurrentPerServer { get; set; } = 5;
    public int? HostPortOverride { get; set; } // 테스트 편의용(기본 21)

    public static FtpConfig ToFtpConfig(FtpOptions o) => new()
    {
        ConnectTimeout = o.ConnectTimeoutSeconds * 1000,
        ReadTimeout = o.ReadTimeoutSeconds * 1000,
        DataConnectionConnectTimeout = o.ConnectTimeoutSeconds * 1000,
        DataConnectionReadTimeout = o.ReadTimeoutSeconds * 1000,
        EncryptionMode = o.Security switch
        {
            FtpSecurity.ExplicitTls => FtpEncryptionMode.Explicit,
            FtpSecurity.ImplicitTls => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None,
        },
        ValidateAnyCertificate = o.AcceptUntrustedCertificates, // self-signed 내부 서버 허용 여부(운영 설정)
    };
}

// src/FileGateway.Infrastructure/Ftp/FtpConcurrencyLimiter.cs
namespace FileGateway.Infrastructure.Ftp;

/// <summary>전체/서버별 FTP 동시성 permit. 단기 명령은 RunAsync, 스트리밍은 lease를 스트림에 소유시킨다.</summary>
public sealed class FtpConcurrencyLimiter(FtpOptions options)
{
    private readonly SemaphoreSlim _global = new(options.MaxConcurrentGlobal, options.MaxConcurrentGlobal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perServer = new(StringComparer.OrdinalIgnoreCase);

    public sealed class FtpLease(SemaphoreSlim global, SemaphoreSlim perServer) : IAsyncDisposable
    {
        private int _released;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            perServer.Release(); global.Release();
            await ValueTask.CompletedTask;
        }
    }

    public async Task<FtpLease> AcquireAsync(FileServerConnection server, CancellationToken ct)
    {
        var perServer = _perServer.GetOrAdd(server.Host,
            _ => new SemaphoreSlim(options.MaxConcurrentPerServer, options.MaxConcurrentPerServer));
        await _global.WaitAsync(ct);
        try { await perServer.WaitAsync(ct); }
        catch { _global.Release(); throw; }
        return new FtpLease(_global, perServer);
    }

    public async Task<T> RunAsync<T>(FileServerConnection server, Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        await using var lease = await AcquireAsync(server, ct);
        return await op(ct);
    }
}

// src/FileGateway.Infrastructure/Ftp/FtpFileAccess.cs
using FluentFTP;
using FluentFTP.Exceptions;
using System.Net.Sockets;

namespace FileGateway.Infrastructure.Ftp;

public sealed class FtpFileAccess(FtpOptions options, FtpConcurrencyLimiter limiter) : IFileAccess
{
    public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string dir, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            var items = await client.GetListing(
                RemotePath.Combine(server.RootPath, dir), FtpListOption.Modify | FtpListOption.Size, token);
            return new RemoteDirectoryListing(true,
                items.Where(i => i.Type == FtpFileSystemObjectType.File)
                     .Select(i => new RemoteFileEntry(i.Name, i.Size)).ToList());
        }), ct);

    public Task<long> StatFileAsync(FileServerConnection server, string path, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            var info = await GetObjectInfoOrNullAsync(client, server, path, token);
            if (info is null) throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            return info.Size;
        }), ct);

    public Task<bool> FileExistsAsync(FileServerConnection server, string path, CancellationToken ct)
        => limiter.RunAsync(server, token => WrapAsync(async () =>
        {
            using var client = await ConnectAsync(server, token);
            return await GetObjectInfoOrNullAsync(client, server, path, token) is not null;
        }), ct);

    public async Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string path, CancellationToken ct)
    {
        // lease와 client를 반환 스트림이 소유: 다운로드가 끝나야 permit이 해제된다.
        var lease = await limiter.AcquireAsync(server, ct);
        AsyncFtpClient? client = null;
        try
        {
            client = await ConnectAsync(server, ct);
            var full = RemotePath.Combine(server.RootPath, path);
            var info = await GetObjectInfoOrNullAsync(client, server, path, ct); // 시작 직전 크기 관측
            if (info is null) throw new FileAccessException(FileAccessError.FileNotFound, "file not found");
            var stream = await client.OpenRead(full, 0, ct);
            return new RemoteOpenRead(new OwnedFtpStream(stream, client, lease), info.Size);
        }
        catch (Exception ex)
        {
            if (client is not null) await client.DisposeAsync();
            await lease.DisposeAsync();
            if (ex is FileAccessException) throw;
            throw Classify(ex); // 연결/명령 구분 없이 동일 매핑
        }
    }

    private static async Task<FtpListItem?> GetObjectInfoOrNullAsync(
        AsyncFtpClient client, FileServerConnection server, string path, CancellationToken ct)
    {
        try { return await client.GetObjectInfo(RemotePath.Combine(server.RootPath, path), ct); }
        catch (FtpException ex) when (IsFileNotFoundReply(ex)) { return null; } // MLST 550 → 부재
    }

    /// <summary>연결·명령 구분 없이 모든 FTP 오류를 FileAccessError로 변환한다.</summary>
    private static async Task<T> WrapAsync<T>(Func<Task<T>> op)
    {
        try { return await op(); }
        catch (Exception ex) when (ex is not FileAccessException) { throw Classify(ex); }
    }

    private static FileAccessException Classify(Exception ex) => ex switch
    {
        FtpAuthenticationException => new(FileAccessError.AuthenticationFailed, "ftp auth failed", ex),
        SocketException => new(FileAccessError.ConnectionFailed, "ftp connection failed", ex),
        TimeoutException => new(FileAccessError.Timeout, "ftp timeout", ex),
        FtpException => new(FileAccessError.ProtocolError, "ftp protocol error", ex),
        _ => new(FileAccessError.ProtocolError, "ftp failure", ex),
    };


    private async Task<AsyncFtpClient> ConnectAsync(FileServerConnection server, CancellationToken ct)
    {
        var client = new AsyncFtpClient(server.Host, options.UserName ?? "", options.Password ?? "",
            options.HostPortOverride ?? 21, FtpOptions.ToFtpConfig(options));
        try { await client.Connect(ct); return client; }
        catch { await client.DisposeAsync(); throw; }
    }

    private static bool IsFileNotFoundReply(FtpException ex)
        => ex.Message.Contains("550", StringComparison.Ordinal) ||
           ex.InnerException?.Message.Contains("550", StringComparison.Ordinal) == true;

    private static bool IsNoSuchPath(Exception ex) => IsFileNotFoundReply((FtpException)ex);

    private sealed class OwnedFtpStream(Stream inner, AsyncFtpClient client, FtpConcurrencyLimiter.FtpLease lease) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct) => await inner.ReadAsync(b, ct);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override long Length => inner.Length;
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await client.DisposeAsync();
            await lease.DisposeAsync();
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

```

구현 정리 노트: `ListFilesAsync`의 "디렉터리 부재" 판정(`IsNoSuchPath`)은 op 본문에서 `FtpException`을 `ProtocolError`로 변환하기 전에 먼저 적용해 `RemoteDirectoryListing.Missing`을 반환한다(테스트 `ListFiles_reports_missing_directory_as_not_exists`가 검증). 미사용 헬퍼는 제거한다.

구현 시 FluentFTP 실제 예외 타입/오버로드를 통합테스트로 검증하며 보정한다(550 감정, `OpenRead` 오버로드, dispose 소유권). 불일치가 있으면 테스트가 통과하도록 최소한으로 수정한다.

- [ ] **Step 5: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~FtpFileAccessTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(infra): fluentftp adapter with error mapping and concurrency limits"
```

---

### Task 6: 기준정보 모델 + 전체 검증 + 스냅샷

**Files:**
- Create: `src/FileGateway.Logs/Definitions/Models.cs`, `LogDefinitionValidator.cs`, `src/FileGateway.Logs/Tokens/LogTokenKinds.cs`
- Create: `src/FileGateway.Configurations/Definitions/Models.cs`, `ConfigurationDefinitionValidator.cs`, `src/FileGateway.Configurations/Tokens/ConfigurationTokenKinds.cs`
- Create: `src/FileGateway.Infrastructure/ReferenceData/ReferenceDataRaw.cs`, `ReferenceDataSnapshot.cs`, `ReferenceDataSnapshotBuilder.cs`
- Test: `tests/FileGateway.UnitTests/ReferenceData/SnapshotBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1~3 (`RemotePath`, `GlobPattern`)
- Produces (이후 Task 전체가 사용):
  - Logs: `enum GenerationType { Hourly, Daily, Continuous }`, `enum Cardinality { Single, Multiple }`, `enum MetadataMode { Template, Regex }`, `record LogDiscoveryRule(string PathTemplate, string FilePattern, Cardinality Cardinality)`, `record MetadataMapping(string Group, string Target, string? Format)`, `record LogMetadataRule(MetadataMode Mode, string Pattern, IReadOnlyList<MetadataMapping> Mappings)`, `record EquipmentLogDefinition(string EquipmentId, string LogType, string ServerId, GenerationType GenerationType, LogDiscoveryRule DiscoveryRule, LogMetadataRule MetadataRule)`, `record ResolvedLogDefinition(EquipmentLogDefinition Definition, FileServerConnection Server)`, `record LogTypeSummary(string LogType, string GenerationType)`
  - `static class LogDefinitionValidator { IReadOnlyList<string> Validate(EquipmentLogDefinition def); }`
  - `static class LogTokenKinds { const string FileIdPurpose = "fg.fileid.log"; const string ContinuationPurpose = "fg.page.log"; }`
  - Configurations: `record CurrentRule(string PathTemplate, string FilePattern)`, `record HistoryRule(string PathTemplate, string FilePattern, string MarkerPathTemplate)`, `record EquipmentConfigurationDefinition(string EquipmentId, string ConfigurationType, string ServerId, CurrentRule CurrentRule, HistoryRule HistoryRule)`, `record ResolvedConfigurationDefinition(EquipmentConfigurationDefinition Definition, FileServerConnection Server)`
  - `static class ConfigurationDefinitionValidator { IReadOnlyList<string> Validate(EquipmentConfigurationDefinition def); }`
  - `static class ConfigurationTokenKinds { const string FileIdCurrentPurpose = "fg.fileid.cfgcurrent"; const string FileIdSnapshotPurpose = "fg.fileid.cfgsnapshot"; const string ContinuationPurpose = "fg.page.cfghistory"; }`
  - Infrastructure: `record RawServer(string ServerId, string Host, string RootPath)`, `record RawLogDefinition(string EquipmentId, string LogType, string ServerId, string GenerationType, string PathTemplate, string FilePattern, string Cardinality, string MetadataMode, string MetadataPattern, string MetadataMappingsJson)`, `record RawConfigurationDefinition(string EquipmentId, string ConfigurationType, string ServerId, string CurrentPathTemplate, string CurrentFilePattern, string HistoryPathTemplate, string HistoryFilePattern, string HistoryMarkerPathTemplate)`, `record ReferenceDataRaw(IReadOnlyList<string> EquipmentIds, IReadOnlyList<RawServer> Servers, IReadOnlyList<RawLogDefinition> LogDefinitions, IReadOnlyList<RawConfigurationDefinition> ConfigurationDefinitions)`
  - `class ReferenceDataSnapshot { IReadOnlySet<string> EquipmentIds; IReadOnlyDictionary<string, FileServerConnection> Servers; ResolvedLogDefinition? FindLog(equipmentId, logType); ResolvedConfigurationDefinition? FindConfiguration(equipmentId, configurationType); IReadOnlyList<LogTypeSummary> GetLogSummaries(equipmentId); IReadOnlyList<string> GetConfigurationTypeSummaries(equipmentId); }`
  - `static class ReferenceDataSnapshotBuilder { static ReferenceDataSnapshot Build(ReferenceDataRaw raw); }` — 검증 실패 시 `ReferenceDataValidationException(IReadOnlyList<string> Errors)`

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.ReferenceData;

public class SnapshotBuilderTests
{
    private static ReferenceDataRaw Valid() => new(
        ["EQ-001", "EQ-002"],
        [new RawServer("SRV1", "ftp1.internal", "ftproot")],
        [new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
            "Logs/{yyyy}/{MM}/{dd}/{HH}", "*.zip", "Multiple",
            "Template", "{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", "[]")],
        []);

    [Fact]
    public void Builds_snapshot_with_indexes()
    {
        var snap = ReferenceDataSnapshotBuilder.Build(Valid());
        Assert.Contains("EQ-001", snap.EquipmentIds);
        var def = snap.FindLog("EQ-001", "eventlog"); // logType 조회는 대소문자 그대로 지원(정확 일치)
        Assert.Null(def);
        def = snap.FindLog("EQ-001", "EventLog");
        Assert.NotNull(def);
        Assert.Equal("ftp1.internal", def.Server.Host);
        Assert.Equal("EventLog", Assert.Single(snap.GetLogSummaries("EQ-001")).LogType);
    }

    [Fact]
    public void Rejects_duplicate_equipment_logType()
    {
        var raw = Valid();
        raw = raw with { LogDefinitions = [.. raw.LogDefinitions, raw.LogDefinitions[0]] };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));
    }

    [Fact]
    public void Rejects_unknown_server_and_unknown_equipment()
    {
        var raw = Valid() with
        {
            LogDefinitions = [Valid().LogDefinitions[0] with { ServerId = "NOPE" }]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));

        var raw2 = Valid() with
        {
            ConfigurationDefinitions = [new RawConfigurationDefinition(
                "EQ-X", "PM", "SRV1", "PM", "PM_*.cfg", "History/{yyyy}/{MM}/{dd}", "PM_*.cfg", "{yyyy}/{MM}/{dd}/_DONE")]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw2));
    }

    [Fact]
    public void Rejects_path_escape_attempt()
    {
        var raw = Valid() with
        {
            LogDefinitions = [Valid().LogDefinitions[0] with { PathTemplate = "../other/{yyyy}" }]
        };
        Assert.Throws<ReferenceDataValidationException>(() => ReferenceDataSnapshotBuilder.Build(raw));
    }

    [Theory]
    [InlineData("Logs/../../x", "Escape")]
    [InlineData("/abs/{yyyy}", "Rooted")]
    [InlineData("Logs/{yyyy}", "BadGlob")]
    public void Validator_reports_specific_errors(string pathTemplate, string _case)
    {
        var def = new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1", GenerationType.Hourly,
            new LogDiscoveryRule(pathTemplate, _case == "BadGlob" ? "a/b" : "*.zip", Cardinality.Single),
            new LogMetadataRule(MetadataMode.Template,
                _case == "BadGlob" ? "{yyyy}/{MM}/{dd}/{HH}/Event.zip" : pathTemplate + "/Event.zip", []));
        var errors = LogDefinitionValidator.Validate(def);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validator_requires_hour_tokens_for_hourly_and_forbids_for_daily()
    {
        var hourly = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "{yyyy}/{MM}/{dd}/Event.log", [])); // {HH} 없음
        Assert.Contains(LogDefinitionValidator.Validate(hourly), e => e.Contains("HH"));

        var daily = new EquipmentLogDefinition("E", "L", "S", GenerationType.Daily,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "{yyyy}/{MM}/{dd}/{HH}/Event.log", [])); // Daily에 HH
        Assert.Contains(LogDefinitionValidator.Validate(daily), e => e.Contains("Daily"));
    }

    [Fact]
    public void Validator_accepts_continuous_without_date_tokens()
    {
        var def = new EquipmentLogDefinition("E", "Trace", "S", GenerationType.Continuous,
            new LogDiscoveryRule("Trace/current", "Trace_*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Template, "Trace/current/Trace_{subtype}.log", []));
        Assert.Empty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Validator_rejects_unsupported_regex_target_or_missing_format()
    {
        var def = new EquipmentLogDefinition("E", "L", "S", GenerationType.Hourly,
            new LogDiscoveryRule("Logs", "*.log", Cardinality.Multiple),
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(\d{4})/",
                [new MetadataMapping("1", "timestamp", null)])); // 숫자 그룹명 불가/형식 없음
        Assert.NotEmpty(LogDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Configuration_validator_requires_date_tokens_in_history_rules()
    {
        var def = new EquipmentConfigurationDefinition("E", "PM", "S",
            new CurrentRule("PM/current", "PM_*.cfg"),
            new HistoryRule("PM/history", "PM_*.cfg", "PM/history/_DONE")); // 날짜 토큰 없음
        Assert.NotEmpty(ConfigurationDefinitionValidator.Validate(def));
    }

    [Fact]
    public void Snapshot_exposes_root_boundary_via_servers() // rootPath 경계 데이터가 스냅샷에 보존됨
    {
        var snap = ReferenceDataSnapshotBuilder.Build(Valid());
        Assert.Equal("ftproot", snap.Servers["SRV1"].RootPath);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~SnapshotBuilderTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

`Models.cs` 파일들은 Interfaces에 정의한 record/enum 그대로. 검증기 핵심:

```csharp
// src/FileGateway.Logs/Definitions/LogDefinitionValidator.cs
namespace FileGateway.Logs.Definitions;

public static class LogDefinitionValidator
{
    private static readonly string[] PathTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}"];
    private static readonly string[] DateTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}", "{mm}"];

    public static IReadOnlyList<string> Validate(EquipmentLogDefinition def)
    {
        var errors = new List<string>();
        var rule = def.DiscoveryRule;

        if (!RemotePath.IsSafeDefinitionPath(rule.PathTemplate))
            errors.Add($"pathTemplate unsafe: {rule.PathTemplate}");
        else if (rule.PathTemplate.Split('/').Any(s => s.Contains("..")))
            errors.Add("pathTemplate contains '..'");
        if (!rule.PathTemplate.Split('/').Any(s => s.Contains('{'))) { /* 토큰 없음 = flat, 허용 */ }

        try { GlobPattern.Validate(rule.FilePattern); }
        catch (ArgumentException ex) { errors.Add($"filePattern invalid: {ex.Message}"); }

        switch (def.GenerationType)
        {
            case GenerationType.Hourly:
                if (!HasToken(rule.PathTemplate, "{HH}") && !HasAnyToken(rule.PathTemplate)) { }
                break;
        }

        ValidateMetadata(def, errors);
        return errors;
    }

    private static bool HasToken(string s, string t) => s.Contains(t, StringComparison.Ordinal);
    private static bool HasAnyToken(string s) => s.Contains('{');

    private static void ValidateMetadata(EquipmentLogDefinition def, List<string> errors)
    {
        var meta = def.MetadataRule;
        if (string.IsNullOrWhiteSpace(meta.Pattern)) { errors.Add("metadata pattern empty"); return; }

        if (meta.Mode == MetadataMode.Template)
        {
            foreach (var token in ExtractTokens(meta.Pattern))
                if (!DateTokens.Contains(token) && token != "{subtype}" && !token.StartsWith("{attribute."))
                    errors.Add($"unknown metadata token: {token}");
            var hasDate = HasToken(meta.Pattern, "{yyyy}") && HasToken(meta.Pattern, "{MM}") && HasToken(meta.Pattern, "{dd}");
            var hasHour = HasToken(meta.Pattern, "{HH}");
            if (def.GenerationType == GenerationType.Hourly && !(hasDate && hasHour))
                errors.Add("Hourly metadata pattern must contain yyyy/MM/dd/HH tokens");
            if (def.GenerationType == GenerationType.Daily && (hasHour || HasToken(meta.Pattern, "{mm}")))
                errors.Add("Daily metadata pattern must not contain time tokens");
            if (def.GenerationType == GenerationType.Daily && !hasDate)
                errors.Add("Daily metadata pattern must contain yyyy/MM/dd tokens");
        }
        else
        {
            try
            {
                var regex = new Regex(meta.Pattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
                if (def.GenerationType != GenerationType.Continuous &&
                    meta.Mappings.All(m => m.Target != "timestamp"))
                    errors.Add($"{def.GenerationType} requires a timestamp mapping");
                foreach (var m in meta.Mappings)
                {
                    if (m.Target is "timestamp")
                    {
                        if (string.IsNullOrEmpty(m.Format)) errors.Add("timestamp mapping requires format");
                        else
                        {
                            if (def.GenerationType == GenerationType.Daily &&
                                (m.Format!.Contains('H') || m.Format.Contains('m') || m.Format.Contains('s')))
                                errors.Add("Daily timestamp format must be date-only");
                            if (!regex.GetGroupNames().Contains(m.Group))
                                errors.Add($"mapping group not in regex: {m.Group}");
                        }
                    }
                    else if (m.Target is not "subtype" && !m.Target.StartsWith("attribute."))
                        errors.Add($"unsupported mapping target: {m.Target}");
                }
            }
            catch (ArgumentException ex) { errors.Add($"metadata regex invalid: {ex.Message}"); }
        }
    }

    private static IEnumerable<string> ExtractTokens(string pattern)
        => Regex.Matches(pattern, @"\{[^}]+\}").Select(m => m.Value);
}
```

(`ExplicitCapture`는 이름 없는 그룹 캡처 방지 — mapping은 named group만 허용. PathTemplate의 token 종류 검증은 PathTokens 화이트리스트로 동일 방식.)

`ConfigurationDefinitionValidator`: current/history pathTemplate 안전성(원칙 동일), glob 유효성, history pathTemplate과 markerPathTemplate에 `{yyyy}{MM}{dd}` 필수 포함, `{HH}` 금지.

`ReferenceDataSnapshotBuilder.Build(raw)` 절차:
1. row 파싱(GenerationType/Cardinality/MetadataMode parse 실패 → 오류 목록 추가), MetadataMappingsJson 역직렬화.
2. EquipmentIds 집합 구성(중복 → 오류).
3. Servers 중복 ServerId → 오류.
4. 각 log/config 정의: equipmentId/serverId 존재, feature validator 오류 없음, `(equipmentId, logType)`/`(equipmentId, configurationType)` 중복 → 오류.
5. 오류 존재 시 `ReferenceDataValidationException(errors)` throw. 전부 통과 시 `ReferenceDataSnapshot` 생성(사전 인덱스 구축, catalog 요약은 이름 오름차순 정렬).
6. FTP 접근 없음(전 과정 순수 메모리 — 문서 요구사항).

`ReferenceDataValidationException(IReadOnlyList<string> Errors) : Exception`.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~SnapshotBuilderTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(reference-data): validated reference data snapshot for log and configuration definitions"
```

---

### Task 7: MSSQL SP reader + single-flight 기준정보 cache

**Files:**
- Infrastructure csproj: `dotnet add package Microsoft.Data.SqlClient`
- Create: `db/mvp-schema.sql`, `db/mvp-stored-procedure.sql`
- Create: `src/FileGateway.Infrastructure/ReferenceData/IReferenceDataSource.cs`, `SpReferenceDataSource.cs`, `IReferenceDataView.cs`, `ReferenceDataCache.cs`
- Test: `tests/FileGateway.IntegrationTests/DatabaseFixture.cs`, `tests/FileGateway.IntegrationTests/ReferenceData/SpReaderTests.cs`, `tests/FileGateway.UnitTests/ReferenceData/ReferenceDataCacheTests.cs`
- IntegrationTests csproj: `dotnet add package Testcontainers.MsSql`

**Interfaces:**
- Consumes: Task 6 (`ReferenceDataRaw`, `ReferenceDataSnapshotBuilder`), Core `FileGatewayException`
- Produces:
  - `interface IReferenceDataSource { Task<ReferenceDataRaw> ReadAsync(CancellationToken ct); }`
  - `class SpReferenceDataSource(string connectionString) : IReferenceDataSource` — SP `FileGateway_GetReferenceData` 호출, 4 result set을 `ReferenceDataRaw`로 매핑
  - `interface IReferenceDataView { Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct); }`
  - `class ReferenceDataCache(IReferenceDataSource source, TimeSpan ttl) : IReferenceDataView` + `ReferenceDataSnapshot? CurrentSnapshot { get; }`, `bool HasUsableSnapshot { get; }`, `DateTimeOffset? LastGoodRefreshAt { get; }`, `DateTime? LastRefreshFailedAt`, `string? LastRefreshError`. **`GetSnapshotAsync` 의미**: usable cache + TTL 유효 → 즉시 반환. usable cache + TTL 만료 → 단일 background refresh(single-flight)를 촉발하고 **현재 stale snapshot을 즉시 반환**(요청이 DB refresh를 기다리지 않는다). cache 없음 → 공유 최초 로딩을 await(실패 시 `ReferenceDataUnavailable`, 동시 요청은 동일 결과 공유).

- [ ] **Step 1: DB 계약 스크립트**

```sql
-- db/mvp-schema.sql (테스트/개발용 계약 구현)
CREATE TABLE dbo.FgEquipment (EquipmentId nvarchar(64) NOT NULL PRIMARY KEY);
CREATE TABLE dbo.FgServer (ServerId nvarchar(64) NOT NULL PRIMARY KEY,
    Host nvarchar(255) NOT NULL, RootPath nvarchar(512) NOT NULL);
CREATE TABLE dbo.FgLogDefinition (
    EquipmentId nvarchar(64) NOT NULL, LogType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL, GenerationType nvarchar(16) NOT NULL,
    PathTemplate nvarchar(512) NOT NULL, FilePattern nvarchar(256) NOT NULL,
    Cardinality nvarchar(16) NOT NULL, MetadataMode nvarchar(16) NOT NULL,
    MetadataPattern nvarchar(1024) NOT NULL, MetadataMappings nvarchar(max) NOT NULL DEFAULT '[]',
    CONSTRAINT PK_FgLogDefinition PRIMARY KEY (EquipmentId, LogType));
CREATE TABLE dbo.FgConfigurationDefinition (
    EquipmentId nvarchar(64) NOT NULL, ConfigurationType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL,
    CurrentPathTemplate nvarchar(512) NOT NULL, CurrentFilePattern nvarchar(256) NOT NULL,
    HistoryPathTemplate nvarchar(512) NOT NULL, HistoryFilePattern nvarchar(256) NOT NULL,
    HistoryMarkerPathTemplate nvarchar(512) NOT NULL,
    CONSTRAINT PK_FgConfigurationDefinition PRIMARY KEY (EquipmentId, ConfigurationType));
```

```sql
-- db/mvp-stored-procedure.sql
CREATE OR ALTER PROCEDURE dbo.FileGateway_GetReferenceData AS
BEGIN
    SET NOCOUNT ON;
    SELECT EquipmentId FROM dbo.FgEquipment;
    SELECT ServerId, Host, RootPath FROM dbo.FgServer;
    SELECT EquipmentId, LogType, ServerId, GenerationType, PathTemplate, FilePattern,
           Cardinality, MetadataMode, MetadataPattern, MetadataMappings
    FROM dbo.FgLogDefinition;
    SELECT EquipmentId, ConfigurationType, ServerId, CurrentPathTemplate, CurrentFilePattern,
           HistoryPathTemplate, HistoryFilePattern, HistoryMarkerPathTemplate
    FROM dbo.FgConfigurationDefinition;
END
```

- [ ] **Step 2: DatabaseFixture (Testcontainers)**

```csharp
// tests/FileGateway.IntegrationTests/DatabaseFixture.cs
using Testcontainers.MsSql;

namespace FileGateway.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04").Build(); // latest 금지: 실행 시점 최신 CU 태그로 고정(확정 결정 17)

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync(await File.ReadAllTextAsync("db/mvp-schema.sql"));
        await ExecuteAsync(await File.ReadAllTextAsync("db/mvp-stored-procedure.sql"));
    }

    public Task ExecuteAsync(string sql) // GO 없는 배치 실행 헬퍼
    {
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

- [ ] **Step 3: 실패 테스트**

```csharp
// tests/FileGateway.IntegrationTests/ReferenceData/SpReaderTests.cs
namespace FileGateway.IntegrationTests.ReferenceData;

public class SpReaderTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Reader_maps_four_result_sets()
    {
        await db.ExecuteAsync(@"INSERT dbo.FgEquipment VALUES('EQ-001');
            INSERT dbo.FgServer VALUES('SRV1','ftp1.internal','ftproot');
            INSERT dbo.FgLogDefinition VALUES('EQ-001','EventLog','SRV1','Hourly',
              'Logs/{yyyy}/{MM}/{dd}/{HH}','*.zip','Multiple','Template',
              'Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip','[]');
            INSERT dbo.FgConfigurationDefinition VALUES('EQ-001','PM','SRV1',
              'PM/current','PM_*.cfg','PM/history/{yyyy}/{MM}/{dd}','PM_*.cfg',
              'PM/history/{yyyy}/{MM}/{dd}/_DONE');");

        var raw = await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None);

        Assert.Contains("EQ-001", raw.EquipmentIds);
        var log = Assert.Single(raw.LogDefinitions);
        Assert.Equal("Hourly", log.GenerationType);
        var cfg = Assert.Single(raw.ConfigurationDefinitions);
        Assert.Equal("PM/history/{yyyy}/{MM}/{dd}/_DONE", cfg.HistoryMarkerPathTemplate);
    }

    [Fact]
    public async Task Snapshot_from_sp_passes_validation_without_ftp()
        => Assert.NotNull(ReferenceDataSnapshotBuilder.Build(
               await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None)));
}
```

```csharp
// tests/FileGateway.UnitTests/ReferenceData/ReferenceDataCacheTests.cs
namespace FileGateway.UnitTests.ReferenceData;

public class ReferenceDataCacheTests
{
    private sealed class FakeSource(ReferenceDataRaw first) : IReferenceDataSource
    {
        public int Calls; public Func<Task<ReferenceDataRaw>>? Next;
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
        { Calls++; return Next != null ? Next() : Task.FromResult(first); }
    }

    private static ReferenceDataRaw Raw(string equipment = "EQ-001") => new(
        [equipment], [new RawServer("SRV1", "h", "ftproot")], [], []);

    [Fact]
    public async Task First_load_failure_without_cache_throws_ReferenceDataUnavailable()
    {
        var src = new FakeSource(Raw()) { Next = () => throw new SqlExceptionSim() };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMinutes(15));
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() => cache.GetSnapshotAsync(CancellationToken.None));
        Assert.Equal("ReferenceDataUnavailable", ex.Code);
    }

    [Fact]
    public async Task Concurrent_first_load_shares_single_refresh()
    {
        var src = new FakeSource(Raw()) { Next = async () => { await Task.Delay(200); return Raw(); } };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMinutes(15));
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetSnapshotAsync(CancellationToken.None)));
        Assert.Equal(1, src.Calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task Expired_cache_returns_stale_immediately_and_refreshes_in_background()
    {
        var v1 = Raw("EQ-A");
        var v2 = Raw("EQ-B");
        var src = new FakeSource(v1)
        {
            Next = async () => { await Task.Delay(300); return v2; }
        };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100); // TTL 경과, refresh는 300ms 지연
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await cache.GetSnapshotAsync(CancellationToken.None);
        sw.Stop();

        Assert.Same(first, second);                     // DB refresh를 기다리지 않고 stale 즉시 반환
        Assert.True(sw.ElapsedMilliseconds < 200);

        await Task.Delay(400);                           // background refresh 완료 대기
        Assert.Contains("EQ-B", cache.CurrentSnapshot!.EquipmentIds); // atomic 교체 확인
    }

    [Fact]
    public async Task Refresh_failure_keeps_last_known_good_stale_snapshot()
    {
        var good = Raw();
        var src = new FakeSource(good) { Next = () => { src.Next = () => throw new SqlExceptionSim(); return Task.FromResult(good); } };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100); // TTL 경과
        var second = await cache.GetSnapshotAsync(CancellationToken.None);

        Assert.Same(first, second);          // atomic 교체 없이 동일 인스턴스
        Assert.True(cache.HasUsableSnapshot);
        Assert.NotNull(cache.LastRefreshError);
    }

    [Fact]
    public async Task Validation_failure_rejects_new_snapshot_entirely()
    {
        var good = Raw();
        var broken = new ReferenceDataRaw(["EQ-1", "EQ-1"], [], [], []); // 장비 중복
        var src = new FakeSource(good) { Next = () => Task.FromResult(broken) };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100);
        Assert.Same(first, await cache.GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Successful_validation_swaps_atomically()
    {
        var v1 = Raw("EQ-A"); var v2 = Raw("EQ-B");
        var src = new FakeSource(v1) { Next = () => Task.FromResult(v2) };
        var cache = new ReferenceDataCache(src, TimeSpan.FromMilliseconds(50));
        var first = await cache.GetSnapshotAsync(CancellationToken.None);

        await Task.Delay(100);
        var second = await cache.GetSnapshotAsync(CancellationToken.None);
        Assert.NotSame(first, second);
        Assert.Contains("EQ-B", second.EquipmentIds);
    }

    private sealed class SqlExceptionSim : Exception;
}
```

- [ ] **Step 4: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~ReferenceDataCacheTests"` 및 통합 테스트 / Expected: FAIL

- [ ] **Step 5: 구현**

```csharp
// src/FileGateway.Infrastructure/ReferenceData/SpReferenceDataSource.cs
using Microsoft.Data.SqlClient;

namespace FileGateway.Infrastructure.ReferenceData;

public sealed class SpReferenceDataSource(string connectionString) : IReferenceDataSource
{
    public async Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.FileGateway_GetReferenceData";
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var equipments = new List<string>();
        while (await reader.ReadAsync(ct)) equipments.Add(reader.GetString(0));
        await reader.NextResultAsync(ct);

        var servers = new List<RawServer>();
        while (await reader.ReadAsync(ct))
            servers.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct);

        var logs = new List<RawLogDefinition>();
        while (await reader.ReadAsync(ct))
            logs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9)));
        await reader.NextResultAsync(ct);

        var configs = new List<RawConfigurationDefinition>();
        while (await reader.ReadAsync(ct))
            configs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));

        return new(equipments, servers, logs, configs);
    }
}

// src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs
namespace FileGateway.Infrastructure.ReferenceData;

public sealed class ReferenceDataCache(IReferenceDataSource source, TimeSpan ttl) : IReferenceDataView
{
    private readonly object _gate = new();
    private Task<ReferenceDataSnapshot>? _inFlight;
    private DateTimeOffset _loadedAt;

    public ReferenceDataSnapshot? CurrentSnapshot { get; private set; }
    public bool HasUsableSnapshot => CurrentSnapshot is not null;
    public DateTimeOffset? LastGoodRefreshAt => HasUsableSnapshot ? _loadedAt : null;
    public DateTime? LastRefreshFailedAt { get; private set; }
    public string? LastRefreshError { get; private set; }

    public Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (CurrentSnapshot is not null)
            {
                if (DateTimeOffset.UtcNow - _loadedAt < ttl)
                    return Task.FromResult(CurrentSnapshot);
                // TTL 만료: single-flight background refresh 촉발 후 stale 즉시 반환.
                // 요청이 DB를 기다리지 않는다(확정 결정 14, 리뷰 P1 반영).
                _ = TriggerRefresh();
                return Task.FromResult(CurrentSnapshot);
            }
            // 최초 로딩: 동시 요청이 동일 공유 로딩을 await
            return _inFlight ??= InitialLoadAsync();
        }
    }

    private async Task TriggerRefresh()
    {
        lock (_gate)
        {
            if (_inFlight is not null) return; // single-flight
            _inFlight = LoadAsync();
        }
        try { await _inFlight!; }
        catch { /* 실패는 아래 LoadAsync에서 상태로 기록됨. stale cache 유지. */ }
    }

    private async Task<ReferenceDataSnapshot> InitialLoadAsync()
    {
        try { return await LoadAsync(); }
        finally
        {
            lock (_gate) { _inFlight = null; } // 이후 요청이 재시도 가능
        }
    }

    private async Task<ReferenceDataSnapshot> LoadAsync()
    {
        try
        {
            var raw = await source.ReadAsync(CancellationToken.None);
            var snapshot = ReferenceDataSnapshotBuilder.Build(raw); // 검증 실패 → 새 스냅샷 전체 거부
            lock (_gate)
            {
                CurrentSnapshot = snapshot;   // atomic 참조 교체
                _loadedAt = DateTimeOffset.UtcNow;
                LastRefreshError = null; LastRefreshFailedAt = null;
                _inFlight = null;
                return snapshot;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                LastRefreshFailedAt = DateTime.UtcNow;
                LastRefreshError = ex.Message;
                _inFlight = null;
                if (CurrentSnapshot is not null) return CurrentSnapshot; // stale 유지
                throw new FileGatewayException("ReferenceDataUnavailable", "reference data unavailable");
            }
        }
    }
}
```

동작 요약: TTL 만료 후 요청은 즉시 stale를 받고 refresh는 1회만 실행된다. 최초 로딩 실패는 `ReferenceDataUnavailable` 예외로 모든 동시 대기자에게 전파되고(공유 Task), 다음 요청이 재시도한다. background refresh의 `ct`는 호출자 요청 취소와 분리한다(`CancellationToken.None`) — 취소된 요청이 cache 상태를 좌우하지 않게 한다.

- [ ] **Step 6: 통과 확인 후 커밋**

Run: `dotnet test` / Expected: 전체 PASS

```bash
git add -A && git commit -m "feat(reference-data): mssql sp reader and single-flight atomic cache"
```

---

### Task 8: Logs — pathTemplate 슬롯 확장

**Files:**
- Create: `src/FileGateway.Logs/Internal/PathTemplate.cs`, `SlotExpansion.cs`, `src/FileGateway.Core/Time/SiteTime.cs`, `src/FileGateway.Core/Time/EffectiveRange.cs`
- Test: `tests/FileGateway.UnitTests/Logs/SlotExpansionTests.cs`

**Interfaces:**
- Consumes: Task 6 모델
- Produces:
  - `static class SiteTime { static readonly TimeZoneInfo Local("Asia/Seoul"); static DateTimeOffset ToSiteLocal(DateTimeOffset t); static DateTimeOffset SiteLocalMidnight(DateTimeOffset t); static DateTimeOffset Parse(string iso); }` — offset 없는 입력은 Site local 해석
  - `record EffectiveRange(DateTimeOffset From, DateTimeOffset To)` — `FileGateway.Core.Time` 네임스페이스(Logs/Configurations 공용)
  - `static class PathTemplate { static string Expand(string template, DateTimeOffset siteLocalSlot); static void ValidateTokens(string template); }` — 허용 토큰 `{yyyy}{MM}{dd}{HH}`
  - `static class SlotExpansion { static IEnumerable<DateTimeOffset> EnumerateSlots(GenerationType type, EffectiveRange range); }` — Hourly: Site local 시간 슬롯(정시 절사), Daily: Site local 자정 날짜 슬롯, Continuous: 단일 더미 슬롯

- [x] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Logs;

public class SlotExpansionTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 22, 10, 30, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset To = new(2026, 8, 22, 13, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Hourly_enumerates_hour_slots_in_half_open_range()
    {
        var slots = SlotExpansion.EnumerateSlots(GenerationType.Hourly, new EffectiveRange(From, To)).ToList();
        // 10:30 → 10:00 슬롯부터, 13:00 제외
        Assert.Equal(
        [
            new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 11, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(9)),
        ], slots);
    }

    [Fact]
    public void Daily_enumerates_midnight_slots()
    {
        var from = new DateTimeOffset(2026, 8, 22, 23, 0, 0, TimeSpan.FromHours(9));
        var to = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(9));
        var slots = SlotExpansion.EnumerateSlots(GenerationType.Daily, new EffectiveRange(from, to)).ToList();
        Assert.Equal(2, slots.Count);
        Assert.All(slots, s => Assert.Equal(0, s.Hour));
    }

    [Fact]
    public void Continuous_returns_single_slot()
        => Assert.Single(SlotExpansion.EnumerateSlots(GenerationType.Continuous, new EffectiveRange(From, To)));

    [Fact]
    public void Expand_substitutes_site_local_components()
    {
        var slot = new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal("Logs/2026/08/22/18", PathTemplate.Expand("Logs/{yyyy}/{MM}/{dd}/{HH}", slot));
        Assert.Equal("Logs/flat", PathTemplate.Expand("Logs/flat", slot));
    }

    [Fact]
    public void Expand_rejects_unknown_token_via_validate()
        => Assert.Throws<ArgumentException>(() => PathTemplate.ValidateTokens("Logs/{yy}"));

    [Fact]
    public void SiteTime_parses_offsetless_as_seoul()
    {
        var parsed = SiteTime.Parse("2026-08-22T18:00:00");
        Assert.Equal(TimeSpan.FromHours(9), parsed.Offset);
    }

    [Theory]
    [InlineData("2026-08-22T18:00:00Z", 0)]          // Z → UTC
    [InlineData("2026-08-22T18:00:00+09:00", 9)]     // 명시적 offset 유지
    [InlineData("2026-08-22T18:00:00-05:00", -5)]
    [InlineData("2026-08-22T18:00:00", 9)]           // offset 없음 → Seoul (머신 timezone 무관)
    public void SiteTime_parse_respects_offset_contract(string iso, int expectedOffsetHours)
        => Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), SiteTime.Parse(iso).Offset);

    [Fact]
    public void SiteTime_midnight_uses_seoul_offset()
        => Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
                        SiteTime.SiteLocalMidnight(new DateTimeOffset(2026, 8, 22, 15, 0, 0, TimeSpan.UtcNow.Offset)));
}
```

- [x] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~SlotExpansionTests"` / Expected: FAIL

- [x] **Step 3: 구현**

```csharp
// src/FileGateway.Core/Time/SiteTime.cs
namespace FileGateway.Core.Time;

public static class SiteTime
{
    public static readonly TimeZoneInfo Local = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

    public static DateTimeOffset ToSiteLocal(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Local);

    public static DateTimeOffset SiteLocalMidnight(DateTimeOffset t)
    {
        var l = ToSiteLocal(t);
        return new DateTimeOffset(l.Date, l.Offset);
    }

    /// <summary>API 시각 파싱. offset 포함 값은 그 offset을 그대로, offset 없는 값은 Asia/Seoul(+09:00)로 해석한다.
    /// 실행 머신 local timezone은 절대 사용하지 않는다(확정 결정 9).</summary>
    public static DateTimeOffset Parse(string iso)
    {
        if (!HasOffset(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var naive))
            return new DateTimeOffset(naive, Local.GetUtcOffset(naive)); // offset 없음 → Asia/Seoul 확정
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
            return withOffset;
        throw new ArgumentException($"unparseable timestamp: {iso}");
    }

    private static bool HasOffset(string iso)
        => iso.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
        || iso.Contains('+')
        || iso.LastIndexOf('-') >= 10; // ISO 날짜부 "YYYY-MM-DD"는 10자: 이후의 '-'는 시간대 offset
    }
}

// src/FileGateway.Logs/Internal/PathTemplate.cs
namespace FileGateway.Logs.Internal;

public static class PathTemplate
{
    private static readonly string[] AllowedTokens = ["{yyyy}", "{MM}", "{dd}", "{HH}"];

    public static void ValidateTokens(string template)
    {
        foreach (var m in Regex.Matches(template, @"\{[^}]+\}").Select(m => m.Value))
            if (!AllowedTokens.Contains(m))
                throw new ArgumentException($"unsupported path token: {m}");
    }

    public static string Expand(string template, DateTimeOffset siteLocalSlot)
        => template.Replace("{yyyy}", siteLocalSlot.ToString("yyyy", CultureInfo.InvariantCulture))
                   .Replace("{MM}", siteLocalSlot.ToString("MM", CultureInfo.InvariantCulture))
                   .Replace("{dd}", siteLocalSlot.ToString("dd", CultureInfo.InvariantCulture))
                   .Replace("{HH}", siteLocalSlot.ToString("HH", CultureInfo.InvariantCulture));
}

// src/FileGateway.Logs/Internal/SlotExpansion.cs
namespace FileGateway.Logs.Internal;

public static class SlotExpansion
{
    public static IEnumerable<DateTimeOffset> EnumerateSlots(GenerationType type, EffectiveRange range)
    {
        switch (type)
        {
            case GenerationType.Hourly:
                for (var s = FloorToSiteHour(range.From); s < range.To; s = s.AddHours(1))
                    yield return s;
                break;
            case GenerationType.Daily:
                for (var d = SiteTime.SiteLocalMidnight(range.From); d < range.To; d = d.AddDays(1))
                    yield return d;
                break;
            default:
                yield return DateTimeOffset.MinValue; // Continuous: 토큰 미사용 단일 슬롯
                break;
        }
    }

    private static DateTimeOffset FloorToSiteHour(DateTimeOffset t)
    {
        var l = SiteTime.ToSiteLocal(t);
        var floored = new DateTimeOffset(l.Year, l.Month, l.Day, l.Hour, 0, 0, l.Offset);
        return floored;
    }
}
```

`SiteTime.Parse`는 offset 미포함 문자열을 Site local(+09:00)로 확정하는 구현으로 마무리한다(테스트 참조).

- [x] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~SlotExpansionTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(logs): site-local slot expansion and path template tokens"
```

---

### Task 9: Logs — MetadataRule 파서

**Files:**
- Create: `src/FileGateway.Logs/Internal/MetadataRuleParser.cs`
- Test: `tests/FileGateway.UnitTests/Logs/MetadataRuleParserTests.cs`

**Interfaces:**
- Consumes: Task 6 `LogMetadataRule`, Task 8 `SiteTime`
- Produces: `record ParsedMetadata(DateTimeOffset? Timestamp, string? Subtype, IReadOnlyDictionary<string, string> Attributes)`; `static class MetadataRuleParser { static ParsedMetadata? Parse(LogMetadataRule rule, GenerationType generation, string relativePath); }` — 해석 불가 시 `null`(호출자가 `FileDefinitionConflict`로 승격)

- [x] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Logs;

public class MetadataRuleParserTests
{
    private const string Path244 = "Logs/2026/08/22/18/Event_A.zip";

    [Fact]
    public void Template_extracts_timestamp_subtype_and_attributes()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly, Path244)!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Template_extracts_attributes()
    {
        var rule = new LogMetadataRule(MetadataMode.Template,
            "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{attribute.lot}_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly,
            "Logs/2026/08/22/18/Event_L07_A.zip")!;
        Assert.Equal("L07", meta.Attributes["lot"]);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Daily_timestamp_is_site_local_midnight()
    {
        var rule = new LogMetadataRule(MetadataMode.Template, "Logs/{yyyy}/{MM}/{dd}/Event_{subtype}.zip", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Daily, "Logs/2026/08/22/Event_A.zip")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }

    [Fact]
    public void Continuous_without_date_tokens_yields_null_timestamp()
    {
        var rule = new LogMetadataRule(MetadataMode.Template, "Trace/Trace_{subtype}.log", []);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous, "Trace/Trace_PM.log")!;
        Assert.Null(meta.Timestamp);
        Assert.Equal("PM", meta.Subtype);
    }

    [Fact]
    public void Template_mismatch_returns_null()
        => Assert.Null(MetadataRuleParser.Parse(
            new LogMetadataRule(MetadataMode.Template, "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", []),
            GenerationType.Hourly, "Logs/2026/08/22/18/Other_A.zip"));

    [Fact]
    public void Regex_named_groups_with_mappings()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex,
            @"^Logs/(?<ts>\d{8}_\d{2})/Event_(?<s>[A-Z0-9]+)\.zip$",
            [new MetadataMapping("ts", "timestamp", "yyyyMMdd_HH"), new MetadataMapping("s", "subtype", null)]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Hourly, "Logs/20260822_18/Event_A.zip")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
        Assert.Equal("A", meta.Subtype);
    }

    [Fact]
    public void Regex_missing_required_group_returns_null()
        => Assert.Null(MetadataRuleParser.Parse(
            new LogMetadataRule(MetadataMode.Regex, @"^Logs/(?<s>x)/y\.zip$",
                [new MetadataMapping("ts", "timestamp", "yyyyMMdd")]),
            GenerationType.Hourly, "Logs/x/y.zip"));

    [Fact]
    public void Regex_attribute_mapping()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex, @"^L/(?<v>\d+)/a\.log$",
            [new MetadataMapping("v", "attribute.version", null)]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Continuous, "L/3/a.log")!;
        Assert.Equal("3", meta.Attributes["version"]);
    }

    [Fact]
    public void Regex_datetime_with_offsetless_value_interpreted_as_seoul()
    {
        var rule = new LogMetadataRule(MetadataMode.Regex, @"^D/(?<ts>\d{8})/x\.log$",
            [new MetadataMapping("ts", "timestamp", "yyyyMMdd")]);
        var meta = MetadataRuleParser.Parse(rule, GenerationType.Daily, "D/20260822/x.log")!;
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)), meta.Timestamp);
    }
}
```

- [x] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~MetadataRuleParserTests"` / Expected: FAIL

- [x] **Step 3: 구현**

```csharp
// src/FileGateway.Logs/Internal/MetadataRuleParser.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace FileGateway.Logs.Internal;

public sealed record ParsedMetadata(
    DateTimeOffset? Timestamp, string? Subtype, IReadOnlyDictionary<string, string> Attributes)
{
    public static readonly ParsedMetadata Empty = new(null, null,
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
}

public static partial class MetadataRuleParser
{
    public static ParsedMetadata? Parse(LogMetadataRule rule, GenerationType generation, string relativePath)
        => rule.Mode == MetadataMode.Template ? ParseTemplate(rule.Pattern, generation, relativePath)
                                              : ParseRegex(rule, generation, relativePath);

    private static ParsedMetadata? ParseTemplate(string pattern, GenerationType generation, string path)
    {
        var regex = TemplateToRegex(pattern);
        var m = regex.Match(path);
        if (!m.Success) return null;

        var attrs = new Dictionary<string, string>();
        string? subtype = null;
        DateTimeOffset? date = null, hour = null;

        foreach (var name in regex.GetGroupNames().Where(g => regex.GroupNumberFromName(g) >= 0 && !int.TryParse(g, out _)))
        {
            var v = m.Groups[name].Value;
            if (v.Length == 0) return null;
            switch (name)
            {
                case "fg_ts_yyyy": date = DateTimeOffset.TryParseExact(
                        $"{v}-{G(m, "fg_ts_MM")}-{G(m, "fg_ts_dd")}", "yyyy-M-d",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? new DateTimeOffset(d.Date, TimeSpan.FromHours(9)) : null; break;
                case "fg_ts_MM": case "fg_ts_dd": break;
                case "fg_ts_HH": hour = DateTimeOffset.TryParseExact(v, "HH", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var h) ? h : null; break;
                case "fg_subtype": subtype = v; break;
                default:
                    if (name.StartsWith("fg_attr_", StringComparison.Ordinal))
                        attrs[name["fg_attr_".Length..]] = v;
                    break;
            }
        }

        if (date is not null)
        {
            var localDate = TimeZoneInfo.ConvertTime(date.Value, SiteTime.Local);
            var midnight = new DateTimeOffset(localDate.Date, TimeSpan.FromHours(9));
            if (generation == GenerationType.Daily) return new(midnight, subtype, attrs);
            if (generation == GenerationType.Hourly)
            {
                if (hour is null) return null;
                return new(midnight.AddHours(hour.Value.Hour), subtype, attrs);
            }
            // Continuous: 추출된 시각이 있으면 사용(없어도 null 허용은 아래에서 처리)
            return new(midnight.AddHours(hour?.Hour ?? 0), subtype, attrs);
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily) return null; // 날짜 토큰 미추출
        return new ParsedMetadata(null, subtype, attrs); // Continuous, timestamp 없음
    }

    private static string G(Match m, string name) => m.Groups[name].Value;

    [GeneratedRegex(@"\{(?<tok>yyyy|MM|dd|HH|mm|subtype|attr\.[^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private static Regex TemplateToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var last = 0;
        foreach (Match tm in TokenRegex().Matches(pattern))
        {
            sb.Append(Regex.Escape(pattern[last..tm.Index]));
            var tok = tm.Groups["tok"].Value;
            sb.Append(tok switch
            {
                "yyyy" => "(?<fg_ts_yyyy>\\d{4})",
                "MM" => "(?<fg_ts_MM>\\d{2})",
                "dd" => "(?<fg_ts_dd>\\d{2})",
                "HH" => "(?<fg_ts_HH>\\d{2})",
                "mm" => "(?<fg_ts_mm>\\d{2})",
                "subtype" => "(?<fg_subtype>[^/]+?)",
                var a when a.StartsWith("attribute.", StringComparison.Ordinal)
                    => $"(?<fg_attr_{a["attribute.".Length..]}>[^/]+?)",
                _ => throw new ArgumentException($"unknown token {tok}")
            });
            last = tm.Index + tm.Length;
        }
        sb.Append(Regex.Escape(pattern[last..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    }

    private static ParsedMetadata? ParseRegex(LogMetadataRule rule, GenerationType generation, string path)
    {
        var regex = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        var m = regex.Match(path);
        if (!m.Success) return null;

        DateTimeOffset? timestamp = null; string? subtype = null;
        var attrs = new Dictionary<string, string>();
        foreach (var map in rule.Mappings)
        {
            if (!m.Groups[map.Group].Success) return null;
            var value = m.Groups[map.Group].Value;
            if (map.Target == "timestamp")
            {
                if (!DateTimeOffset.TryParseExact(value, map.Format!, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) return null;
                var unspecified = new DateTimeOffset(dt.DateTime, TimeSpan.FromHours(9)); // Site local 해석
                timestamp = generation == GenerationType.Daily
                    ? new DateTimeOffset(unspecified.Date, TimeSpan.FromHours(9))
                    : unspecified;
            }
            else if (map.Target == "subtype") subtype = value;
            else if (map.Target.StartsWith("attribute.", StringComparison.Ordinal))
                attrs[map.Target["attribute.".Length..]] = value;
            else return null;
        }
        if (generation is GenerationType.Hourly or GenerationType.Daily && timestamp is null) return null;
        return new(timestamp, subtype, attrs);
    }
}
```

- [x] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~MetadataRuleParserTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(logs): metadata rule parser for template and regex modes"
```

---

### Task 10: Logs — Resolver(탐색/필터/정렬/cardinality)

**Files:**
- Create: `src/FileGateway.Logs/LogListQuery.cs`, `src/FileGateway.Logs/LogFileDescriptor.cs`, `src/FileGateway.Logs/Internal/LogResolver.cs`
- Test: `tests/FileGateway.UnitTests/Logs/LogResolverTests.cs`, `tests/FileGateway.UnitTests/Logs/EffectiveRangeTests.cs`

**Interfaces:**
- Consumes: Task 3 `IFileAccess`/`FakeFileAccess`, Task 6 정의, Task 8 `SlotExpansion`/`PathTemplate`/`EffectiveRange`, Task 9 `MetadataRuleParser`
- Produces:
  - `record LogListQuery(string EquipmentId, string LogType, DateTimeOffset? From, DateTimeOffset? To, string? Subtype, IReadOnlyDictionary<string,string> Attributes, int? Limit, string? ContinuationToken)`
  - `static class EffectiveRangePlanner { static EffectiveRange Normalize(LogListQuery q, GenerationType type, TimeSpan maxRange); }` — 규칙 위반 시 `FileGatewayException("InvalidRequest")`
  - `record LogFileDescriptor(string FileId, string EquipmentId, string LogType, string? Subtype, DateTimeOffset? Timestamp, string FileName, long Size, bool IsContinuous, IReadOnlyDictionary<string,string> Attributes)`
  - `record ResolvedLogFile(ParsedMetadata Metadata, RemoteFileEntry Entry, string RelativePath)`
  - `class LogResolver(IFileAccess fileAccess)` — `Task<IReadOnlyList<ResolvedLogFile>> ResolveAsync(ResolvedLogDefinition def, EffectiveRange range, CancellationToken ct)`: 슬롯→디렉터리(중복 제거)→목록(부재 0개, I/O 오류 전체 실패)→glob→metadata→case-insensitive 중복 검사→cardinality 슬롯 검사→시간 필터; 정렬은 반환 전 적용(Hourly/Daily `timestamp DESC`,`fileName ASC` ci / Continuous `fileName ASC` ci)

- [x] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Logs;

public class EffectiveRangeTests
{
    private static readonly TimeSpan Max = TimeSpan.FromDays(31);
    private static readonly DateTimeOffset F = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9));

    private static LogListQuery Q(DateTimeOffset? from = null, DateTimeOffset? to = null)
        => new("EQ-001", "EventLog", from, to, null, [], null, null);

    [Fact] public void No_bounds_defaults_to_last_24h()
        => Assert.Equal(TimeSpan.FromHours(24),
             EffectiveRangePlanner.Normalize(Q(), GenerationType.Hourly, Max).To
               - EffectiveRangePlanner.Normalize(Q(), GenerationType.Hourly, Max).From);

    [Fact] public void From_only_extends_two_days()
        => Assert.Equal(TimeSpan.FromDays(2),
             EffectiveRangePlanner.Normalize(Q(F), GenerationType.Hourly, Max).To - F);

    [Fact] public void To_only_is_invalid()
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), GenerationType.Hourly, Max)).Code);

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void From_gte_to_invalid(int deltaHours)
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddHours(deltaHours)), GenerationType.Hourly, Max)).Code);

    [Fact] public void Over_max_range_invalid()
        => Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F, F.AddDays(32)), GenerationType.Hourly, Max)).Code);

    [Fact] public void Continuous_rejects_any_time_bound()
    {
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(F), GenerationType.Continuous, Max)).Code);
        Assert.Equal("InvalidRequest", Assert.Throws<FileGatewayException>(
            () => EffectiveRangePlanner.Normalize(Q(to: F), GenerationType.Continuous, Max)).Code);
    }
}

public class LogResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");

    private static ResolvedLogDefinition Def(GenerationType gen = GenerationType.Hourly,
        string pathTemplate = "Logs/{yyyy}/{MM}/{dd}/{HH}", string metaPattern = "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip",
        Cardinality card = Cardinality.Multiple)
        => new(new EquipmentLogDefinition("EQ-001", "EventLog", "SRV1", gen,
               new LogDiscoveryRule(pathTemplate, "Event_*.zip", card),
               new LogMetadataRule(MetadataMode.Template, metaPattern, [])), Srv);

    private static EffectiveRange Range(int y, int m, int d, int h)
        => new(new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.FromHours(9)),
               new DateTimeOffset(y, m, d, h + 1, 0, 0, TimeSpan.FromHours(9)));

    [Fact]
    public async Task Hourly_flat_directory_multiple_hours_one_listing()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/flat/2026/08/22/18/Event_A.zip"u8.ToString() is var _ ? "Logs/all/Event_A.zip" : "", "x"u8.ToArray());
        // flat 구조: pathTemplate 토큰 없음 = 한 디렉터리, 파일명에 시간
        ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/Event_A.zip", "x"u8.ToArray());
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip");
        // 파일명이 패턴과 불일치 → 아래 별도 케이스. 여기서는 메타 패턴에 맞춘 파일 사용
        var ftp2 = new FakeFileAccess();
        ftp2.AddFile("Logs/all/2026082218_Event_A.zip", "x"u8.ToArray());
        var files = await new LogResolver(ftp2).ResolveAsync(def, Range(2026, 8, 22, 18), CancellationToken.None);
        var f = Assert.Single(files);
        Assert.Equal("2026082218_Event_A.zip", f.Entry.Name);
        Assert.NotNull(f.Metadata.Timestamp);
    }

    [Fact]
    public async Task Missing_directory_yields_empty_not_error()
    {
        var files = await new LogResolver(new FakeFileAccess())
            .ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None);
        Assert.Empty(files);
    }

    [Fact]
    public async Task Ftp_io_failure_fails_whole_request()
    {
        var ftp = new FakeFileAccess(); // throw 유도: 서버 장애 흉내
        var files = await Assert.ThrowsAsync<FileAccessException>(() =>
            new LogResolver(new ThrowingFileAccess()).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Empty(files);
    }

    [Fact]
    public async Task Metadata_parse_failure_is_definition_conflict()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/20260822_Event_A.zip", "x"u8.ToArray()); // 패턴 불일치
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_names_are_conflict()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/event_a.ZIP", "y"u8.ToArray()); // glob 통과, ci 동일
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Same_basename_in_different_hour_directories_is_not_conflict()
    {
        // 논리 identity는 timestamp + fileName: 서로 다른 시간대 디렉터리의 같은 basename은 별개 파일이다
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/17/Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/Event_A.zip", "y"u8.ToArray());
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(9)));
        var files = await new LogResolver(ftp).ResolveAsync(Def(), range, CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.Equal(["Logs/2026/08/22/18/Event_A.zip", "Logs/2026/08/22/17/Event_A.zip"],
            files.Select(f => f.RelativePath)); // timestamp DESC
    }

    [Fact]
    public async Task Single_cardinality_with_two_files_in_slot_is_conflict()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/2026/08/22/18/Event_A.zip", "x"u8.ToArray());
        ftp.AddFile("Logs/2026/08/22/18/Event_B.zip", "y"u8.ToArray());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            new LogResolver(ftp).ResolveAsync(Def(card: Cardinality.Single), Range(2026, 8, 22, 18), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Hourly_filters_by_parsed_timestamp_and_sorts_desc()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event_A.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event_B.zip", "2"u8.ToArray());
        ftp.AddFile("Logs/all/2026082220_Event_C.zip", "3"u8.ToArray()); // 범위 밖(18시~19시)
        var def = Def(GenerationType.Hourly, "Logs/all", "Logs/all/{yyyy}{MM}{dd}{HH}_Event_{subtype}.zip");
        var range = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(9)));
        var files = await new LogResolver(ftp).ResolveAsync(def, range, CancellationToken.None);
        Assert.Single(files); // 18시 파일만 (flat 디렉터리 전체 조회 후 timestamp 필터)
        // 두 슬롯 조회 정렬 확인
        var range2 = new EffectiveRange(
            new DateTimeOffset(2026, 8, 22, 17, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 20, 0, 0, TimeSpan.FromHours(9)));
        var files2 = await new LogResolver(ftp).ResolveAsync(def, range2, CancellationToken.None);
        Assert.Equal(["2026082220_Event_C.zip", "2026082218_Event_A.zip", "2026082217_Event_B.zip"],
            files2.Select(f => f.Entry.Name));
    }

    [Fact]
    public async Task Continuous_lists_current_slot_sorted_by_name()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Trace/cur/Trace_B.log", "1"u8.ToArray());
        ftp.AddFile("Trace/cur/Trace_A.log", "2"u8.ToArray());
        var def = Def(GenerationType.Continuous, "Trace/cur", "Trace/cur/Trace_{subtype}.log");
        var files = await new LogResolver(ftp).ResolveAsync(def, new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), CancellationToken.None);
        Assert.Equal(["Trace_A.log", "Trace_B.log"], files.Select(f => f.Entry.Name));
    }

    private sealed class ThrowingFileAccess : IFileAccess
    {
        public Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection s, string d, CancellationToken ct)
            => Task.FromException<RemoteDirectoryListing>(new FileAccessException(FileAccessError.ConnectionFailed, "down"));
        public Task<long> StatFileAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteOpenRead> OpenReadAsync(FileServerConnection s, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
```

- [x] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~LogResolverTests|FullyQualifiedName~EffectiveRangeTests"` / Expected: FAIL

- [x] **Step 3: 구현**

`EffectiveRangePlanner.Normalize`:

```csharp
namespace FileGateway.Logs;

public static class EffectiveRangePlanner
{
    public static EffectiveRange Normalize(LogListQuery q, GenerationType type, TimeSpan maxRange)
    {
        if (type == GenerationType.Continuous)
        {
            if (q.From is not null || q.To is not null)
                throw new FileGatewayException("InvalidRequest", "Continuous log does not accept from/to");
            return new(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        }
        if (q.To is not null && q.From is null)
            throw new FileGatewayException("InvalidRequest", "to without from is not supported");
        if (q.To is not null && q.From >= q.To)
            throw new FileGatewayException("InvalidRequest", "from must be before to");

        var from = q.From ?? DateTimeOffset.UtcNow.AddHours(-24);
        var to = q.To ?? (q.From is not null ? q.From.Value.AddDays(2) : DateTimeOffset.UtcNow);
        if (to - from > maxRange)
            throw new FileGatewayException("InvalidRequest", $"query range exceeds limit ({maxRange})");
        return new(from, to);
    }
}
```

`LogResolver`:

```csharp
// src/FileGateway.Logs/Internal/LogResolver.cs
namespace FileGateway.Logs.Internal;

public sealed class LogResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedLogFile>> ResolveAsync(
        ResolvedLogDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var d = def.Definition;
        var rule = d.DiscoveryRule;
        var glob = new GlobPattern(rule.FilePattern);

        // 슬롯 → 디렉터리(중복 제거: 여러 슬롯이 같은 물리 디렉터리일 수 있다)
        var directories = SlotExpansion.EnumerateSlots(d.GenerationType, range)
            .Select(slot => PathTemplate.Expand(rule.PathTemplate, slot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<ResolvedLogFile>();

        foreach (var dir in directories)
        {
            var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct); // I/O 오류는 그대로 상향(전체 실패)
            if (!listing.Exists) continue;                                      // 디렉터리 부재 = 정상 0개

            // 중복 판정은 "동일 탐색 결과(동일 디렉터리)" 기준이다(문서: 동일 탐색 범위의 case-insensitive 동일 파일명).
            // 서로 다른 디렉터리의 같은 basename은 논리 timestamp가 다른 별개 파일이므로 충돌이 아니다.
            var seenNames = new HashSet<string>(FileNameComparison.Comparer);
            foreach (var entry in listing.Files)
            {
                if (!glob.Matches(entry.Name)) continue;
                if (!seenNames.Add(entry.Name))
                    throw new FileGatewayException("FileDefinitionConflict",
                        $"case-insensitive duplicate file name in {dir}: {entry.Name}");
                var relativePath = dir + "/" + entry.Name;
                var meta = MetadataRuleParser.Parse(d.MetadataRule, d.GenerationType, relativePath);
                if (meta is null)
                    throw new FileGatewayException("FileDefinitionConflict",
                        $"file matched pattern but metadata unparseable: {relativePath}");
                files.Add(new(meta, entry, relativePath));
            }
        }

        CheckCardinality(d, rule.Cardinality, files);

        if (d.GenerationType != GenerationType.Continuous)
            files = files.Where(f => f.Metadata.Timestamp >= range.From && f.Metadata.Timestamp < range.To).ToList();

        return d.GenerationType == GenerationType.Continuous
            ? files.OrderBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList()
            : files.OrderByDescending(f => f.Metadata.Timestamp!.Value)
                   .ThenBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }

    private static void CheckCardinality(EquipmentLogDefinition d, Cardinality card, List<ResolvedLogFile> files)
    {
        if (card != Cardinality.Single) return;
        var slotKeys = d.GenerationType switch
        {
            GenerationType.Hourly => new Func<ResolvedLogFile, object>(
                f => new DateTimeOffset(SiteTime.ToSiteLocal(f.Metadata.Timestamp!.Value).DateTime.Date,
                                        TimeSpan.FromHours(9)).AddHours(SiteTime.ToSiteLocal(f.Metadata.Timestamp!.Value).Hour)),
            GenerationType.Daily => f => SiteTime.SiteLocalMidnight(f.Metadata.Timestamp!.Value),
            _ => _ => 0
        };
        foreach (var g in files.GroupBy(slotKeys))
            if (g.Count() > 1)
                throw new FileGatewayException("FileDefinitionConflict",
                    $"cardinality=Single but slot has {g.Count()} files");
    }
}
```

- [x] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~LogResolverTests|FullyQualifiedName~EffectiveRangeTests"` / Expected: PASS (첫 테스트의 정리 안 된 중복 라인은 구현 확정 후 삭제 — 테스트 코드 최종본에는 `ftp2` 케이스만 남긴다)

```bash
git add -A && git commit -m "feat(logs): resolver with slot dedup, filters, cardinality and sorting"
```

---

### Task 11: Logs — pagination cursor + fileId 발급/재해석 + 서비스 조립

**Files:**
- Create: `src/FileGateway.Core/Queries/PagedResult.cs`(`PagedResult<T>`, `MatchCount`, `SingleFileMatch` — Logs/Configurations/Api 공용), `src/FileGateway.Logs/Internal/LogCursor.cs`, `src/FileGateway.Logs/ILogQueryService.cs`, `LogQueryService.cs`
- Test: `tests/FileGateway.UnitTests/Logs/LogCursorTests.cs`, `tests/FileGateway.UnitTests/Logs/LogQueryServiceTests.cs`

**Interfaces:**
- Consumes: Task 4 `ITokenCodec`, Task 7 `IReferenceDataView`, Task 10 전부
- Produces:
  - `record PagedResult<T>(IReadOnlyList<T> Items, string? ContinuationToken)` — `FileGateway.Core.Queries`
  - `enum MatchCount { Zero, One, Many }`, `record SingleFileMatch(LocatedFile? File, MatchCount Count)` — `FileGateway.Core.Queries`
  - `interface ILogQueryService { Task<PagedResult<LogFileDescriptor>> ListAsync(LogListQuery query, CancellationToken ct); Task<SingleFileMatch> ResolveSingleAsync(LogListQuery query, CancellationToken ct); Task<LocatedFile> LocateByFileIdAsync(TokenPayload fileId, CancellationToken ct); }`
  - `class LogQueryService(IReferenceDataView referenceData, IFileAccess fileAccess, ITokenCodec tokens, TimeProvider clock)` — `TimeProvider.System` DI 등록. cursor TTL은 `TokenOptions.ContinuationTtl`, fileId TTL은 `TokenOptions.FileIdTtl`를 생성자에서 받도록 파라미터 추가(`TimeSpan fileTtl, TimeSpan pageTtl`)
  - cursor claims(raw 조회조건 바인딩): `equipmentId`,`logType`,`from`,`to`(원본, 없으면 빈 문자열),`subtype`,`attrs`(정규화 `k=v&...` 정렬),`lastTs`(없으면 빈),`lastName`. limit 미포함
  - fileId claims: `equipmentId`,`logType`,`ts`(round-trip "O", 없으면 빈),`fileName`

- [x] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Logs;

public class LogCursorTests
{
    private static readonly ITokenCodec Codec = new DataProtectionTokenCodec(
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());
    private static readonly ITokenCodec FakeCodec = new FakeCodec();

    private sealed class FakeCodec : ITokenCodec // 빠른 직렬화(검증 대상: 바인딩/비교 논리)
    {
        public string Protect(TokenPayload p) => System.Text.Json.JsonSerializer.Serialize(p);
        public TokenDecodeResult Unprotect(string t)
        {
            try { var p = System.Text.Json.JsonSerializer.Deserialize<TokenPayload>(t)!; return new(TokenValidity.Valid, p); }
            catch { return new(TokenValidity.Invalid, null); }
        }
    }

    internal static string Issue(LogListQuery q, DateTimeOffset? lastTs, string? lastName) =>
        LogCursor.Encode(Codec, q, lastTs, lastName, TimeSpan.FromMinutes(30));

    [Fact]
    public void Binding_same_raw_conditions_matches()
    {
        var q = new LogListQuery("EQ-1", "Event", new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9)), "A",
            new Dictionary<string, string> { ["lot"] = "7" }, 50, null);
        var token = Issue(q, null, null);
        LogCursor.AssertBinding(Codec, token, q); // 예외 없음
    }

    [Fact]
    public void Binding_different_conditions_throws_InvalidRequest()
    {
        var q = new LogListQuery("EQ-1", "Event", null, null, null, [], 50, null);
        var token = Issue(q, null, null);
        var changed = q with { Subtype = "B" };
        Assert.Equal("InvalidRequest",
            Assert.Throws<FileGatewayException>(() => LogCursor.AssertBinding(Codec, token, changed)).Code);
    }

    [Fact]
    public void Limit_change_is_allowed()
    {
        var q = new LogListQuery("EQ-1", "Event", null, null, null, [], 50, null);
        var token = Issue(q, null, null);
        LogCursor.AssertBinding(Codec, token, q with { Limit = 200 });
    }
}
```

```csharp
namespace FileGateway.UnitTests.Logs;

public class LogQueryServiceTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static readonly ITokenCodec Codec = new DataProtectionTokenCodec(
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>());

    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"], [new RawServer("SRV1", "ftp1", "ftproot")],
        [new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
            "Logs/all", "Event_*.zip", "Multiple", "Template",
            "Logs/all/{yyyy}{MM}{dd}{HH}_Event.zip", "[]")], []));

    private sealed class FixedView(ReferenceDataSnapshot snap) : IReferenceDataView
        => Task.FromResult(snap) as Task<ReferenceDataSnapshot>; // => public Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct) { get; }

    private static LogQueryService Service(FakeFileAccess ftp, IReferenceDataView? view = null)
        => new(view ?? new FixedViewWrapper(Snapshot()), ftp, Codec,
               TimeProvider.System, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30));

    [Fact]
    public async Task List_issues_fileIds_and_paginates()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        var svc = Service(ftp);

        var q = new LogListQuery("EQ-001", "EventLog",
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9)), null, [], 1, null);
        var p1 = await svc.ListAsync(q, CancellationToken.None);
        var first = Assert.Single(p1.Items);
        Assert.NotNull(p1.ContinuationToken);
        Assert.Equal("2026082218_Event.zip", first.FileName);
        Assert.False(string.IsNullOrEmpty(first.FileId));

        var p2 = await svc.ListAsync(q with { ContinuationToken = p1.ContinuationToken }, CancellationToken.None);
        var second = Assert.Single(p2.Items);
        Assert.Null(p2.ContinuationToken);
        Assert.Equal("2026082217_Event.zip", second.FileName);
    }

    [Fact]
    public async Task Empty_result_is_items_empty_token_null()
    {
        var svc = Service(new FakeFileAccess());
        var q = new LogListQuery("EQ-001", "EventLog", null, null, null, [], null, null);
        var page = await svc.ListAsync(q, CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task Unknown_equipment_or_type_is_definition_not_found()
    {
        var svc = Service(new FakeFileAccess());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            svc.ListAsync(new LogListQuery("EQ-X", "EventLog", null, null, null, [], null, null), CancellationToken.None));
        Assert.Equal("LogDefinitionNotFound", ex.Code);
    }

    [Fact]
    public async Task Subtype_and_attribute_filters_apply_case_sensitively()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        // Template 메타 패턴에 subtype 없음 → 필터 지정 시 0건 (필터는 case-sensitive 정확 일치)
        var svc = Service(ftp);
        var q = new LogListQuery("EQ-001", "EventLog", null, null, "Nope", [], null, null);
        Assert.Empty((await svc.ListAsync(q, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task ResolveSingle_maps_zero_one_many()
    {
        var ftp = new FakeFileAccess();
        var svc = Service(ftp);
        var q = new LogListQuery("EQ-001", "EventLog", null, null, null, [], null, null);
        Assert.Equal(MatchCount.Zero, (await svc.ResolveSingleAsync(q, CancellationToken.None)).Count);

        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        Assert.Equal(MatchCount.One, (await svc.ResolveSingleAsync(q, CancellationToken.None)).Count);

        ftp.AddFile("Logs/all/2026082217_Event.zip", "2"u8.ToArray());
        Assert.Equal(MatchCount.Many, (await svc.ResolveSingleAsync(q, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task FileId_round_trip_locates_file()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "12345"u8.ToArray());
        var svc = Service(ftp);
        var page = await svc.ListAsync(new LogListQuery("EQ-001", "EventLog", null, null, null, [], null, null), CancellationToken.None);
        var fileId = Assert.Single(page.Items).FileId;

        var decoded = Codec.Unprotect(fileId);
        var located = await svc.LocateByFileIdAsync(decoded.Payload!, CancellationToken.None);
        Assert.Equal("Logs/all/2026082218_Event.zip", located.RelativePath);
        Assert.Equal(5, located.Size);
    }

    [Fact]
    public async Task FileId_for_missing_file_is_FileNotFound()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("Logs/all/2026082218_Event.zip", "1"u8.ToArray());
        var svc = Service(ftp);
        var page = await svc.ListAsync(new LogListQuery("EQ-001", "EventLog", null, null, null, [], null, null), CancellationToken.None);
        var fileId = Assert.Single(page.Items).FileId;
        ftp.RemoveFile("Logs/all/2026082218_Event.zip"); // 이후 삭제

        var ex = await Assert.ThrowsAsync<FileGatewayException>(() =>
            svc.LocateByFileIdAsync(Codec.Unprotect(fileId).Payload!, CancellationToken.None));
        Assert.Equal("FileNotFound", ex.Code);
    }
}
```

(`FixedView`/`FixedViewWrapper`는 `IReferenceDataView` 구현 3줄 헬퍼로 `TestUtils`에 둔다: `Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(_snap);`)

- [x] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~LogCursorTests|FullyQualifiedName~LogQueryServiceTests"` / Expected: FAIL

- [x] **Step 3: 구현**

```csharp
// src/FileGateway.Logs/Internal/LogCursor.cs
namespace FileGateway.Logs.Internal;

public static class LogCursor
{
    public static string Canonical(LogListQuery q)
        => string.Join("|",
            q.EquipmentId, q.LogType,
            q.From?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            q.To?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            q.Subtype ?? "",
            string.Join("&", q.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}")));

    public static string Encode(ITokenCodec codec, LogListQuery q,
        DateTimeOffset? lastTimestamp, string? lastFileName, TimeSpan ttl)
    {
        var claims = new Dictionary<string, string>
        {
            ["bind"] = Canonical(q),
            ["lastTs"] = lastTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["lastName"] = lastFileName ?? "",
        };
        return codec.Protect(new TokenPayload(LogTokenKinds.ContinuationPurpose, claims,
            DateTimeOffset.UtcNow, ttl));
    }

    public static (DateTimeOffset? LastTs, string? LastName) Decode(ITokenCodec codec, string token)
    {
        var result = codec.Unprotect(token);
        if (result.Validity != TokenValidity.Valid)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var p = result.Payload!;
        if (p.Purpose != LogTokenKinds.ContinuationPurpose)
            throw new FileGatewayException("InvalidRequest", "invalid continuation token");
        var ts = p.Claims.TryGetValue("lastTs", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : null;
        return (ts, p.Claims.TryGetValue("lastName", out var n) && n.Length > 0 ? n : null);
    }

    public static void AssertBinding(ITokenCodec codec, string token, LogListQuery current)
    {
        var result = codec.Unprotect(token);
        if (result.Validity != TokenValidity.Valid ||
            result.Payload!.Purpose != LogTokenKinds.ContinuationPurpose ||
            result.Payload.Claims.GetValueOrDefault("bind") != Canonical(current))
            throw new FileGatewayException("InvalidRequest", "continuation token does not match query conditions");
    }
}

// src/FileGateway.Logs/LogQueryService.cs (핵심 흐름)
namespace FileGateway.Logs;

public sealed class LogQueryService(
    IReferenceDataView referenceData, IFileAccess fileAccess, ITokenCodec tokens,
    TimeProvider clock, TimeSpan fileTtl, TimeSpan pageTtl) : ILogQueryService
{
    public async Task<PagedResult<LogFileDescriptor>> ListAsync(LogListQuery query, CancellationToken ct)
    {
        var snapshot = await referenceData.GetSnapshotAsync(ct);
        var def = snapshot.FindLog(query.EquipmentId, query.LogType)
                  ?? throw new FileGatewayException("LogDefinitionNotFound", "no log definition");
        var range = EffectiveRangePlanner.Normalize(query, def.Definition.GenerationType, /* Logs.MaxQueryRange 주입 */ MaxRange);

        var files = await new LogResolver(fileAccess).ResolveAsync(def, range, ct);
        files = ApplyFilters(files, query); // subtype/attr: case-sensitive 정확 일치

        var cursor = (DateTimeOffset? LastTs, string? LastName)? = null;
        if (query.ContinuationToken is not null)
        {
            LogCursor.AssertBinding(tokens, query.ContinuationToken, query);
            cursor = LogCursor.Decode(tokens, query.ContinuationToken);
            files = SkipUntilAfter(files, cursor.Value.LastTs, cursor.Value.LastName);
        }

        var limit = query.Limit ?? LimitDefault; // 생성자 파라미터로 주입(PagingOptions)
        var page = files.Take(limit).ToList();
        string? next = null;
        if (files.Count > limit && page.Count > 0)
        {
            var last = page[^1];
            var lastTs = def.Definition.GenerationType == GenerationType.Continuous ? null : last.Metadata.Timestamp;
            next = LogCursor.Encode(tokens, query, lastTs, last.Entry.Name, pageTtl);
        }
        return new(page.Select(f => ToDescriptor(def, f, query)).ToList(), next);
    }

    private LogFileDescriptor ToDescriptor(ResolvedLogDefinition def, ResolvedLogFile f, LogListQuery q)
    {
        var claims = new Dictionary<string, string>
        {
            ["equipmentId"] = q.EquipmentId, ["logType"] = q.LogType,
            ["ts"] = f.Metadata.Timestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            ["fileName"] = f.Entry.Name,
        };
        var fileId = tokens.Protect(new TokenPayload(LogTokenKinds.FileIdPurpose, claims,
            clock.GetUtcNow(), fileTtl));
        return new(fileId, q.EquipmentId, q.LogType, f.Metadata.Subtype, f.Metadata.Timestamp,
            f.Entry.Name, f.Entry.Size, def.Definition.GenerationType == GenerationType.Continuous,
            f.Metadata.Attributes);
    }

    public async Task<SingleFileMatch> ResolveSingleAsync(LogListQuery query, CancellationToken ct)
    {
        // ListAsync와 동일 Resolver 실행 후 개수 판정 (fileId 발급 없이 위치만)
        ... // 구현: 정규화→Resolve→필터→0/1/N 분기, 1개면 LocatedFile(server, relativePath, name, size) stat 확정
    }

    public async Task<LocatedFile> LocateByFileIdAsync(TokenPayload payload, CancellationToken ct)
    {
        if (payload.Purpose != LogTokenKinds.FileIdPurpose)
            throw new FileGatewayException("InvalidFileId", "not a log file id");
        var equipmentId = payload.Claims["equipmentId"]; var logType = payload.Claims["logType"];
        var ts = payload.Claims.TryGetValue("ts", out var t) && t.Length > 0
            ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;
        var fileName = payload.Claims["fileName"];

        var snapshot = await referenceData.GetSnapshotAsync(ct);
        var def = snapshot.FindLog(equipmentId, logType)
                  ?? throw new FileGatewayException("LogDefinitionNotFound", "definition removed");
        var range = ts is null
            ? new EffectiveRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)   // Continuous: 현재 슬롯 전체
            : new EffectiveRange(ts.Value, ts.Value.AddSeconds(1));                  // 해당 슬롯만 재탐색
        var files = await new LogResolver(fileAccess).ResolveAsync(def, range, ct);
        var match = files.SingleOrDefault(f => FileNameComparison.Same(f.Entry.Name, fileName))
            ?? throw new FileGatewayException("FileNotFound", "logical file no longer exists");
        return new(def.Server, match.RelativePath, match.Entry.Name, match.Entry.Size);
    }
}
```

생성자 파라미터에 `TimeSpan maxQueryRange`, `int limitDefault`를 추가하고 `ResolveSingleAsync`는 `ListAsync`와 동일 경로(토큰 없이)를 재사용한다. `SkipUntilAfter`: Hourly/Daily는 `timestamp < cursor.LastTs` 스킵 + 동일 timestamp에서 `fileName` ci 정렬 순 커서 이후부터, Continuous는 `fileName` 커서 이후. `InvalidFileId` 판정은 Api가 `TokenValidity`로 선행 처리하되 purpose 불일치 방어도 유지한다.

- [x] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~LogCursorTests|FullyQualifiedName~LogQueryServiceTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(logs): stateless pagination cursor, file id issuance and reinterpretation"
```

---

### Task 12: Configurations — Current resolver + fileId

**Files:**
- Create: `src/FileGateway.Configurations/Internal/CurrentResolver.cs`, `src/FileGateway.Configurations/ConfigurationItems.cs`, `IConfigurationQueryService.cs` 인터페이스 일부
- Test: `tests/FileGateway.UnitTests/Configurations/CurrentResolverTests.cs`

**Interfaces:**
- Consumes: Task 3/4/6/7/9 성과
- Produces:
  - `record ConfigurationItem(string FileId, string EquipmentId, string ConfigurationType, string FileName, long Size)`
  - `interface IConfigurationQueryService` 전체 시그니처(Task 13에서 완성): `Task<IReadOnlyList<ConfigurationItem>> GetCurrentAsync(string equipmentId, string configurationType, CancellationToken ct); Task<SingleFileMatch> ResolveCurrentSingleAsync(string equipmentId, string configurationType, CancellationToken ct); Task<PagedResult<ConfigurationHistoryItem>> GetHistoryAsync(ConfigurationHistoryQuery q, CancellationToken ct); Task<LocatedFile> LocateByFileIdAsync(TokenPayload payload, CancellationToken ct);`
  - `PagedResult<T>`/`MatchCount`/`SingleFileMatch`은 Task 11이 `FileGateway.Core.Queries`에 생성한 공용 타입을 그대로 사용한다.
  - `CurrentResolver(IFileAccess)` — `Task<IReadOnlyList<ResolvedConfigFile>> ResolveAsync(ResolvedConfigurationDefinition def, CancellationToken ct)` + `record ResolvedConfigFile(string RelativePath, RemoteFileEntry Entry)`: 디렉터리 부재 → 빈 목록, glob → ci 중복 검사 → ci `fileName ASC` 정렬. pagination 없음.
  - fileId claims(Current): `equipmentId`,`configurationType`,`fileName`

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.UnitTests.Configurations;

public class CurrentResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static ResolvedConfigurationDefinition Def()
        => new(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
               new CurrentRule("PM/current", "PM_*.cfg"),
               new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")),
               Srv);

    [Fact]
    public async Task Returns_all_current_files_sorted_case_insensitive()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/pm2.cfg", "22"u8.ToArray());
        ftp.AddFile("PM/current/PM1.cfg", "11"u8.ToArray());
        var files = await new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None);
        Assert.Equal(["PM1.cfg", "pm2.cfg"], files.Select(f => f.Entry.Name));
    }

    [Fact]
    public async Task Missing_directory_returns_empty_list()
        => Assert.Empty(await new CurrentResolver(new FakeFileAccess()).ResolveAsync(Def(), CancellationToken.None));

    [Fact]
    public async Task Case_insensitive_duplicate_is_conflict()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/PM1.cfg", "1"u8.ToArray());
        ftp.AddFile("PM/current/pm1.CFG", "2"u8.ToArray());
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }

    [Fact]
    public async Task Non_matching_files_excluded()
    {
        var ftp = new FakeFileAccess();
        ftp.AddFile("PM/current/PM1.cfg", "1"u8.ToArray());
        ftp.AddFile("PM/current/readme.txt", "2"u8.ToArray());
        var files = await new CurrentResolver(ftp).ResolveAsync(Def(), CancellationToken.None);
        Assert.Single(files);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~CurrentResolverTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Configurations/Internal/CurrentResolver.cs
namespace FileGateway.Configurations.Internal;

public sealed record ResolvedConfigFile(string RelativePath, RemoteFileEntry Entry);

public sealed class CurrentResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedConfigFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, CancellationToken ct)
    {
        var rule = def.Definition.CurrentRule;
        var glob = new GlobPattern(rule.FilePattern);
        var listing = await fileAccess.ListFilesAsync(def.Server, rule.PathTemplate, ct);
        if (!listing.Exists) return [];

        var files = new List<ResolvedConfigFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer);
        foreach (var e in listing.Files)
        {
            if (!glob.Matches(e.Name)) continue;
            if (!seen.Add(e.Name))
                throw new FileGatewayException("FileDefinitionConflict", $"duplicate file name: {e.Name}");
            files.Add(new(rule.PathTemplate + "/" + e.Name, e));
        }
        return files.OrderBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }
}
```

`ConfigurationQueryService.GetCurrentAsync`: snapshot 조회(`ConfigurationDefinitionNotFound`), resolver 실행, 각 파일 fileId 발급(`ConfigurationTokenKinds.FileIdCurrentPurpose`, claims `equipmentId`/`configurationType`/`fileName`, TTL `TokenOptions.FileIdTtl`) → `ConfigurationItem` 배열. `ResolveCurrentSingleAsync`: 동일 경로 0/1/N 분기(Current 정의상 N이 정상 케이스일 수 있음 — `MultipleFilesMatched`는 직접 다운로드 계약상 오류). `LocateByFileIdAsync`의 Current 분기: purpose 확인 → 정의 조회 → resolver → ci 이름 매치 → 부재 시 `FileNotFound`.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~CurrentResolverTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(configurations): current set resolution and file ids"
```

---

### Task 13: Configurations — History resolver + pagination + snapshot fileId

**Files:**
- Create: `src/FileGateway.Configurations/Internal/HistoryResolver.cs`, `HistoryCursor.cs`, `src/FileGateway.Configurations/ConfigurationHistoryQuery.cs`, `ConfigurationItems.cs`(HistoryItem 추가), `ConfigurationQueryService.cs`
- Test: `tests/FileGateway.UnitTests/Configurations/HistoryResolverTests.cs`, `tests/FileGateway.UnitTests/Configurations/ConfigurationQueryServiceTests.cs`

**Interfaces:**
- Consumes: Task 12 산출 + Task 7/4
- Produces:
  - `record ConfigurationHistoryQuery(string EquipmentId, string ConfigurationType, DateTimeOffset From, DateTimeOffset To, int? Limit, string? ContinuationToken)`
  - `record ConfigurationHistoryItem(string FileId, string EquipmentId, string ConfigurationType, DateTimeOffset SnapshotTimestamp, string FileName, long Size)`
  - `HistoryResolver(IFileAccess)` — `Task<IReadOnlyList<ResolvedSnapshotFile>> ResolveAsync(ResolvedConfigurationDefinition def, EffectiveRange range, CancellationToken ct)` + `record ResolvedSnapshotFile(DateTimeOffset SnapshotTimestamp, string RelativePath, RemoteFileEntry Entry)`: 날짜 슬롯별 marker 존재 확인(FileExists) → marker 없으면 해당 날짜 skip → 디렉터리 부재 skip → glob → ci 중복 검사 → `snapshotTimestamp DESC`/`fileName ASC` 정렬. I/O 오류는 전체 실패.
  - History cursor claims: `equipmentId`,`configurationType`,`from`,`to`(raw "O"),`lastTs`,`lastName`. 바인딩 규칙은 Log와 동일(조건 변경 → `InvalidRequest`, limit 제외).
  - Snapshot fileId claims: `equipmentId`,`configurationType`,`ts`("O"),`fileName`. 재해석 시 marker 재확인.

- [ ] **Step 1: 실키 테스트**

```csharp
namespace FileGateway.UnitTests.Configurations;

public class HistoryResolverTests
{
    private static readonly FileServerConnection Srv = new("SRV1", "ftp1", "ftproot");
    private static ResolvedConfigurationDefinition Def()
        => new(new EquipmentConfigurationDefinition("EQ-001", "PM", "SRV1",
               new CurrentRule("PM/current", "PM_*.cfg"),
               new HistoryRule("PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}/_DONE")),
               Srv);

    private static EffectiveRange Range(int day)
        => new(new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.FromHours(9)),
               new DateTimeOffset(2026, 8, day + 1, 0, 0, 0, TimeSpan.FromHours(9)));

    private static void Seed(FakeFileAccess ftp, int day, params string[] files)
    {
        var d = $"PM/history/2026/08/{day:00}";
        foreach (var f in files) ftp.AddFile($"{d}/{f}", new byte[f.Length]);
        if (files.Length > 0) ftp.AddFile($"{d}/_DONE", []); // marker: 존재만
    }

    [Fact]
    public async Task Only_marked_snapshot_sets_are_included()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg", "PM2.cfg"); // marker 있음
        ftp.AddFile("PM/history/2026/08/21/PM1.cfg", "x"u8.ToArray()); // marker 없음
        var files = await new HistoryResolver(ftp).ResolveAsync(Def(), Range(21) with { }, CancellationToken.None);
        // 21~22 범위로 확장해 확인
        files = await new HistoryResolver(ftp).ResolveAsync(
            new(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Equal(22, f.SnapshotTimestamp.Day));
    }

    [Fact]
    public async Task Marker_file_itself_is_not_a_result()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(Def(), Range(22), CancellationToken.None);
        Assert.Equal("PM1.cfg", Assert.Single(files).Entry.Name); // _DONE은 glob 불일치로 제외
    }

    [Fact]
    public async Task Sorts_by_snapshot_desc_then_name()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 21, "PM1.cfg");
        Seed(ftp, 22, "pm2.cfg", "PM1.cfg");
        var files = await new HistoryResolver(ftp).ResolveAsync(
            new(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        Assert.Equal(
        [
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.FromHours(9)),
        ], files.Select(f => f.SnapshotTimestamp));
        Assert.Equal("PM1.cfg", files[0].Entry.Name); // 동일 시각 내 이름 오름차순
    }

    [Fact]
    public async Task Non_midnight_from_excludes_that_days_snapshot()
    {
        var ftp = new FakeFileAccess();
        Seed(ftp, 22, "PM1.cfg");
        Seed(ftp, 23, "PM1.cfg");
        // from=22T12:00 → 22일 자정 snapshot은 [from,to) 밖, 23일 자정만 포함
        var files = await new HistoryResolver(ftp).ResolveAsync(
            new(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(9))), CancellationToken.None);
        var ts = Assert.Single(files).SnapshotTimestamp;
        Assert.Equal(23, ts.Day);
    }

    [Fact]
    public async Task Duplicate_case_insensitive_name_is_conflict()
    {
        var ftp = new FakeFileAccess();
        var d = "PM/history/2026/08/22";
        ftp.AddFile($"{d}/PM1.cfg", "1"u8.ToArray());
        ftp.AddFile($"{d}/pm1.CFG", "2"u8.ToArray());
        ftp.AddFile($"{d}/_DONE", []);
        var ex = await Assert.ThrowsAsync<FileGatewayException>(
            () => new HistoryResolver(ftp).ResolveAsync(Def(), Range(22), CancellationToken.None));
        Assert.Equal("FileDefinitionConflict", ex.Code);
    }
}
```

`ConfigurationQueryServiceTests`(핵심):

```csharp
namespace FileGateway.UnitTests.Configurations;

public class ConfigurationQueryServiceTests
{
    // fixture: snapshot(EQ-001/PM), FakeFileAccess, codec, service — Task 11과 동일 패턴(자체 헬퍼)

    [Fact] public async Task History_requires_range_via_service_validation() { /* from>=to → InvalidRequest; to-from > HistoryMaxQueryRange → InvalidRequest */ }
    [Fact] public async Task History_paginates_and_allows_limit_change() { /* 3 files, limit 2 → page2 limit 1 */ }
    [Fact] public async Task History_continuation_condition_change_is_invalid() { /* equipmentId 변경 시 InvalidRequest */ }
    [Fact] public async Task Snapshot_fileId_locates_and_rechecks_marker() { /* 발급→정상 locate→marker 제거→FileNotFound(파일 잔존에도) */ }
    [Fact] public async Task Current_fileId_points_to_current_content() { /* 파일 내용 교체 후 동일 fileId로 size 갱신 확인 */ }
    [Fact] public async Task Unknown_purpose_is_invalid_file_id() { /* log fileId 토큰을 넘기면 InvalidFileId */ }
}
```

(각 `[Fact]` 본문은 위 주석 시나리오 그대로 실제 assert 코드로 작성한다 — Task 11의 서비스 테스트 패턴을 그대로 따른다.)

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~HistoryResolverTests|FullyQualifiedName~ConfigurationQueryServiceTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Configurations/Internal/HistoryResolver.cs
namespace FileGateway.Configurations.Internal;

public sealed record ResolvedSnapshotFile(DateTimeOffset SnapshotTimestamp, string RelativePath, RemoteFileEntry Entry);

public sealed class HistoryResolver(IFileAccess fileAccess)
{
    public async Task<IReadOnlyList<ResolvedSnapshotFile>> ResolveAsync(
        ResolvedConfigurationDefinition def, EffectiveRange range, CancellationToken ct)
    {
        var rule = def.Definition.HistoryRule;
        var glob = new GlobPattern(rule.FilePattern);
        var files = new List<ResolvedSnapshotFile>();
        var seen = new HashSet<string>(FileNameComparison.Comparer);

        // [from, to)의 정확한 하한: from이 자정이 아니면 그날 자정 snapshot은 from 이전이므로 제외한다.
        var start = SiteTime.SiteLocalMidnight(range.From);
        if (start < range.From) start = start.AddDays(1);
        for (var date = start; date < range.To; date = date.AddDays(1))
        {
            var markerRel = ExpandDate(rule.MarkerPathTemplate, date);
            if (!await fileAccess.FileExistsAsync(def.Server, markerRel, ct)) continue; // 미완료 Set 제외

            var dir = ExpandDate(rule.PathTemplate, date);
            var listing = await fileAccess.ListFilesAsync(def.Server, dir, ct);
            if (!listing.Exists) continue;

            foreach (var e in listing.Files)
            {
                if (!glob.Matches(e.Name)) continue;
                if (!seen.Add($"{date:O}|{e.Name}"))
                    throw new FileGatewayException("FileDefinitionConflict", $"duplicate: {e.Name}");
                files.Add(new(date, dir + "/" + e.Name, e));
            }
        }
        return files.OrderByDescending(f => f.SnapshotTimestamp)
                    .ThenBy(f => f.Entry.Name, FileNameComparison.Comparer).ToList();
    }

    private static string ExpandDate(string template, DateTimeOffset siteLocalMidnight)
        => template.Replace("{yyyy}", siteLocalMidnight.ToString("yyyy", CultureInfo.InvariantCulture))
                   .Replace("{MM}", siteLocalMidnight.ToString("MM", CultureInfo.InvariantCulture))
                   .Replace("{dd}", siteLocalMidnight.ToString("dd", CultureInfo.InvariantCulture));
}
```

`HistoryCursor`: `LogCursor`와 동일 구조(purpose만 `ConfigurationTokenKinds.ContinuationPurpose`, claims `equipmentId`/`configurationType`/`from`/`to`/`lastTs`/`lastName`). 중복 구현을 줄이려면 Core `Tokens`에 공용 `CursorBinding` 헬퍼를 두고 양쪽에서 사용해도 된다(단순 문자열 비교 + codec 호출).

`ConfigurationQueryService`:
- `GetHistoryAsync`: from/to 필수·`from >= to`·`HistoryMaxQueryRange` 검증(Api 계층에서 파싱 후에도 방어) → 정의 조회 → `HistoryResolver` → cursor 바인딩/스킵(`snapshotTimestamp < lastTs` 스킵 + 동일 ts에서 이름 이후) → limit 페이지 → 각 항목 fileId 발급(`FileIdSnapshotPurpose`, claims `equipmentId`/`configurationType`/`ts`/`fileName`).
- `LocateByFileIdAsync`: purpose 분기(Current/Snapshot). Snapshot: 정의 조회 → `ts`로 날짜 슬롯 marker 재확인(`FileExistsAsync` false → `FileNotFound`) → 디렉터리 나열 → ci 이름 매치 → 부재 `FileNotFound` → stat으로 size 확정. Current: Task 12 분기.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~HistoryResolverTests|FullyQualifiedName~ConfigurationQueryServiceTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(configurations): snapshot history with completion markers, pagination and ids"
```

---

### Task 14: Api bootstrap — 인증/오류매핑/감사/Health/옵션

**Files:**
- Create: `src/FileGateway.Api/Program.cs`(전체 재작성), `Options/FileGatewayOptions.cs`, `Auth/ApiKeyMiddleware.cs`, `Errors/ErrorMappingMiddleware.cs`, `Audit/AuditMiddleware.cs`, `Endpoints/HealthEndpoints.cs`, `appsettings.json`
- Test: `tests/FileGateway.IntegrationTests/Api/ApiBootstrapTests.cs`, `tests/FileGateway.UnitTests/TestUtils/FixedSnapshotView.cs`

**Interfaces:**
- Consumes: Task 4/7 산출
- Produces:
  - `FileGatewayOptions`(섹션 "FileGateway"): `Logs.MaxQueryRange`(기본 31일), `Configurations.HistoryMaxQueryRange`(기본 366일), `Paging.LimitDefault=100`/`LimitMax=1000`, `Tokens.FileIdTtl=24h`/`ContinuationTtl=30분`, `ReferenceData.CacheTtl=15분`. 시작 시 `MaxQueryRange < 2일`이면 실패.
  - `ApiKeyOptions`(섹션 "Authentication"): `List<ApiKeyEntry> ApiKeys { string Key; string CallerId; }` — Secret 공급(환경변수 등).
  - 미들웨어 순서: **Audit(최외곽) → ErrorMapping → ApiKey → endpoints**. 이유: 엔드포인트/ApiKey에서 던진 예외는 ErrorMapping이 받아 ProblemDetails를 쓰고 `Items["Audit.ErrorCode"]=code`를 남긴 뒤 Audit이 unwind하므로, Audit은 **실패 요청을 포함해** 최종 `Response.StatusCode`와 errorCode를 함께 기록할 수 있다(확정 결정 15).
  - DI 등록(이후 Task가 확장): `ITokenCodec=DataProtectionTokenCodec`, `IFileAccess=FtpFileAccess`(transient)+`FtpConcurrencyLimiter`(singleton), `ReferenceDataCache`(singleton) as `IReferenceDataView`, `LogQueryService`/`ConfigurationQueryService`(옵션 주입), `TimeProvider.System`.
  - `ErrorMappingMiddleware`: `FileGatewayException` → `FileGatewayErrors.Map(code)`로 (status,title) → `{type:"about:blank",title,status,code,traceId}` JSON + `Items["Audit.ErrorCode"]=ex.Code`. `OperationCanceledException` + `HttpContext.RequestAborted` → `Items["Audit.ErrorCode"]="ClientCancelled"` 처리 후 로그, 새 응답 없음(이미 시작됐을 수 있음). 그 외 → 500 `InternalError`(상세는 서버 로그만, `Items["Audit.ErrorCode"]="InternalError"`).
  - `FileGatewayErrors`(Core.Errors): 코드→(status,title) 정적 사전. Global Constraints 오류 표와 1:1.
  - `AuditMiddleware`: 완료 시 `ILogger("FileGateway.Audit")` 구조화 로그 — `callerId`,`clientIp`(X-Forwarded-For 무시, RemoteIpAddress),`method`,`path`(route template),`equipmentId`,`logType`/`configurationType`(Items),`fileId`(Items),`fileName`,`fileSize`(Items),**`status`=최종 Response.StatusCode**,**`errorCode`=Items["Audit.ErrorCode"]**,"elapsedMs". `/health/*` 미기록. API Key 원문·token payload·물리 경로 미기록.
  - `/health/live` 200 상시. `/health/ready`: `GetSnapshotAsync`를 짧은 timeout(예: 5초 linked CTS)으로 호출해 **최초 기준정보 로딩을 실제로 유발**하고 결과를 반영한다(확정 결정 14). 성공 또는 stale cache 존재 → 200(본문에 `lastGoodRefreshAt`/`stale` 정보 포함). 최초 로딩 실패/timeout으로 usable cache 없음 → 503. FTP 서버 접근 없음.

- [ ] **Step 1: 실패 테스트**

```csharp
// tests/FileGateway.IntegrationTests/Api/ApiBootstrapTests.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileGateway.IntegrationTests.Api;

public class ApiBootstrapTests
{
    private sealed class Factory(Action<IServiceCollection>? configure = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.Configure<AuthenticationOptions>(o => o.ApiKeys =
                    [new() { Key = "test-key", CallerId = "caller-1" }]);
                configure?.Invoke(services);
            });
        }
    }

    private sealed class CountingSource(ReferenceDataSnapshot snapshot) : IReferenceDataSource
    {
        public int Calls;
        public Task<ReferenceDataRaw> ReadAsync(CancellationToken ct)
        { Calls++; return Task.FromResult(new ReferenceDataRaw(["EQ-001"], [], [], [])); }
    }

    private static Factory FactoryWithSnapshot(bool withUsableSnapshot)
        => new(services =>
        {
            var view = new FixedSnapshotView(withUsableSnapshot
                ? ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], []))
                : null);
            services.AddSingleton<IReferenceDataView>(view);
        });

    [Fact]
    public async Task Missing_api_key_is_401_InvalidApiKey()
    {
        using var factory = FactoryWithSnapshot(true);
        var response = await factory.CreateClient().GetAsync("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(401, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidApiKey", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Wrong_api_key_is_401()
    {
        using var factory = FactoryWithSnapshot(true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(401, (int)(await client.GetAsync("/api/v1/equipments/EQ-001/file-types")).StatusCode);
    }

    [Fact]
    public async Task Api_key_in_query_string_is_not_accepted()
    {
        using var factory = FactoryWithSnapshot(true);
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/equipments/EQ-001/file-types?X-Api-Key=test-key");
        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Health_live_is_ok_even_without_reference_data()
    {
        using var factory = FactoryWithSnapshot(false);
        Assert.Equal(200, (int)(await factory.CreateClient().GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Health_ready_triggers_initial_load_and_fails_when_unavailable()
    {
        // ready는 최초 기준정보 로딩을 실제로 유발한다(확정 결정 14). FixedSnapshotView(null)은
        // GetSnapshotAsync에서 ReferenceDataUnavailable을 throw하므로 ready는 503이고,
        // 실 ReferenceDataCache라면 ready 호출 시점에 source가 1회 호출된다.
        using var noData = FactoryWithSnapshot(false);
        Assert.Equal(503, (int)(await noData.CreateClient().GetAsync("/health/ready")).StatusCode);

        using var withData = FactoryWithSnapshot(true);
        Assert.Equal(200, (int)(await withData.CreateClient().GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Health_ready_induces_single_initial_load_on_real_cache()
    {
        var source = new CountingSource(ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], [])));
        using var factory = new Factory(services =>
        {
            services.AddSingleton<IReferenceDataSource>(source);
            services.AddSingleton<IReferenceDataView>(sp => new ReferenceDataCache(
                sp.GetRequiredService<IReferenceDataSource>(), TimeSpan.FromMinutes(15)));
        });

        var client = factory.CreateClient();
        Assert.Equal(200, (int)(await client.GetAsync("/health/ready")).StatusCode);   // 최초 로딩 유발
        Assert.Equal(1, source.Calls);                                                 // 로딩 실제 실행(단 1회)
        await client.GetAsync("/health/ready");                                        // TTL 내 재호출
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Audit_log_records_failed_request_with_status_and_error_code()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new Factory(s => s.AddSingleton<ILoggerProvider>(logs));
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/equipments/EQ-001/file-types"); // API Key 누락 → 401
        Assert.Equal(401, (int)response.StatusCode);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        Assert.Contains("401", entry.Message);                    // 최종 status
        Assert.Contains("InvalidApiKey", entry.Message);           // 안정적 오류 분류
    }


    [Fact]
    public async Task Reference_data_unavailable_maps_to_503_problem_details()
    {
        using var factory = FactoryWithSnapshot(false); // GetSnapshotAsync가 ReferenceDataUnavailable throw
        factory.CreateClient().DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        var response = await factory.CreateClient()
            .SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/equipments/EQ-001/file-types")
                { Headers = { { "X-Api-Key", "test-key" } } });
        Assert.Equal(503, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ReferenceDataUnavailable", body.GetProperty("code").GetString());
        Assert.NotNull(body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task MaxQueryRange_below_two_days_fails_startup()
    {
        using var factory = new Factory(s => s.PostConfigure<FileGatewayOptions>(
            o => o.Logs.MaxQueryRange = TimeSpan.FromDays(1)));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Audit_log_contains_caller_and_endpoint_without_key()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = new Factory(s =>
        {
            s.AddSingleton<ILoggerProvider>(logs);
            var view = new FixedSnapshotView(ReferenceDataSnapshotBuilder.Build(new(["EQ-001"], [], [], [])));
            s.AddSingleton<IReferenceDataView>(view);
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/equipments/EQ-001/file-types");
        request.Headers.Add("X-Api-Key", "test-key");
        await client.SendAsync(request);

        var entry = logs.Entries.Single(e => e.Category == "FileGateway.Audit");
        Assert.Contains("caller-1", entry.Message);
        Assert.DoesNotContain("test-key", entry.Message);
    }
}
```

(`FixedSnapshotView`: `IReferenceDataView` 구현, null이면 `GetSnapshotAsync`에서 `FileGatewayException("ReferenceDataUnavailable")` throw. `CollectingLoggerProvider`: `ILoggerProvider` 테스트 더블.)

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~ApiBootstrapTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Api/Program.cs
using FileGateway.Api.Auth;
using FileGateway.Api.Audit;
using FileGateway.Api.Errors;
using FileGateway.Api.Options;
using FileGateway.Configurations;
using FileGateway.Core.Tokens;
using FileGateway.Infrastructure.Ftp;
using FileGateway.Infrastructure.ReferenceData;
using FileGateway.Infrastructure.Tokens;
using FileGateway.Logs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FileGatewayOptions>(builder.Configuration.GetSection("FileGateway"));
builder.Services.AddOptions<FileGatewayOptions>()
    .Validate(o => o.Logs.MaxQueryRange >= TimeSpan.FromDays(2), "Logs.MaxQueryRange must be >= 2 days")
    .Validate(o => o.Configurations.HistoryMaxQueryRange > TimeSpan.Zero, "HistoryMaxQueryRange must be positive")
    .ValidateOnStart();
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));

builder.Services.AddDataProtection(); // Task 20에서 IIS persist 구성 추가
builder.Services.AddSingleton<ITokenCodec, DataProtectionTokenCodec>();
builder.Services.AddSingleton<FtpConcurrencyLimiter>();
builder.Services.AddSingleton<FtpOptions>(sp => sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.Ftp
    ?? throw new InvalidOperationException("Ftp options required"));
builder.Services.AddTransient<IFileAccess, FtpFileAccess>();
builder.Services.AddSingleton<IReferenceDataSource>(sp => new SpReferenceDataSource(
    builder.Configuration.GetConnectionString("ReferenceData")
    ?? throw new InvalidOperationException("ReferenceData connection string required")));
builder.Services.AddSingleton<IReferenceDataView>(sp => new ReferenceDataCache(
    sp.GetRequiredService<IReferenceDataSource>(),
    sp.GetRequiredService<IOptions<FileGatewayOptions>>().Value.ReferenceData.CacheTtl));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILogQueryService>(sp => LogQueryServiceFactory.Create(sp));
builder.Services.AddSingleton<IConfigurationQueryService>(sp => ConfigurationQueryServiceFactory.Create(sp));

var app = builder.Build();
app.UseMiddleware<AuditMiddleware>();       // 최외곽: 최종 status + Audit.ErrorCode를 함께 기록
app.UseMiddleware<ErrorMappingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapHealthEndpoints();
app.MapCatalogEndpoints();          // Task 15 (등록부만 먼저 추가 가능)
app.MapLogEndpoints();              // Task 16
app.MapConfigurationEndpoints();    // Task 17
app.MapFileEndpoints();             // Task 18
app.Run();

public partial class Program;
```

`ApiKeyMiddleware`: `/api/` prefix에서만 동작. header `X-Api-Key` 조회(없거나 목록 불일치 → `FileGatewayException("InvalidApiKey")` → ErrorMapping이 401 ProblemDetails). 비교는 `CryptographicOperations.FixedTimeEquals(UTF8). 성공 시 `HttpContext.Items["CallerId"]` 설정. query string은 전혀 읽지 않는다.

`ErrorMappingMiddleware`:

```csharp
public sealed class ErrorMappingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (FileGatewayException ex)
        {
            var (status, title) = FileGatewayErrors.Map(ex.Code);
            context.Items["Audit.ErrorCode"] = ex.Code;
            if (context.Response.HasStarted) { /* 다운로드 중 오류: 응답 불가, 로그만 */ }
            else await WriteProblem(context, status, ex.Code, title);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Items["Audit.ErrorCode"] = "ClientCancelled"; // 연결 종료에 맡긴다(새 응답 없음)
        }
        catch (Exception ex)
        {
            context.Items["Audit.ErrorCode"] = "InternalError";
            context.RequestServices.GetRequiredService<ILogger<ErrorMappingMiddleware>>()
                .LogError(ex, "unhandled error {Path}", context.Request.Path);
            if (!context.Response.HasStarted)
                await WriteProblem(context, 500, "InternalError", "Internal server error");
        }
    }

    private static async Task WriteProblem(HttpContext ctx, int status, string code, string title)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank", title, status, code,
            traceId = Activity.Current?.Id ?? ctx.TraceIdentifier,
        }));
    }
}
```

`AuditMiddleware`는 `HttpContext.Items`에서 `"Audit.EquipmentId"`, `"Audit.LogType"`, `"Audit.ConfigurationType"`, `"Audit.FileId"`, `"Audit.FileName"`, `"Audit.FileSize"`, `"Audit.ErrorCode"`를 읽어 로그. 엔드포인트(이후 Task)가 값을 채운다. `ApiKeyMiddleware`/엔드포인트가 `Items["CallerId"]`를 제공.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~ApiBootstrapTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(api): bootstrap with api key auth, problem details, audit and health"
```

---

### Task 15: Api — 설비별 제공 파일 종류 조회

**Files:**
- Create: `src/FileGateway.Api/Endpoints/CatalogEndpoints.cs`
- Test: `tests/FileGateway.IntegrationTests/Api/CatalogEndpointTests.cs`

**Interfaces:**
- Consumes: Task 14 bootstrap(`FixedSnapshotView` 오버라이드), Task 6 snapshot
- Produces: `GET /api/v1/equipments/{equipmentId}/file-types` → `200 { equipmentId, logs: [{logType, generationType}], configurations: [{configurationType}] }`. equipment 미존재 → `404 EquipmentNotFound`. 정의 없는 유효 설비 → `200` + 빈 배열. 배열은 이름 오름차순. FTP 접근 없음.

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class CatalogEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static ReferenceDataSnapshot Snapshot() => ReferenceDataSnapshotBuilder.Build(new(
        ["EQ-001"],
        [new RawServer("SRV1", "ftp1", "ftproot")],
        [
            new RawLogDefinition("EQ-001", "TraceLog", "SRV1", "Continuous",
                "Trace/cur", "Trace_*.log", "Multiple", "Template", "Trace/cur/Trace_{subtype}.log", "[]"),
            new RawLogDefinition("EQ-001", "EventLog", "SRV1", "Hourly",
                "Logs/{yyyy}/{MM}/{dd}/{HH}", "Event_*.zip", "Multiple", "Template",
                "Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip", "[]"),
        ],
        [new RawConfigurationDefinition("EQ-001", "PM", "SRV1",
            "PM/current", "PM_*.cfg", "PM/history/{yyyy}/{MM}/{dd}", "PM_*.cfg",
            "PM/history/{yyyy}/{MM}/{dd}/_DONE")]));

    private async Task<JsonElement> GetAsync(string path)
    {
        var client = factory.CreateClient(); // ApiFactory: FixedSnapshotView(Snapshot()) + key "test-key" 기본 헤더
        using var response = await client.GetAsync(path);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Returns_projection_sorted_without_internal_fields()
    {
        var body = await GetAsync("/api/v1/equipments/EQ-001/file-types");
        var logs = body.GetProperty("logs");
        Assert.Equal(2, logs.GetArrayLength());
        Assert.Equal("EventLog", logs[0].GetProperty("logType").GetString());
        Assert.Equal("Hourly", logs[0].GetProperty("generationType").GetString());
        Assert.Equal("TraceLog", logs[1].GetProperty("logType").GetString());
        var json = body.GetRawText();
        Assert.DoesNotContain("serverId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pathTemplate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rootPath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_equipment_is_404_EquipmentNotFound()
    {
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/equipments/EQ-X/file-types");
        Assert.Equal(404, (int)response.StatusCode);
        Assert.Equal("EquipmentNotFound",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Valid_equipment_without_definitions_returns_empty_arrays()
    {
        factory.SetSnapshot(ReferenceDataSnapshotBuilder.Build(new(["EQ-EMPTY"], [], [], [])));
        var body = await GetAsync("/api/v1/equipments/EQ-EMPTY/file-types");
        Assert.Equal(0, body.GetProperty("logs").GetArrayLength());
        Assert.Equal(0, body.GetProperty("configurations").GetArrayLength());
    }
}
```

(`ApiFactory`: Task 14 패턴 재사용 + snapshot 교체 헬퍼. IFileAccess가 호출되면 실패하는 `ThrowingFileAccess`로 등록해 "FTP 접근 없음"을 구조적으로 검증한다.)

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~CatalogEndpointTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Api/Endpoints/CatalogEndpoints.cs
namespace FileGateway.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/equipments/{equipmentId}/file-types",
            async (string equipmentId, IReferenceDataView referenceData, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Items["Audit.EquipmentId"] = equipmentId;
            var snapshot = await referenceData.GetSnapshotAsync(ct);
            if (!snapshot.EquipmentIds.Contains(equipmentId))
                throw new FileGatewayException("EquipmentNotFound", "unknown equipment");
            return Results.Ok(new
            {
                equipmentId,
                logs = snapshot.GetLogSummaries(equipmentId)
                    .Select(s => new { logType = s.LogType, generationType = s.GenerationType }),
                configurations = snapshot.GetConfigurationTypeSummaries(equipmentId)
                    .Select(t => new { configurationType = t }),
            });
        });
    }
}
```

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~CatalogEndpointTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(api): equipment file-type catalog from validated reference data"
```

---

### Task 16: Api — 로그 목록/직접 다운로드 endpoints

**Files:**
- Create: `src/FileGateway.Api/Endpoints/LogEndpoints.cs`, `src/FileGateway.Api/Downloading/DownloadResult.cs`(Task 18에서 완성, 여기선 최소 스트리밍 실행기 포함)
- Test: `tests/FileGateway.IntegrationTests/Api/LogEndpointTests.cs`

**Interfaces:**
- Consumes: Task 11 `ILogQueryService`, Task 14 bootstrap
- Produces:
  - `GET /api/v1/logs` query 바인딩: `equipmentId`,`logType` 필수(누락 `InvalidRequest`), `from`,`to`(ISO-8601), `subtype`, `attr.<name>` 반복, `limit`(기본/최대 초과 `InvalidRequest`), `continuationToken`. 응답 `{ items, continuationToken }`.
  - `GET /api/v1/logs/download` — 내부 `ResolveSingleAsync` 후 0→`FileNotFound`, 1→다운로드, N→`MultipleFilesMatched`.
  - `DownloadResult(LocatedFile file, IFileAccess fileAccess) : IResult` — `Content-Type: application/octet-stream`, `Content-Disposition: attachment; filename=...`(header-safe), `Content-Length`=시작 직전 관측 크기, body는 `ExactLengthStream` 복사. `Audit.FileName`/`Audit.FileSize`/`Audit.FileId` Items 설정은 호출 엔드포인트에서.

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class LogEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // factory: snapshot(EQ-001/EventLog Hourly flat + TraceLog Continuous), FakeFileAccess 기반 IFileAccess 등록
    // FakeFileAccess 시드: Logs/all/2026082218_Event.zip 등

    [Fact]
    public async Task List_returns_envelope_with_camel_case_fields()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
        var item = body.GetProperty("items")[0];
        foreach (var field in new[] { "fileId", "fileName", "equipmentId", "logType", "subtype", "timestamp", "size", "isContinuous", "attributes" })
            Assert.True(item.TryGetProperty(field, out _), $"missing {field}");
    }

    [Fact]
    public async Task Empty_result_is_items_empty_token_null()
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=2020-01-01T00:00:00%2B09:00&to=2020-01-02T00:00:00%2B09:00");
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.IsNull(body.GetProperty("continuationToken").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task Missing_required_params_is_400_InvalidRequest()
    {
        using var response = await factory.CreateClient().GetAsync("/api/v1/logs?equipmentId=EQ-001");
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("InvalidRequest", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Continuous_with_from_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00:00:00%2B09:00")).code);

    [Fact]
    public async Task Limit_above_max_is_400()
        => Assert.Equal("InvalidRequest", (await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit={1001}")).code);

    [Fact]
    public async Task Bad_timestamp_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&from=yesterday")).code);

    [Fact]
    public async Task Pagination_walks_pages_and_allows_limit_change()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = p1.GetProperty("continuationToken").GetString();
        Assert.NotNull(token);
        var p2 = await GetJson($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=2&continuationToken={Uri.EscapeDataString(token!)}");
        Assert.True(p2.GetProperty("items").GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Continuation_with_changed_condition_is_400()
    {
        var p1 = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog&limit=1");
        var token = Uri.EscapeDataString(p1.GetProperty("continuationToken").GetString()!);
        var error = await GetError($"/api/v1/logs?equipmentId=EQ-001&logType=EventLog&subtype=X&continuationToken={token}");
        Assert.Equal("InvalidRequest", error.code);
    }

    [Fact]
    public async Task Download_single_match_streams_with_headers()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2026-08-22T18:00:00%2B09:00&to=2026-08-22T19:00:00%2B09:00");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(response.Content.Headers.ContentLength, bytes.Length);
    }

    [Fact]
    public async Task Download_multiple_match_is_409()
    {
        var error = await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog");
        Assert.Equal(409, error.status);
        Assert.Equal("MultipleFilesMatched", error.code);
    }

    [Fact]
    public async Task Download_no_match_is_404_FileNotFound()
    {
        var error = await GetError("/api/v1/logs/download?equipmentId=EQ-001&logType=EventLog&from=2020-01-01T00:00:00%2B09:00&to=2020-01-02T00:00:00%2B09:00");
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task Error_body_has_no_physical_path()
    {
        var json = await factory.CreateClient()
            .GetStringAsync("/api/v1/logs?equipmentId=EQ-001&logType=Nope");
        Assert.DoesNotContain("ftp1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ftproot", json, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~LogEndpointTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

쿼리 바인더(`LogEndpoints` 내 정적 메서드 `ParseListQuery(HttpRequest, FileGatewayOptions)`):

```csharp
internal static LogListQuery ParseListQuery(HttpRequest request, FileGatewayOptions opt)
{
    string Required(string name)
        => request.Query.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString() : throw new FileGatewayException("InvalidRequest", $"missing {name}");
    var equipmentId = Required("equipmentId");
    var logType = Required("logType");

    DateTimeOffset? Time(string name) => request.Query.TryGetValue(name, out var v) && v.Count > 0
        ? SiteTime.Parse(v.ToString()) : null;

    string? subtype = request.Query.TryGetValue("subtype", out var s) && s.Count > 0 ? s.ToString() : null;
    var attrs = request.Query.Where(kv => kv.Key.StartsWith("attr.", StringComparison.Ordinal))
        .ToDictionary(kv => kv.Key["attr.".Length..], kv => kv.Value.ToString());

    int? limit = null;
    if (request.Query.TryGetValue("limit", out var l) && l.Count > 0)
    {
        if (!int.TryParse(l, out var n) || n <= 0)
            throw new FileGatewayException("InvalidRequest", "invalid limit");
        if (n > opt.Paging.LimitMax)
            throw new FileGatewayException("InvalidRequest", $"limit exceeds max {opt.Paging.LimitMax}");
        limit = n;
    }
    string? token = request.Query.TryGetValue("continuationToken", out var t) && t.Count > 0 ? t.ToString() : null;
    return new(equipmentId, logType, Time("from"), Time("to"), subtype, attrs, limit, token);
}
```

엔드포인트는 `ParseListQuery` → `logQueryService.ListAsync` → `Results.Ok(new { items, continuationToken })` + `Items["Audit.*"]` 채움. download는 `ResolveSingleAsync` 후 분기, 1개면 `new DownloadResult(match.File!, fileAccess)` 반환.

`DownloadResult`:

```csharp
// src/FileGateway.Api/Downloading/DownloadResult.cs
namespace FileGateway.Api.Downloading;

public sealed class DownloadResult(LocatedFile file, IFileAccess fileAccess) : IResult
{
    public async Task ExecuteAsync(HttpContext ctx)
    {
        var open = await fileAccess.OpenReadAsync(file.Server, file.RelativePath, ctx.RequestAborted);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength = open.Length;                       // 시작 직전 크기 = 전송 상한
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.Headers.ContentDisposition =
            ContentDispositionHelper.Attachment(file.FileName);
        await using var capped = new ExactLengthStream(open.Stream, open.Length);
        await capped.CopyToAsync(ctx.Response.Body, 81_920, ctx.RequestAborted); // 시작 후 오류 → 중단(ClientCancelled/IO 분류는 미들웨어)
    }
}
```

`ContentDispositionHelper.Attachment(fileName)`: CR/LF/비ASCII 제거한 ASCII fallback + `filename*=UTF-8''<percent-encoded>`.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~LogEndpointTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(api): log list and conditional download endpoints"
```

---

### Task 17: Api — Configuration Current/History endpoints

**Files:**
- Create: `src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs`
- Test: `tests/FileGateway.IntegrationTests/Api/ConfigurationEndpointTests.cs`

**Interfaces:**
- Consumes: Task 12/13 `IConfigurationQueryService`, Task 16 `DownloadResult`
- Produces:
  - `GET /api/v1/configurations/current?equipmentId&configurationType` → Current item 배열(단순 배열, envelope 아님), 빈 결과 `200 []`
  - `GET /api/v1/configurations/current/download` → 0/1/N 분기
  - `GET /api/v1/configurations/history?equipmentId&configurationType&from&to[&limit&continuationToken]` → `{items, continuationToken}`; from/to 필수, `HistoryMaxQueryRange` 검증

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class ConfigurationEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // factory: EQ-001/PM 정의 + FakeFileAccess 시드(current PM1/PM2, history 08-21 marker, 08-22 marker+PM1.cfg)

    [Fact]
    public async Task Current_returns_plain_sorted_array()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=PM");
        var arr = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, arr.ValueKind); // envelope 아님
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("PM1.cfg", arr[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Current_empty_returns_empty_array_with_200()
    {
        factory.Ftp.RemoveFile("PM/current/PM1.cfg"); factory.Ftp.RemoveFile("PM/current/PM2.cfg");
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task Current_download_multiple_is_409()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal("MultipleFilesMatched", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Current_download_single_streams()
    {
        factory.Ftp.RemoveFile("PM/current/PM2.cfg");
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task History_requires_from_and_to()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM")).code);

    [Fact]
    public async Task History_over_max_range_is_400()
        => Assert.Equal("InvalidRequest", (await GetError("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2020-01-01T00:00:00%2B09:00&to=2030-01-01T00:00:00%2B09:00")).code);

    [Fact]
    public async Task History_returns_only_marked_sets_with_envelope()
    {
        var body = await GetJson("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-20T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength()); // 08-22만 marker 존재
        Assert.Equal("PM1.cfg", items[0].GetProperty("fileName").GetString());
        Assert.NotNull(items[0].GetProperty("snapshotTimestamp").GetString());
    }

    [Fact]
    public async Task Unknown_type_is_404_ConfigurationDefinitionNotFound()
        => Assert.Equal("ConfigurationDefinitionNotFound",
            (await GetError("/api/v1/configurations/current?equipmentId=EQ-001&configurationType=NOPE")).code);
}
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~ConfigurationEndpointTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

Task 16의 바인더 패턴 재사용. current는 `Results.Ok(items)`(배열 직렬화), history는 `from/to` 필수 검증(누락 `InvalidRequest`), `from >= to`, 범위 초과를 `IConfigurationQueryService.GetHistoryAsync`가 방어하되 엔드포인트에서도 시각 파싱 오류를 `InvalidRequest`로 변환. Audit Items 채움.

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~ConfigurationEndpointTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(api): configuration current and history endpoints"
```

---

### Task 18: Api — 공통 files endpoints (metadata + download)

**Files:**
- Create: `src/FileGateway.Api/Endpoints/FileEndpoints.cs`
- Test: `tests/FileGateway.IntegrationTests/Api/FileEndpointTests.cs`

**Interfaces:**
- Consumes: Task 4 codec, Task 11/13 `LocateByFileIdAsync`, Task 16 `DownloadResult`
- Produces:
  - `GET /api/v1/files/{fileId}` → `200 { fileId, fileName, size }` (원본 fileId 문자열 회신, 실제 원격 stat 수행). HEAD 없음.
  - `GET /api/v1/files/{fileId}/download` → `DownloadResult`.
  - fileId 오류 매핑: `TokenValidity.Invalid` 또는 purpose 불일치 → `InvalidFileId`; `Expired` → `FileIdExpired`; 정의/파일 부재는 feature 서비스 예외 그대로.

- [ ] **Step 1: 실패 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class FileEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> GetFileIdAsync() // 로그 목록에서 fileId 확보
    {
        var body = await GetJson("/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        return body.GetProperty("items")[0].GetProperty("fileId").GetString()!;
    }

    [Fact]
    public async Task Metadata_returns_minimal_fields_only()
    {
        var fileId = await GetFileIdAsync();
        var body = await GetJson($"/api/v1/files/{Uri.EscapeDataString(fileId)}");
        Assert.Equal(3, body.EnumerateObject().Count()); // fileId/fileName/size 만
        Assert.Equal(fileId, body.GetProperty("fileId").GetString());
        Assert.True(body.GetProperty("size").GetInt64() >= 0);
    }

    [Fact]
    public async Task Download_streams_with_content_length()
    {
        var fileId = await GetFileIdAsync();
        using var response = await factory.CreateClient()
            .GetAsync($"/api/v1/files/{Uri.EscapeDataString(fileId)}/download");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.True(response.Content.Headers.ContentLength > 0);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(response.Content.Headers.ContentLength.Value, bytes.Length);
    }

    [Fact]
    public async Task Garbage_file_id_is_400_InvalidFileId()
    {
        var error = await GetError("/api/v1/files/garbage");
        Assert.Equal(400, error.status);
        Assert.Equal("InvalidFileId", error.code);
    }

    [Fact]
    public async Task Expired_file_id_is_410()
    {
        var fileId = await GetFileIdAsync();
        // TTL 24h 기본 → 만료 시뮬레이션은 factory 옵션에서 FileIdTtl=1ms 로 재발급
        factory.SetTokenTtl(TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        var expiredId = await GetFileIdAsync();
        var error = await GetError($"/api/v1/files/{Uri.EscapeDataString(expiredId)}");
        Assert.Equal(410, error.status);
        Assert.Equal("FileIdExpired", error.code);
    }

    [Fact]
    public async Task Deleted_logical_file_is_404_FileNotFound()
    {
        var fileId = await GetFileIdAsync();
        factory.Ftp.RemoveFile("Logs/all/2026082218_Event.zip");
        var error = await GetError($"/api/v1/files/{Uri.EscapeDataString(fileId)}");
        Assert.Equal(404, error.status);
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task Snapshot_fileId_rechecks_marker()
    {
        var history = await GetJson("/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-22T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var snapshotFileId = history.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        factory.Ftp.RemoveFile("PM/history/2026/08/22/_DONE"); // marker 제거, 파일은 잔존
        var error = await GetError($"/api/v1/files/{Uri.EscapeDataString(snapshotFileId)}");
        Assert.Equal("FileNotFound", error.code);
    }

    [Fact]
    public async Task No_head_endpoint_exists()
    {
        var fileId = await GetFileIdAsync();
        using var response = await factory.CreateClient()
            .SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/v1/files/{Uri.EscapeDataString(fileId)}"));
        Assert.Equal(405, (int)response.StatusCode); // MapGet만 존재
    }

    [Fact]
    public async Task Truncated_during_transfer_aborts_response()
    {
        var fileId = await GetFileIdAsync();
        factory.Ftp.TruncateAfterOpen("Logs/all/2026082218_Event.zip", bytesToKeep: 1);
        using var response = await factory.CreateClient()
            .GetAsync($"/api/v1/files/{Uri.EscapeDataString(fileId)}/download", HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        // 선언 길이보다 짧게 끝남 → 본문이 Content-Length 미달로 종료/예외
        await Assert.ThrowsAnyAsync<Exception>(() => stream.CopyToAsync(new MemoryStream()));
    }
}
```

(`FakeFileAccess.TruncateAfterOpen`: open 후 실제 bytes보다 작게 반환하는 시나리오 훅 — `FakeFileAccess`에 옵션 추가.)

- [ ] **Step 2: 실패 확인** — Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~FileEndpointTests"` / Expected: FAIL

- [ ] **Step 3: 구현**

```csharp
// src/FileGateway.Api/Endpoints/FileEndpoints.cs
namespace FileGateway.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/files/{fileId}", async (
            string fileId, ITokenCodec codec, ILogQueryService logs,
            IConfigurationQueryService configurations, IFileAccess fileAccess,
            HttpContext ctx, CancellationToken ct) =>
        {
            var located = await LocateAsync(fileId, codec, logs, configurations, ctx, ct);
            ctx.Items["Audit.FileId"] = fileId;
            ctx.Items["Audit.FileName"] = located.FileName;
            return Results.Ok(new { fileId, fileName = located.FileName, size = located.Size });
        });

        app.MapGet("/api/v1/files/{fileId}/download", async (
            string fileId, ITokenCodec codec, ILogQueryService logs,
            IConfigurationQueryService configurations, IFileAccess fileAccess,
            HttpContext ctx, CancellationToken ct) =>
        {
            var located = await LocateAsync(fileId, codec, logs, configurations, ctx, ct);
            ctx.Items["Audit.FileId"] = fileId;
            ctx.Items["Audit.FileName"] = located.FileName;
            return new DownloadResult(located, fileAccess);
        });
    }

    private static async Task<LocatedFile> LocateAsync(
        string fileId, ITokenCodec codec, ILogQueryService logs,
        IConfigurationQueryService configurations, HttpContext ctx, CancellationToken ct)
    {
        var decoded = codec.Unprotect(fileId);
        if (decoded.Validity == TokenValidity.Invalid)
            throw new FileGatewayException("InvalidFileId", "malformed file id");
        if (decoded.Validity == TokenValidity.Expired)
            throw new FileGatewayException("FileIdExpired", "file id expired");
        var payload = decoded.Payload!;
        return payload.Purpose switch
        {
            LogTokenKinds.FileIdPurpose => await logs.LocateByFileIdAsync(payload, ct),
            ConfigurationTokenKinds.FileIdCurrentPurpose or ConfigurationTokenKinds.FileIdSnapshotPurpose
                => await configurations.LocateByFileIdAsync(payload, ct),
            _ => throw new FileGatewayException("InvalidFileId", "unknown file id purpose"),
        };
    }
}
```

- [ ] **Step 4: 통과 확인 후 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~FileEndpointTests"` / Expected: PASS

```bash
git add -A && git commit -m "feat(api): common file id metadata and streaming download endpoints"
```

---

### Task 19: End-to-End 통합테스트 (Testcontainers MSSQL + in-proc FTP)

**Files:**
- Test: `tests/FileGateway.IntegrationTests/Api/EndToEndTests.cs` (+ `ApiFactory`에 실제 컴포지션 모드 추가)

**Interfaces:**
- Consumes: Task 7 `DatabaseFixture`, Task 5 FTP fixture, Task 14~18 전체
- Produces: 실제 DI 구성(실 ReferenceDataCache + SP + FluentFTP + DataProtection)으로 catalog→list→download→history→marker 제거 시나리오 검증. 서비스 오버라이드 없음.

- [ ] **Step 1: 시나리오 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class EndToEndTests : IClassFixture<DatabaseFixture>, IClassFixture<FtpAdapterFixture>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        // DB 시드: EQ-001(EventLog Hourly flat, TraceLog Continuous, PM config), SRV1 → 127.0.0.1
        await DbSeedAsync();
        // FTP 시드: Logs/all/2026082218_Event.zip(100B), Trace/cur/Trace_PM.log, PM/current/PM1.cfg·PM2.cfg,
        //          PM/history/2026/08/22/{PM1.cfg,PM2.cfg,_DONE}
        await FtpSeedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceData"] = _db.ConnectionString,
                ["Authentication:ApiKeys:0:Key"] = "e2e-key",
                ["Authentication:ApiKeys:0:CallerId"] = "e2e-caller",
                ["FileGateway:Ftp:UserName"] = FtpAdapterFixture.UserName,
                ["FileGateway:Ftp:Password"] = FtpAdapterFixture.Password,
                ["FileGateway:Ftp:HostPortOverride"] = _ftp.Port.ToString(),
            }));
        });
        // FtpOptions가 IConfiguration에서 바인딩되도록 Program.cs를 Task 14 방식에서
        // builder.Configuration.GetSection("FileGateway:Ftp").Get<FtpOptions>() 로 조정한다.
    }

    [Fact]
    public async Task Full_flow_catalog_list_download_history_marker_removal()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "e2e-key");

        // 1) catalog — FTP 접근 없이 기준정보 투영
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(2, catalog.GetProperty("logs").GetArrayLength());

        // 2) 로그 목록 → fileId → metadata → download
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/logs?equipmentId=EQ-001&logType=EventLog");
        var fileId = list.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        var meta = await client.GetFromJsonAsync<JsonElement>($"/api/v1/files/{Uri.EscapeDataString(fileId)}");
        Assert.Equal("2026082218_Event.zip", meta.GetProperty("fileName").GetString());
        using var download = await client.GetAsync($"/api/v1/files/{Uri.EscapeDataString(fileId)}/download");
        Assert.Equal(100, download.Content.Headers.ContentLength);

        // 3) Continuous from 거부
        Assert.Equal(400, (int)(await client.GetAsync("/api/v1/logs?equipmentId=EQ-001&logType=TraceLog&from=2026-08-22T00:00:00%2B09:00")).StatusCode);

        // 4) Current 다운로드 409(Multiple)
        Assert.Equal(409, (int)(await client.GetAsync("/api/v1/configurations/current/download?equipmentId=EQ-001&configurationType=PM")).StatusCode);

        // 5) History marker 제거 후 fileId → 404
        var history = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/configurations/history?equipmentId=EQ-001&configurationType=PM&from=2026-08-22T00:00:00%2B09:00&to=2026-08-23T00:00:00%2B09:00");
        var snapshotId = history.GetProperty("items")[0].GetProperty("fileId").GetString()!;
        await FtpDeleteAsync("PM/history/2026/08/22/_DONE");
        Assert.Equal(404, (int)(await client.GetAsync($"/api/v1/files/{Uri.EscapeDataString(snapshotId)}")).StatusCode);
    }

    [Fact]
    public async Task New_log_type_appears_after_cache_refresh()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "e2e-key");
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(2, before.GetProperty("logs").GetArrayLength());

        await _db.ExecuteAsync(@"INSERT dbo.FgLogDefinition VALUES('EQ-001','AlarmLog','SRV1','Daily',
            'Alarms/{yyyy}/{MM}/{dd}','Alarm_*.log','Multiple','Template',
            'Alarms/{yyyy}/{MM}/{dd}/Alarm_{subtype}.log','[]');");
        _factory.Services.GetRequiredService<IReferenceDataView>() // TTL 단축 옵션으로 refresh 유도
        // 개발 편의: CacheTtl=1초로 팩토리 구성 → 1.2초 대기 후 재요청
        await Task.Delay(1200);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/equipments/EQ-001/file-types");
        Assert.Equal(3, after.GetProperty("logs").GetArrayLength()); // 코드 변경 없이 반영
    }
}
```

시드 구현: `DbSeedAsync`는 Task 7의 INSERT 패턴, `FtpSeedAsync`/`FtpDeleteAsync`는 FluentFTP 클라이언트로 fixture에 파일 업로드/삭제. E2E 팩토리의 `FileGateway:ReferenceData:CacheTtl=00:00:01`.

- [ ] **Step 2: 실패 확인 → 3: 시드/팩토리 보강으로 통과 → 4: 커밋**

Run: `dotnet test tests/FileGateway.IntegrationTests --filter "FullyQualifiedName~EndToEndTests"` / Expected: PASS

```bash
git add -A && git commit -m "test(e2e): full stack scenario with mssql container and in-proc ftp"
```

---

### Task 20: 배포 준비 — 설정/키 내구성/문서/전체 검증

**Files:**
- Modify: `src/FileGateway.Api/Program.cs`(DataProtection persist, FtpOptions 구성 바인딩), `src/FileGateway.Api/appsettings.json`, `src/FileGateway.Api/web.config`(없으면 생성)
- Create: `README.md` 갱신(실행/배포 섹션), `docs/INDEX.md`에는 변경 없음(신규 문서 아님)
- Test: 기존 전체 스위트 + `tests/FileGateway.IntegrationTests/Api/KeyPersistenceTests.cs`

**Interfaces:**
- Consumes: Task 14~19 전부
- Produces:
  - DataProtection: `PersistKeysToFileSystem`(경로 = 구성 `DataProtection:KeyDirectory`, 미설정 시 개발 ephemeral 경고 로그) + `SetApplicationName("FileGateway")` — IIS 재시작 후에도 fileId TTL 유지.
  - `appsettings.json` 전체 구조 확정(비밀 제외): `FileGateway` 섹션(Logs/Configurations/Paging/Tokens/ReferenceData/Ftp), `ConnectionStrings:ReferenceData`(환경변수로 주입), `Authentication`(환경변수/Secret 주입 안내 주석).

- [ ] **Step 1: 키 내구성 테스트**

```csharp
namespace FileGateway.IntegrationTests.Api;

public class KeyPersistenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task File_id_survives_provider_recreation_with_same_key_directory()
    {
        var keyDir = Path.Combine(Path.GetTempPath(), "fg-e2e-keys-" + Guid.NewGuid());
        factory.SetDataProtectionKeyDirectory(keyDir);

        var fileId = await factory.IssueFileIdAsync(); // list → items[0].fileId
        factory.RestartApplication(keyDir);            // 같은 key dir으로 호스트 재생성(재시작 시뮬레이션)

        var error = await factory.GetFileErrorAsync(fileId);
        Assert.NotEqual("InvalidFileId", error.code); // 키 유실이 아니면 Invalid/Expired 아님
        Assert.NotEqual("FileIdExpired", error.code);
    }
}
```

(`ApiFactory` 헬퍼 확장: `SetDataProtectionKeyDirectory`, `RestartApplication` — 기존 호스트 dispose 후 동일 keyDir 재구성.)

- [ ] **Step 2: 구현**

```csharp
// Program.cs 내 DataProtection 구성
var keyDir = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrEmpty(keyDir))
    builder.Services.AddDataProtection(o => o.ApplicationDiscriminator = "FileGateway")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDir));
else
    builder.Services.AddDataProtection(o => o.ApplicationDiscriminator = "FileGateway");
```

`appsettings.json`:

```json
{
  "FileGateway": {
    "Logs": { "MaxQueryRange": "31.00:00:00" },
    "Configurations": { "HistoryMaxQueryRange": "366.00:00:00" },
    "Paging": { "LimitDefault": 100, "LimitMax": 1000 },
    "Tokens": { "FileIdTtl": "24:00:00", "ContinuationTtl": "00:30:00" },
    "ReferenceData": { "CacheTtl": "00:15:00" },
    "Ftp": {
      "UserName": null,
      "Password": null,
      "Security": "Plain",
      "AcceptUntrustedCertificates": false,
      "ConnectTimeoutSeconds": 15,
      "ReadTimeoutSeconds": 60,
      "MaxConcurrentGlobal": 50,
      "MaxConcurrentPerServer": 5
    }
  }
}
```

비밀(`Authentication:ApiKeys`, `ConnectionStrings:ReferenceData`, `Ftp:UserName/Password`, `DataProtection:KeyDirectory`)은 환경변수/Secret으로만 주입한다고 README에 명시. README에 `Ftp:Security`(`Plain | ExplicitTls | ImplicitTls`)·인증서 정책 안내와 배포 전 확인 목록 링크(`docs/10-testing-and-deployment.md` "배포 전 필수 확인"), IIS Hosting Bundle, FTP Passive 포트 안내를 추가한다.

- [ ] **Step 3: 자동화 검증 게이트**

Run: `dotnet build && dotnet test`
Expected: 빌드 경고 0(TreatWarningsAsErrors), 전체 테스트 PASS

주의: 이 게이트 통과는 **구현 완료**를 의미할 뿐 MVP 완료가 아니다(확정 결정 18). MVP 완료는 Task 21 수동 배포 검증까지 통과해야 한다.

- [ ] **Step 4: 커밋**

```bash
git add -A && git commit -m "chore(deploy): key persistence, settings layout and runbook notes"
```

### Task 21: 수동 배포 검증 게이트 (MVP 완료 조건)

**Files:**
- Record: 검증 결과는 배포 PR 본문 또는 릴리스 노트의 체크리스트로 기록한다(신규 설계문서를 만들지 않는다).

**Interfaces:**
- Consumes: Task 20 산출(배포 가능한 빌드), `docs/10-testing-and-deployment.md` "배포 전 필수 확인" + "MVP 완료 기준"
- Produces: 운영 유사 환경(Windows Server + IIS + 실제 MSSQL + 실제 파일 서버 FTP/FTPS)에서의 수동 검증 완료. **Task 1~20의 자동화 게이트는 이 Task를 대체하지 않는다**(확정 결정 18). 아래 항목이 전부 체크되기 전까지 MVP를 완료로 선언하지 않는다.

- [ ] **Step 1: 배포 전 필수 확인** (`docs/10-testing-and-deployment.md` 원문 항목 전체)

운영 유사 환경에서 순서대로 확인하고 결과(통과/차단+원인)를 기록한다:

- [ ] HTTPS 인증서/바인딩
- [ ] IIS ASP.NET Core Hosting Bundle/권한
- [ ] 여러 `X-Api-Key` 인증/호출자 구분 및 query string key 비허용(실제 요청으로 401 확인)
- [ ] API Key 신/구 overlap 회전(두 key 동시 활성 → 구 key 폐기)
- [ ] MSSQL 연결(SP 실행 및 기준정보 로딩)
- [ ] 설비별 제공 파일 종류 API가 DB 기준정보와 일치하고 FTP 접근 없이 동작
- [ ] 기준정보 구조 validation/atomic cache 교체/stale fallback/single-flight 동작(잘못된 정의 1건 주입 → 전체 refresh 거부 확인)
- [ ] 기준정보 refresh가 FTP 실재 검사를 수행하지 않는지 확인(FTP 중단 상태에서 catalog 정상)
- [ ] 각 파일 서버 21번 제어 연결
- [ ] IIS FTP SSL 설정 확인(FTP vs FTPS) 및 `Ftp:Security` 설정값 확정
- [ ] Passive 데이터 포트 범위/방화벽
- [ ] 실제 파일 목록/다운로드(대표 로그 + Current Configuration + History Snapshot)
- [ ] 여러 시간 슬롯이 동일 물리 디렉터리를 사용하는 로그 탐색
- [ ] 디렉터리 부재/파일 서버 장애/부분 FTP 실패 구분(각각 200 빈 목록 / 502 / 전체 실패)
- [ ] FTP 전체/서버별 동시성 제한(동시 다운로드 부하 시도)
- [ ] Configuration History 완료 marker 존재 조건과 Snapshot `fileId` 재검증 동작(marker 삭제 → 404)
- [ ] token 보호 key 재시작 내구성(IIS 재시작 후 기존 fileId 유효) 및 rotation 시 기존 fileId TTL 유지
- [ ] rootPath 경계/traversal 차단(경계 위반 정의 주입 → refresh 거부)
- [ ] 로그/Secret에 민감정보 비노출(감사로그·응용로그 샘플 검토)

- [ ] **Step 2: MVP 완료 기준 최종 확인** (`docs/10-testing-and-deployment.md` 원문)

- [ ] Windows Server + IIS 기동
- [ ] MSSQL 기준정보 조회/검증/캐시
- [ ] 설비별 제공 파일 종류 조회
- [ ] 실제 FTP/FTPS 대상 목록/metadata/download
- [ ] 대표 로그 규칙
- [ ] Current Configuration 및 Configuration Snapshot History 규칙
- [ ] API Key/HTTPS
- [ ] 감사로그/Health Check
- [ ] 주요 오류 시나리오
- [ ] 테스트/빌드 성공

- [ ] **Step 3: 차단 항목 처리**

실패/차단 항목은 완료 선언 전에 수정하고 재검증한다. 환경 제약으로 연기가 필요하면 미실행 항목과 이유를 명시한 채 "MVP 완료"가 아닌 "구현 완료, 배포 검증 보류" 상태로 둔다.

---

## Self-Review

**1. Spec coverage** (역할별 문서 요구사항 → Task 매핑):

- 파일명 case-insensitive 비교/중복 충돌/casing 보존: Task 2, 10, 12, 13. ✓
- 시간 규칙([from,to), 기본값, MaxQueryRange≥2일, Daily 00:00, Continuous 거부/시간없음 null): Task 8, 10, 16. ✓
- fileId(24h, resourceKind purpose, 재해석, marker 재확인, 오류 구분): Task 4, 11, 13, 18. ✓
- continuationToken(stateless, 바인딩, limit 변경, 400 통합): Task 11, 13, 16, 17. ✓
- Current(집합, 정렬, 미페이지네이션, 0/1/N 직접다운로드): Task 12, 17. ✓
- History(marker, snapshotTimestamp, 필수 범위, pagination): Task 13, 17. ✓
- catalog(기준정보 투영, EquipmentNotFound/빈 배열, FTP 미접근, DB 추가 자동 반영): Task 15, 19. ✓
- API Key/오류 계약/감사/Health/스트리밍 규칙: Task 14, 16, 18. ✓
- 기준정보 캐시(single-flight, atomic, stale, ReferenceDataUnavailable, 검증만): Task 6, 7. ✓
- FTP Adapter 격리/오류 매핑/동시성 한도: Task 5. ✓
- 키 내구성/rotation: Task 4(동일 key ring 재검증), 20(파일 persist + 재시작 테스트). ✓

갭 확인: `to`-only 거부/`attr.` 필터 case-sensitive/glob 파일명 한정 등 문서 단위테스트 목록의 잔여 항목은 각 Task 테스트에 포함되어 있다(EffectiveRangeTests, LogQueryServiceTests 등). IIS 실서버 검증은 Task 21 수동 게이트로 MVP 완료 조건에 포함했다(자동화 범위 밖임을 명시).

**2. Placeholder scan**: Task 11 `ResolveSingleAsync`, Task 13 서비스 테스트의 `[Fact]` 주석 본문, Task 12/13/17 "구현" 절의 산문 지시는 코드 블록 대신 시나리오를 서술한 형태다. 실행자는 Task 11의 `ListAsync` 전체 구현 패턴과 동일 구조로 작성한다 — 두 service의 공개 시그니처와 분기 규칙은 Interfaces에 완전히 명시되어 있으므로 해석의 여지가 없다. 그 외 "TBD/TODO" 없음.

**3. Type consistency**: `PagedResult<T>`/`MatchCount`/`SingleFileMatch`는 Task 11에서 Core(`FileGateway.Core.Queries`)에 두고 Logs/Configurations/Api가 공유한다(Task 12 Interfaces에 명시). `EffectiveRange`는 Logs Internal → Configurations도 사용하므로 Task 13 이전에 `FileGateway.Core.Time`으로 승격한다(SiteTime과 함께). `FakeFileAccess.TruncateAfterOpen`(Task 18)은 Task 3 헬퍼에 추가한다. 이 두 조정은 Task 12/13/18의 Consumes에 반영했다.

**4. PR 리뷰 반영 (2026-08-23, PR #1)**:

| 리뷰 항목 | 반영 |
|---|---|
| ready 최초 로딩 미유도(P1) | 확정 결정 14, Task 14 ready가 `GetSnapshotAsync` 호출(5s timeout)·테스트 2건 추가 |
| FTPS 구성 누락(P1) | 확정 결정 13, `FtpSecurity`/`AcceptUntrustedCertificates` + `ToFtpConfig` 매핑 테스트, appsettings/README 반영, Task 21에서 실제 FTPS 검증 |
| offset 없는 입력의 Seoul 해석 불일치(P1) | `SiteTime.Parse` 재작성(명시적 offset 판별), 파생 Theory 테스트 4케이스 |
| 감사 실패 status/errorCode 경로(P2) | 확정 결정 15, 순서 `Audit → ErrorMapping → ApiKey`, ErrorMapping이 `Audit.ErrorCode` 기록, 실패 요청 감사 테스트 |
| 계약 잠금의 역할별 문서 미반영(P2) | Task 0(문서별 반영 표)를 모든 구현 Task의 선행 조건으로 추가. `attr.` vs `attribute.` 표기 충돌은 계획 본문에서 설계문서 표기로 정렬 |
| MVP 완료 게이트(P2) | 확정 결정 18 + Task 21 수동 배포 검증 게이트(체크리스트 전체), Task 20은 "구현 완료"로 한정 |
| MSSQL `latest` 태그(P3) | 확정 결정 17, CU 태그 고정 |
| FTP client 조기 dispose(P1, inline) | `OpenReadAsync`가 client/lease를 반환 스트림에 소유, 실패 시 명시적 정리 |
| 동시성 permit 조기 해제(P1, inline) | `FtpLease` 도입 — 스트림 dispose 시 해제, permit 유지 테스트 추가 |
| 로그 중복 판정 scope(P1, inline) | 디렉터리(동일 탐색 결과) 단위로 한정, 교차 디렉터리 동일 basename 정상 테스트 |
| stale cache refresh 대기(P1, inline) | TTL 만료 시 stale 즉시 반환 + single-flight background refresh, 단위테스트 |
| History 하한 경계(P2, inline) | from이 자정이 아니면 그날 자정 Set 제외, 테스트 |
| 연결 후 FTP 명령 오류 매핑(P1, inline) | `WrapAsync`/`Classify`로 전 명령 공통 매핑, MLST 550 → 부재 처리 |
