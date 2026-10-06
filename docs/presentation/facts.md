# facts — FileGateway 소개 설명서 (API 사용자·API 제공자·팀 리더용)

기준일: 2026-10-06

> 관찰 기준: FG = `/home/user/filegateway` (GitHub `hjung3113/FileGateway`) `main` @ `30d89a5` (커밋 날짜 2026-09-04). 코드를 실행하거나 `dotnet test`를 돌리지 않았고 정적으로 읽었다. 출처의 `Lnn`은 해당 커밋의 파일 줄 번호다. 개수는 파일을 직접 세거나 목록과 대조한 값이며 테스트 실행 결과가 아니다.
> 상태 규칙: `implemented` = 코드에서 확인함, `designed` = 설계 문서에만 있음, `planned` = 후속·백로그에만 있음, `n/a` = 원칙·정의·범위처럼 구현 여부가 의미 없는 사실.
> 문서 간 충돌의 처리: `docs/INDEX.md`는 "역할별 문서가 현재 구현 기준"이라고 정한다(`docs/INDEX.md L47`). README·샘플·기존 소개 자료가 역할별 문서나 코드와 다르면 역할별 문서와 코드를 따랐고, 다른 지점은 Q 표에 남겼다.
> 쓰는 말: 이 표의 문장은 아래 T 표의 용어를 쓴다. 원문이 같은 개념을 다른 이름으로 부른 곳도 T 표의 말로 바꿔 적었다.

## F — 사실

### 1. 정체성과 범위

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F001 | FileGateway는 분산 파일 서버에 이미 저장된 설비 로그와 설정 파일(Configuration File)을 조회·다운로드 형태로 제공하는 읽기 전용 게이트웨이다 | — | — | code | implemented | FG@30d89a5:README.md L3; FG@30d89a5:docs/superpowers/specs/2026-08-22-filegateway-design.md L10; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L17-49 | 2026-10-06 | exec,user,dev |
| F002 | API 사용자는 파일 서버 주소나 물리 경로를 몰라도 설비 식별자(equipmentId)와 논리 조회 조건만으로 호출한다 | — | — | code | implemented | FG@30d89a5:README.md L5; FG@30d89a5:docs/01-requirements.md L21-27; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L30-37 | 2026-10-06 | exec,user,dev |
| F003 | FileGateway는 기준정보로 대상 파일 서버와 탐색 규칙을 해석해 FTP/FTPS로 읽는다. 서버가 localhost로 등록되면 같은 머신의 파일시스템에서 직접 읽는다 | — | — | code | implemented | FG@30d89a5:README.md L5; FG@30d89a5:docs/03-server-access-core.md L27-38; FG@30d89a5:src/FileGateway.Api/Program.cs L66-70 | 2026-10-06 | user,dev |
| F004 | 설비 직접 접속, 로그 수집·가공, 설정 파일 이력(History)의 생성·복사·보관은 별도 시스템의 책임이며 FileGateway의 범위가 아니다 | — | — | doc | n/a | FG@30d89a5:README.md L7; FG@30d89a5:docs/01-requirements.md L9, L149-150; FG@30d89a5:AGENTS.md L10 | 2026-10-06 | exec,user,dev |
| F005 | MVP가 제공하는 대상은 설비 로그와 설정 파일이다. 설정 파일은 설비가 실제 동작에 쓰는 파라미터 값이 담긴 파일이며 로그가 아니다 | — | — | doc | implemented | FG@30d89a5:docs/01-requirements.md L7; FG@30d89a5:docs/00-glossary.md L19-21 | 2026-10-06 | exec,user,dev |
| F006 | API 사용자는 사용자용 WPF 데스크톱 앱, Web Backend/BFF, 파일을 받아가야 하는 다른 서버·서비스다. .NET과 Python 등 일반 HTTP 클라이언트로 호출한다 | — | — | doc | n/a | FG@30d89a5:README.md L205-211; FG@30d89a5:docs/01-requirements.md L11-17 | 2026-10-06 | exec,user,dev |
| F007 | 브라우저가 FileGateway API Key를 직접 보유하는 구조는 전제하지 않는다. 웹 애플리케이션은 Backend/BFF를 거쳐 호출한다 | — | — | doc | n/a | FG@30d89a5:README.md L211; FG@30d89a5:docs/01-requirements.md L17 | 2026-10-06 | user,dev |
| F008 | 외부 인터페이스는 HTTPS와 JSON이고, 파일은 streaming download로 내려 준다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L12; FG@30d89a5:docs/05-api-interface.md L5-6; FG@30d89a5:src/FileGateway.Api/Downloading/DownloadResult.cs L9-19 | 2026-10-06 | user,dev |
| F009 | MVP는 ASP.NET Core(.NET 10)를 Windows Server의 IIS에서 운영한다 | 10 | .NET 버전 | code | implemented | FG@30d89a5:docs/01-requirements.md L130-133; FG@30d89a5:docs/02-architecture.md L7; FG@30d89a5:global.json L4; FG@30d89a5:src/FileGateway.Api/FileGateway.Api.csproj L15 | 2026-10-06 | exec,dev |
| F010 | 소스는 다섯 프로젝트(Api, Logs, Configurations, Core, Infrastructure)이고 테스트 프로젝트는 둘(UnitTests, IntegrationTests)이다 | 5 | 프로젝트 | code | implemented | FG@30d89a5:FileGateway.slnx L2-11 | 2026-10-06 | dev |
| F011 | 파일 제공 경로의 계층은 네 층(Api, Logs·Configurations, Core, Infrastructure)이고, 그 아래 외부 자원은 MSSQL과 파일 서버 둘이다. 공식: 층 4 = Api, Logs·Configurations, Core, Infrastructure, 외부 자원 2 = MSSQL, 파일 서버. 입력: F072, F073, F074, F075, F076 | 4 | 층 | derived | implemented | FG@30d89a5:docs/02-architecture.md L24-38; FG@30d89a5:AGENTS.md L17 | 2026-10-06 | dev |

### 2. 설계 원칙

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F012 | 실제 서버 주소와 물리 경로를 외부 API 모델에 노출하지 않는다. 성공 응답, 오류 응답, 헤더 어디에도 담지 않는다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L66; FG@30d89a5:docs/05-api-interface.md L8, L422; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L30-37 | 2026-10-06 | user,dev |
| F013 | 클라이언트 입력으로 파일 시스템·FTP 경로를 직접 만들지 않는다. 모든 접근은 서버의 루트 경로(rootPath) 아래로 정규화한 경로만 쓴다 | — | — | doc | implemented | FG@30d89a5:AGENTS.md L67; FG@30d89a5:docs/05-api-interface.md L12, L418-420; FG@30d89a5:docs/06-reference-data.md L215-224 | 2026-10-06 | user,dev |
| F014 | 파일을 메모리에 통째로 올리지 않고 스트리밍한다. 응답은 스트림 시작 직전에 확인한 크기를 Content-Length로 쓴다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L72; FG@30d89a5:src/FileGateway.Api/Downloading/DownloadResult.cs L11-19; FG@30d89a5:docs/05-api-interface.md L358 | 2026-10-06 | user,dev |
| F015 | 목록 조회와 직접 다운로드는 같은 Resolver 규칙으로 파일을 찾는다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L73; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L12, L32-37 | 2026-10-06 | user,dev |
| F016 | 기준정보 없음, 파일 서버 접근 실패, 경로 없음, 대상 파일 없음을 같은 오류로 뭉개지 않는다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L74; FG@30d89a5:docs/03-server-access-core.md L117-130; FG@30d89a5:src/FileGateway.Core/Errors/FileGatewayErrors.cs L17-30 | 2026-10-06 | user,dev |
| F017 | API Key 원문, FTP credential, 물리 경로, token의 내부 payload는 감사 로그에 남기지 않는다 | — | — | code | implemented | FG@30d89a5:AGENTS.md L75; FG@30d89a5:docs/09-security-and-operations.md L114; FG@30d89a5:src/FileGateway.Api/Audit/AuditMiddleware.cs L6-7 | 2026-10-06 | dev |
| F018 | FileGateway는 저장된 파일을 읽어 제공할 뿐이다. 생산 중 파일의 원자적 교체, 잠금, 내용 일관성은 보정하지 않고 snapshot 복사, 잠금, 버전 고정도 하지 않는다 | — | — | doc | implemented | FG@30d89a5:README.md L633; FG@30d89a5:docs/05-api-interface.md L366; FG@30d89a5:docs/09-security-and-operations.md L157 | 2026-10-06 | user,dev |
| F019 | 기존 계약(Hourly, Daily, Continuous, 현재 설정 파일, 이력)으로 표현할 수 있는 새 로그 종류와 설정 파일 종류는 DB 기준정보를 등록하는 것만으로 노출되고 코드 배포가 필요 없다 | — | — | code | implemented | FG@30d89a5:README.md L171, L196; FG@30d89a5:docs/05-api-interface.md L94; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L33-36 | 2026-10-06 | user,dev |
| F020 | 향후 Linux, 다른 Site·credential, 다른 파일 Provider 확장은 허용하되 MVP에 선구현하지 않는다 | — | — | doc | planned | FG@30d89a5:AGENTS.md L18; FG@30d89a5:docs/07-extension-and-risks.md L5-10, L12-30 | 2026-10-06 | exec,dev |

### 3. 파일 종류

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F021 | 로그 종류(logType)는 업무로 나눈 로그의 분류이고, 생성 유형(generationType)은 파일이 언제 어떻게 만들어지는지를 가르는 분류다. 두 축은 서로 다르다 | — | — | doc | implemented | FG@30d89a5:docs/00-glossary.md L11-17; FG@30d89a5:docs/04a-log-provider.md L37 | 2026-10-06 | user,dev |
| F022 | 생성 유형은 Hourly, Daily, Continuous 세 가지다 | 3 | 가지 | code | implemented | FG@30d89a5:src/FileGateway.Logs/Definitions/Models.cs L6; FG@30d89a5:docs/00-glossary.md L16 | 2026-10-06 | exec,user,dev |
| F023 | Hourly는 시간 또는 시간 범위로 조회하고 같은 시간대에 여러 파일을 허용한다. Daily는 일자 범위로 조회하며 logical timestamp는 그 날짜의 Site local 00:00이다 | — | — | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L183-189; FG@30d89a5:docs/01-requirements.md L67-75 | 2026-10-06 | user,dev |
| F024 | Continuous는 시간 범위 없이 현재 파일만 조회한다. from이나 to가 들어오면 InvalidRequest이고, 명확한 논리 시각이 없으면 timestamp는 null이다 | — | — | code | implemented | FG@30d89a5:docs/04a-log-provider.md L191-197; FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L23-27 | 2026-10-06 | user,dev |
| F025 | 설비 식별자와 로그 종류의 조합(equipmentId + logType)은 로그 정의 하나, 생성 유형 하나에 대응한다. 한 요청에서 한 설비의 로그 종류 여러 개를 함께 탐색하지 않는다 | 1 | 개 | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L33, L235-237; FG@30d89a5:docs/05-api-interface.md L113 | 2026-10-06 | user,dev |
| F026 | 같은 설비·설정 파일 종류 아래 현재 설정 파일이 여러 개(예: PM1~PM4)일 수 있다. 개별 파일을 별도 종류나 subtype으로 쪼개지 않는다 | 4 | 개(PM1~PM4) | doc | implemented | FG@30d89a5:docs/00-glossary.md L24; FG@30d89a5:docs/01-requirements.md L57-58; FG@30d89a5:docs/04b-configuration-provider.md L35 | 2026-10-06 | user,dev |
| F027 | 현재 설정 파일(Current)은 지금 있는 파일 집합이고, 이력(History)은 별도 시스템이 날짜 폴더로 복사해 둔 스냅샷이다. API는 둘을 명시적으로 구분하며 서로를 결과에 넣지 않는다 | — | — | code | implemented | FG@30d89a5:docs/04b-configuration-provider.md L52, L212-217; FG@30d89a5:docs/05-api-interface.md L201, L270; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L17-57 | 2026-10-06 | user,dev |
| F028 | 로그의 logical timestamp는 파일명·경로 규칙에서 뽑은 논리 시각이며 FTP 수정 시각이 아니다. offset 없는 시각은 Asia/Seoul로 해석하고, API는 UTC offset이 붙은 ISO-8601을 쓴다 | — | — | code | implemented | FG@30d89a5:docs/00-glossary.md L51-52; FG@30d89a5:docs/01-requirements.md L101-104; FG@30d89a5:src/FileGateway.Core/Time/SiteTime.cs L17-27 | 2026-10-06 | user,dev |
| F029 | 설정 파일 종류(configurationType)는 설정 파일을 업무 의미로 나누는 안정적인 논리 분류이며 실제 파일명과 분리한다 | — | — | doc | implemented | FG@30d89a5:docs/00-glossary.md L23-25; FG@30d89a5:docs/01-requirements.md L88 | 2026-10-06 | user,dev |

### 4. 엔드포인트

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F030 | /api/v1 아래 엔드포인트는 모두 9개다: equipments, equipments/{equipmentId}/file-types, logs, logs/download, configurations/current, configurations/current/download, configurations/history, files, files/download | 9 | 개 | code | implemented | FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L11, L23; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L17, L28; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L17, L28, L48; FG@30d89a5:src/FileGateway.Api/Endpoints/FileEndpoints.cs L17, L25 | 2026-10-06 | exec,user,dev |
| F031 | /health/live와 /health/ready 두 개가 따로 있고 인증 없이 열린다. API Key 검사는 /api 경로에만 걸린다 | 2 | 개 | code | implemented | FG@30d89a5:src/FileGateway.Api/Endpoints/HealthEndpoints.cs L11, L14; FG@30d89a5:src/FileGateway.Api/Auth/ApiKeyMiddleware.cs L16; FG@30d89a5:docs/09-security-and-operations.md L178 | 2026-10-06 | dev |
| F032 | GET /api/v1/equipments는 검증을 통과한 기준정보의 모든 equipmentId를 오름차순 items로 돌려준다. 파일 서버는 조회하지 않는다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L41-51; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L11-21 | 2026-10-06 | user,dev |
| F033 | GET /api/v1/equipments/{equipmentId}/file-types는 그 설비의 logs(logType, generationType)와 configurations(configurationType)를 돌려준다. 기준정보만 읽고 파일 서버를 스캔하지 않으며, 없는 설비는 404 EquipmentNotFound다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L53-95; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L23-38 | 2026-10-06 | user,dev |
| F034 | GET /api/v1/logs는 equipmentId와 logType이 필수이고 from, to, subtype, attr.<name>, limit, continuationToken을 받는다. 응답은 items와 continuationToken을 담은 JSON이다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L97-113, L160-171; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L25, L53-62 | 2026-10-06 | user,dev |
| F035 | 로그 목록 정렬은 Hourly·Daily가 timestamp 내림차순에 같은 시각이면 fileName 오름차순(대소문자 무시)이고, Continuous는 fileName 오름차순이다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L153-156; FG@30d89a5:docs/04a-log-provider.md L228-235 | 2026-10-06 | user,dev |
| F036 | GET /api/v1/logs/download는 일치 0건이면 404 FileNotFound, 1건이면 단일 파일 스트림, 2건 이상이면 zip 스트림(application/zip, Content-Length 없음)을 돌려준다 | 0, 1, 2 | 건 | code | implemented | FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L38-48; FG@30d89a5:src/FileGateway.Api/Downloading/ZipDownloadResult.cs L20-23; FG@30d89a5:docs/05-api-interface.md L398-409 | 2026-10-06 | user,dev |
| F037 | MultipleFilesMatched(409)는 /logs/download에서 더 이상 나오지 않고, 현재 설정 파일의 직접 다운로드에서만 쓴다 | 409 | 상태 코드 | code | implemented | FG@30d89a5:docs/05-api-interface.md L404; FG@30d89a5:docs/07-extension-and-risks.md L38; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L38-39 | 2026-10-06 | user,dev |
| F038 | GET /api/v1/configurations/current는 현재 설정 파일의 단순 배열을 fileName 오름차순으로 돌려준다. 없으면 200과 빈 배열이고 limit·continuationToken은 쓰지 않는다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L191-215; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L24-25 | 2026-10-06 | user,dev |
| F039 | GET /api/v1/configurations/current/download는 0개 일치면 FileNotFound, 1개면 그 파일, 2개 이상이면 409 MultipleFilesMatched다 | 0, 1, 2 | 개 | code | implemented | FG@30d89a5:docs/05-api-interface.md L217-229; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L36-39 | 2026-10-06 | user,dev |
| F040 | GET /api/v1/configurations/history는 equipmentId, configurationType, from, to가 모두 필수이고 items와 continuationToken을 돌려준다. 개별 스냅샷을 snapshotTimestamp 내림차순, 같은 시각이면 fileName 오름차순으로 나열한다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L231-270; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L67-75, L91-92; FG@30d89a5:docs/04b-configuration-provider.md L219-224 | 2026-10-06 | user,dev |
| F041 | 이력 전용 직접 다운로드 endpoint는 없다. 스냅샷은 목록에서 받은 fileId로 /files/download를 쓴다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L276; FG@30d89a5:docs/04b-configuration-provider.md L226; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L17, L28, L48 | 2026-10-06 | user,dev |
| F042 | GET /api/v1/files?fileId=는 fileId, fileName, size만 돌려주며 실제로 파일 서버에서 stat 한다. HEAD endpoint는 두지 않는다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L287-314; FG@30d89a5:src/FileGateway.Api/Endpoints/FileEndpoints.cs L17-23 | 2026-10-06 | user,dev |
| F043 | GET /api/v1/files/download?fileId=는 파일을 스트리밍한다. Content-Type은 application/octet-stream이고 Content-Disposition은 attachment다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Api/Downloading/DownloadResult.cs L15-17; FG@30d89a5:docs/05-api-interface.md L370-372 | 2026-10-06 | user,dev |
| F044 | fileId는 URL 경로가 아니라 query parameter로 보낸다. 토큰이 260자를 쉽게 넘겨서, 경로로 두면 IIS의 URL 세그먼트 한도에 걸려 요청이 서버에 닿기 전에 거부된다 | 260 | 자 | doc | implemented | FG@30d89a5:docs/05-api-interface.md L293; FG@30d89a5:README.md L481-482, L579-580 | 2026-10-06 | user,dev |
| F045 | 조건만으로 존재를 확인하는 HEAD endpoint는 없다. 조건 기반 존재 확인은 목록 조회를 쓴다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L189, L295 | 2026-10-06 | user,dev |

### 5. 인증

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F046 | 인증은 X-Api-Key 헤더로만 한다. query string으로는 받지 않으며, 헤더가 없거나 값이 맞지 않으면 모두 401 InvalidApiKey다 | 401 | 상태 코드 | code | implemented | FG@30d89a5:src/FileGateway.Api/Auth/ApiKeyMiddleware.cs L16-22; FG@30d89a5:docs/05-api-interface.md L14-24 | 2026-10-06 | user,dev |
| F047 | 여러 API Key를 동시에 켤 수 있고 key마다 callerId가 짝지어진다. MVP에서는 모든 활성 key의 권한 범위가 같다 | — | — | code | implemented | FG@30d89a5:docs/09-security-and-operations.md L16-19; FG@30d89a5:src/FileGateway.Api/Options/AuthenticationOptions.cs L7-14; FG@30d89a5:docs/07-extension-and-risks.md L30 | 2026-10-06 | user,dev |
| F048 | API Key 회전은 overlap 방식이다. 옛 key를 둔 채 새 key를 추가해 재시작·재배포하고, 클라이언트 전환을 확인한 뒤 옛 key를 지워 다시 배포한다 | — | — | doc | implemented | FG@30d89a5:README.md L147-156; FG@30d89a5:docs/09-security-and-operations.md L18 | 2026-10-06 | dev |

### 6. 시간 범위, 페이지, 토큰

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F049 | Hourly·Daily에서 from과 to가 모두 없으면 최근 2일을 조회한다 | 2 | 일 | code | implemented | FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L35-36; FG@30d89a5:docs/01-requirements.md L106 | 2026-10-06 | user,dev |
| F050 | Hourly·Daily에서 from만 있으면 from부터 2일 구간 [from, from+2일)을 조회한다 | 2 | 일 | code | implemented | FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L36; FG@30d89a5:docs/01-requirements.md L107 | 2026-10-06 | user,dev |
| F051 | to만 있거나 from이 to보다 앞서지 않으면 InvalidRequest다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L29-32; FG@30d89a5:docs/01-requirements.md L108-110 | 2026-10-06 | user,dev |
| F052 | 시간 범위는 반개구간 [from, to)다. from은 포함하고 to는 제외한다 | — | — | doc | implemented | FG@30d89a5:docs/00-glossary.md L55-57; FG@30d89a5:docs/05-api-interface.md L117 | 2026-10-06 | user,dev |
| F053 | Logs:MaxQueryRange 기본값은 31일이고 요청 범위가 넘으면 InvalidRequest다. 이 값은 2일 이상이어야 하며 기동 때 검증한다 | 31 | 일 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L20; FG@30d89a5:src/FileGateway.Api/Program.cs L25; FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L37-38; FG@30d89a5:src/FileGateway.Api/appsettings.json L9-11 | 2026-10-06 | user,dev |
| F054 | 이력 조회는 from과 to를 모두 요구한다. Configurations:HistoryMaxQueryRange 기본값은 366일이고 넘으면 InvalidRequest다 | 366 | 일 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L25; FG@30d89a5:src/FileGateway.Api/appsettings.json L12-14; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L91-92 | 2026-10-06 | user,dev |
| F055 | from·to의 UTC offset +09:00은 query string에서 %2B09:00으로 인코딩해야 한다. 그대로 +를 보내면 공백으로 읽혀 파싱에 실패한다 | — | — | doc | implemented | FG@30d89a5:README.md L307 | 2026-10-06 | user,dev |
| F056 | 목록은 limit과 continuationToken으로 페이지를 넘긴다. limit 기본값은 100, 최댓값은 1000이고 최댓값을 넘으면 InvalidRequest다 | 100, 1000 | 건 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L30-31; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L85-92; FG@30d89a5:README.md L128 | 2026-10-06 | user,dev |
| F057 | continuationToken은 서버에 이전 결과를 저장하지 않는 stateless 커서이고 유효기간은 30분이다. 토큰을 쓰는 동안 결과 집합을 바꾸는 조건을 바꾸면 InvalidRequest이고, limit은 바꿔도 된다 | 30 | 분 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L37; FG@30d89a5:docs/05-api-interface.md L175-181 | 2026-10-06 | user,dev |
| F058 | 페이지 사이에 원격 파일이 늘거나 줄면 결과가 달라질 수 있다. 완전한 snapshot은 보장하지 않는다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L179; FG@30d89a5:docs/07-extension-and-risks.md L86-92 | 2026-10-06 | user,dev |
| F059 | fileId 유효기간은 24시간이다 | 24 | 시간 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L36; FG@30d89a5:src/FileGateway.Api/appsettings.json L20; FG@30d89a5:docs/05-api-interface.md L324 | 2026-10-06 | exec,user,dev |
| F060 | fileId는 논리 파일(설비, 종류, 시각, 파일명)을 가리키고 물리 host·경로를 담지 않는다. 접근할 때마다 현재 기준정보로 물리 위치를 다시 계산하므로, 서버나 경로가 바뀌어도 같은 논리 파일이 있으면 기존 fileId가 유효하다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L322-327; FG@30d89a5:docs/00-glossary.md L71-73 | 2026-10-06 | user,dev |
| F061 | fileId 처리 오류는 네 가지로 구분한다. 형식·서명 오류는 400 InvalidFileId, 24시간 경과는 410 FileIdExpired, 기준정보 정의 삭제는 404 LogDefinitionNotFound 또는 ConfigurationDefinitionNotFound, 실제 파일 없음은 404 FileNotFound다 | 4 | 가지 | doc | implemented | FG@30d89a5:docs/05-api-interface.md L380-386; FG@30d89a5:README.md L264-275 | 2026-10-06 | user,dev |
| F062 | 현재 설정 파일의 fileId는 특정 바이트 버전이 아니라 다운로드 시점의 현재 내용을 가리킨다. 과거 버전이 필요하면 스냅샷의 fileId를 쓴다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L356; FG@30d89a5:docs/01-requirements.md L124-126 | 2026-10-06 | user,dev |
| F063 | 다운로드가 시작된 뒤 원격 I/O 오류가 나면 JSON 오류로 바꾸지 않고 스트림을 끊는다. 잘린 파일은 Content-Length와 실제로 받은 바이트 수를 비교해야 알 수 있다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L376; FG@30d89a5:README.md L530 | 2026-10-06 | user,dev |
| F064 | 스트리밍 시작 전의 파일 서버 연결 실패나 프로토콜 오류는 일반 JSON 오류(FileServerUnavailable, FileServerProtocolError)로 돌려준다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L374 | 2026-10-06 | user,dev |
| F065 | 클라이언트가 연결을 끊거나 요청을 취소하면 ClientCancelled로 기록한다. 새 오류 응답의 code가 아니다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L378, L462 | 2026-10-06 | dev |

### 7. 오류 응답

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F066 | 오류 code는 14종이다 | 14 | 종 | code | implemented | FG@30d89a5:src/FileGateway.Core/Errors/FileGatewayErrors.cs L17-30 | 2026-10-06 | exec,user,dev |
| F067 | code와 HTTP 상태: InvalidRequest 400, InvalidFileId 400, InvalidApiKey 401, EquipmentNotFound 404, LogDefinitionNotFound 404, ConfigurationDefinitionNotFound 404, FileNotFound 404, MultipleFilesMatched 409, FileIdExpired 410, FileDefinitionConflict 500, InternalError 500, FileServerUnavailable 502, FileServerProtocolError 502, ReferenceDataUnavailable 503 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Core/Errors/FileGatewayErrors.cs L17-30; FG@30d89a5:README.md L429-442 | 2026-10-06 | user,dev |
| F068 | 오류 body는 type, title, status, code, traceId를 담는다. 분기는 code로 하고 원인 추적은 traceId로 서버 로그와 잇는다. 물리 경로, credential, DB 진단은 담지 않는다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L424-443; FG@30d89a5:README.md L417-444 | 2026-10-06 | user,dev |
| F069 | 각 code의 뜻: InvalidRequest는 요청 파라미터·시간 범위·토큰 조건 오류, InvalidFileId는 fileId 형식·서명 오류, InvalidApiKey는 키 누락·불일치, EquipmentNotFound는 없는 equipmentId, *DefinitionNotFound는 기준정보 삭제로 재해석 불가, FileNotFound는 논리 파일이 실제로 없음, MultipleFilesMatched는 직접 다운로드 조건에 2건 이상, FileIdExpired는 24시간 경과, FileDefinitionConflict는 cardinality 위반·metadata 해석 실패, InternalError는 서버 내부 오류, FileServerUnavailable·FileServerProtocolError는 파일 서버 연결·프로토콜 오류, ReferenceDataUnavailable은 사용 가능한 기준정보 없음 | — | — | doc | implemented | FG@30d89a5:README.md L429-442 | 2026-10-06 | user,dev |
| F070 | FileDefinitionConflict(500)는 사용자 조건이 아니라 기준정보나 파일 상태가 정의와 어긋난 경우다. 생성 슬롯당 파일이 Single인데 둘 이상, 대소문자만 다른 같은 파일명, metadata 해석 실패, 날짜 불일치가 해당한다 | 500 | 상태 코드 | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L92, L157; FG@30d89a5:docs/04b-configuration-provider.md L169-172 | 2026-10-06 | dev |
| F071 | IIS·ARR 단계의 502·503은 JSON이 아니라 HTML이나 빈 body일 수 있다. 클라이언트는 본문이 항상 JSON이라고 가정하면 안 된다 | — | — | doc | n/a | FG@30d89a5:README.md L546, L600 | 2026-10-06 | user,dev |

### 8. 시스템 구조와 파일 접근

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F072 | Api 층은 HTTPS endpoint, API Key 인증, 요청 검증, 감사 로그, Health, JSON·스트리밍 응답을 맡는다. 설비별 제공 종류 catalog는 Logs·Configurations의 정의 요약을 합쳐 만든다 | — | — | doc | implemented | FG@30d89a5:docs/02-architecture.md L40-51 | 2026-10-06 | dev |
| F073 | Logs 층은 로그 조회 정책, LogResolver, 경로·파일명 규칙 해석, 날짜·시간·subtype·attributes 필터, 로그 identity, 페이지 의미를 맡는다 | — | — | doc | implemented | FG@30d89a5:docs/02-architecture.md L53-62; FG@30d89a5:src/FileGateway.Logs/Internal/LogResolver.cs L16 | 2026-10-06 | dev |
| F074 | Configurations 층은 현재 설정 파일 해석, 이력 탐색, 설정 파일 종류 분류, identity, 이력 페이지 의미를 맡고 이력을 만들지는 않는다 | — | — | doc | implemented | FG@30d89a5:docs/02-architecture.md L64-73 | 2026-10-06 | dev |
| F075 | Core 층은 IFileAccess, 원격 파일 모델, 공통 I/O 오류, token codec 계약을 두고 Log, Configuration, FTP, MSSQL, IIS를 알지 못한다 | — | — | doc | implemented | FG@30d89a5:docs/02-architecture.md L75-84 | 2026-10-06 | dev |
| F076 | Infrastructure 층은 MSSQL SP 호출, 기준정보 캐시, FTP/FTPS 어댑터와 localhost 로컬 접근, credential·token key 공급을 맡는다 | — | — | doc | implemented | FG@30d89a5:docs/02-architecture.md L86-92 | 2026-10-06 | dev |
| F077 | IFileAccess 구현은 FtpFileAccess와 LocalFileAccess 둘이고, 앞단의 RoutingFileAccess가 호출마다 서버 host로 하나를 고른다. 상위 계층은 IFileAccess만 안다 | 2 | 개 | code | implemented | FG@30d89a5:docs/03-server-access-core.md L27-44; FG@30d89a5:src/FileGateway.Api/Program.cs L66-70; FG@30d89a5:src/FileGateway.Infrastructure/Ftp/RoutingFileAccess.cs L25-29 | 2026-10-06 | dev |
| F078 | host가 localhost와 정확히 일치할 때만(대소문자 무시, 앞뒤 공백 제거) 로컬 파일시스템으로 간다. 127.0.0.1, ::1, 머신명, FQDN은 모두 FTP로 간다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/Ftp/RoutingFileAccess.cs L28-29; FG@30d89a5:docs/03-server-access-core.md L42 | 2026-10-06 | dev |
| F079 | 로컬 접근의 루트 경로는 로컬 절대 경로여야 하고(상대 경로는 ProtocolError), 경로는 세 겹으로 막는다: `..`·rooted 경로 거부, 루트 하위인지 재확인, symlink·junction 거부 | 3 | 겹 | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/Ftp/LocalFileAccess.cs L134, L153, L159; FG@30d89a5:docs/03-server-access-core.md L48-50 | 2026-10-06 | dev |
| F080 | FTP/FTPS 어댑터는 FluentFTP를 쓰며 Infrastructure 안에 가두고 FluentFTP 타입을 상위 계층에 노출하지 않는다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/FileGateway.Infrastructure.csproj L13; FG@30d89a5:docs/02-architecture.md L100 | 2026-10-06 | dev |
| F081 | FtpClientPool은 전체 한도와 서버별 한도의 permit을 함께 잡고, 같은 Host의 idle FTP 연결을 재사용한다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/Ftp/FtpClientPool.cs L9-11, L144; FG@30d89a5:docs/03-server-access-core.md L66-70 | 2026-10-06 | dev |
| F082 | FTP 보안은 Plain, ExplicitTls, ImplicitTls 중 하나이고 기본은 Plain이다. 인증서 비신뢰 허용(AcceptUntrustedCertificates) 기본값은 false이고, 내부 self-signed 인증서는 이 값을 true로 켜야만 허용한다(운영 판단 필요) | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/Ftp/FtpOptions.cs L5, L11-12; FG@30d89a5:src/FileGateway.Api/appsettings.json L29-30; FG@30d89a5:README.md L133, L163 | 2026-10-06 | dev |
| F083 | FTP 타임아웃은 연결 15초, 읽기 60초이고 동시 접속 상한은 전체 50, 서버별 5다 | 15, 60, 50, 5 | 초, 초, 건, 건 | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/Ftp/FtpOptions.cs L13-16; FG@30d89a5:src/FileGateway.Api/appsettings.json L31-34 | 2026-10-06 | dev |
| F084 | FTP 계정은 DB 결과에 넣지 않고 Secret이나 환경변수로 공급한다. MVP의 모든 파일 서버는 같은 credential을 쓴다 | — | — | doc | implemented | FG@30d89a5:docs/03-server-access-core.md L77, L81; FG@30d89a5:docs/09-security-and-operations.md L24-26; FG@30d89a5:docs/06-reference-data.md L129 | 2026-10-06 | dev |
| F085 | 외부 라이브러리는 FluentFTP(FTP/FTPS)와 Microsoft.Data.SqlClient(MSSQL)만 쓴다. 테스트에서만 Testcontainers.MsSql과 FubarDev.FtpServer를 더하고, MediatR, AutoMapper, Polly, Dapper는 들이지 않는다 | — | — | code | implemented | FG@30d89a5:docs/02-architecture.md L98-107; FG@30d89a5:src/FileGateway.Infrastructure/FileGateway.Infrastructure.csproj L13-14; FG@30d89a5:tests/FileGateway.IntegrationTests/FileGateway.IntegrationTests.csproj L17-20 | 2026-10-06 | dev |

### 9. 기준정보

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F086 | 기준정보는 단일 Stored Procedure FileGateway_GetReferenceData가 result set 4개로 돌려준다: Equipments, Servers, LogDefinitions, ConfigurationDefinitions | 4 | 개 | code | implemented | FG@30d89a5:docs/06-reference-data.md L9-14; FG@30d89a5:db/mvp-stored-procedure.sql L2-21 | 2026-10-06 | dev |
| F087 | Servers는 ServerId, Host, FileRootPath를 담는다. LogDefinitions는 11개 컬럼, ConfigurationDefinitions는 13개 컬럼이며 컬럼 순서는 계약이 아니다 | 11, 13 | 개 | code | implemented | FG@30d89a5:docs/06-reference-data.md L12-14, L18, L133; FG@30d89a5:db/mvp-stored-procedure.sql L5-20 | 2026-10-06 | dev |
| F088 | 로그 정의의 키는 EquipmentId + LogType, 설정 파일 정의의 키는 EquipmentId + ConfigurationType이다. 같은 키가 여러 행이면 충돌한 모든 행을 invalid로 처리하고 하나를 승자로 고르지 않는다 | — | — | code | implemented | FG@30d89a5:db/mvp-schema.sql L13, L25; FG@30d89a5:docs/06-reference-data.md L59, L283; FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L67-70, L83-84 | 2026-10-06 | dev |
| F089 | 모든 정의는 Equipments에 있는 EquipmentId와 Servers에 있는 ServerId를 가리켜야 한다. 아니면 그 정의는 invalid다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L119-127, L178-186 | 2026-10-06 | dev |
| F090 | db/ 아래 SQL은 테스트·개발용 계약 구현이다. 운영 DB의 내부 구조는 이 계약만 지키면 자유롭고, SP는 FTP 비밀번호를 돌려주지 않는다 | — | — | doc | implemented | FG@30d89a5:docs/06-reference-data.md L16, L129; FG@30d89a5:docs/02-architecture.md L146 | 2026-10-06 | dev |
| F091 | 기준정보 캐시는 프로세스 메모리에 두고 CacheTtl 기본값은 15분이다. TTL은 강제 폐기가 아니라 갱신을 다시 시도할 시점이다 | 15 | 분 | code | implemented | FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L42; FG@30d89a5:docs/06-reference-data.md L258-263 | 2026-10-06 | dev |
| F092 | TTL이 지난 뒤 첫 요청이 갱신을 시작하지만, 그 요청은 기다리지 않고 기존 기준정보로 즉시 응답한다. 갱신은 프로세스당 하나만(single-flight) 돈다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs L27-41, L78-85; FG@30d89a5:docs/06-reference-data.md L269-273 | 2026-10-06 | dev |
| F093 | 기동 때 실제 요청 전에 기준정보를 한 번 읽어 둔다. 읽기에 실패해도 프로세스는 시작하고, 정상본이 생길 때까지 /health/ready가 503으로 같은 경로를 재시도한다 | — | — | code | implemented | FG@30d89a5:docs/06-reference-data.md L261; FG@30d89a5:src/FileGateway.Api/Program.cs L78; FG@30d89a5:src/FileGateway.Api/ReferenceData/ReferenceDataWarmupService.cs | 2026-10-06 | dev |
| F094 | 필수 result set·컬럼 shape가 틀리거나 설비·서버 식별자(equipmentId·serverId 중복, 빈 serverId)가 틀리면 전역 검증 실패로 새 데이터를 통째로 거부한다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L30-52; FG@30d89a5:docs/06-reference-data.md L295 | 2026-10-06 | dev |
| F095 | 전역 검증이 실패하고 마지막 정상본이 있으면 그것을 stale 상태로 계속 쓴다. 정상본이 없는 최초 로딩이면 503 ReferenceDataUnavailable이다 | 503 | 상태 코드 | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs L151-159; FG@30d89a5:docs/06-reference-data.md L277-280 | 2026-10-06 | dev |
| F096 | 전역 검증을 통과하면 개별 정의의 검증 실패는 그 정의만 새 기준정보에서 빼고, 나머지 정상 정의로 한 번에(atomic) 교체한다. 이전 기준정보의 정의로 메우지 않는다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L21-22, L54-57, L129-130; FG@30d89a5:docs/06-reference-data.md L267, L278, L281 | 2026-10-06 | dev |
| F097 | 갱신은 구조·문법·불변식만 검증한다. 파일 서버에 접속해 폴더·파일·marker가 실제로 있는지는 확인하지 않고, 목록·다운로드 요청 때 확인한다 | — | — | doc | implemented | FG@30d89a5:docs/06-reference-data.md L293; FG@30d89a5:docs/09-security-and-operations.md L124; FG@30d89a5:README.md L193 | 2026-10-06 | dev |
| F098 | 정의가 빠진 설비·종류는 catalog에 나오지 않고, 그 종류를 직접 조회하면 LogDefinitionNotFound 또는 ConfigurationDefinitionNotFound다 | — | — | doc | implemented | FG@30d89a5:docs/05-api-interface.md L88; FG@30d89a5:docs/09-security-and-operations.md L129 | 2026-10-06 | user,dev |
| F099 | 모든 경로는 서버의 루트 경로(rootPath) 아래로 정규화되어야 한다. `..`이나 절대 경로로 벗어나는 정의는 접근에 쓰지 않고 기준정보 오류로 본다 | — | — | doc | implemented | FG@30d89a5:docs/06-reference-data.md L215-224; FG@30d89a5:docs/09-security-and-operations.md L42-47 | 2026-10-06 | dev |
| F100 | 새 컬럼·모드는 세 단계로 배포한다: (1) 스키마와 신규 SP, (2) 새 result set을 읽는 앱을 전 인스턴스에, (3) 신규 값 활성화. 순서를 어기면 구 앱과 신규 SP 조합이 기준정보 불완전으로 끝난다 | 3 | 단계 | doc | implemented | FG@30d89a5:docs/06-reference-data.md L20, L22-30 | 2026-10-06 | dev |
| F101 | 롤백할 때는 앱을 되돌리기 전에 신규 모드·metadata 값과 regex: 경로를 옛 값으로 비활성화한다 | — | — | doc | implemented | FG@30d89a5:docs/06-reference-data.md L30 | 2026-10-06 | dev |
| F102 | 결정적 파일명 추정(fileNameTemplate)은 선택 설정이다. Single이고 Hourly·Daily인 로그 정의에만 쓸 수 있고, 목록 조회(LIST) 없이 StatFileAsync 한 번으로 파일을 확인한다 | 1 | 번 | code | implemented | FG@30d89a5:docs/04a-log-provider.md L64-74; FG@30d89a5:README.md L198-203; FG@30d89a5:src/FileGateway.Logs/Internal/LogResolver.cs L10-24, L38-63 | 2026-10-06 | dev |
| F103 | 추정이 틀려도 클라이언트 응답은 빈 결과 또는 FileNotFound 그대로다. 계산된 경로는 API 제공자 전용 진단 테이블 dbo.FgFileAccessFailureLog에 기록하며 이 테이블은 API로 노출하지 않는다 | — | — | code | implemented | FG@30d89a5:README.md L202; FG@30d89a5:docs/09-security-and-operations.md L40, L206; FG@30d89a5:db/mvp-schema.sql L26-35 | 2026-10-06 | dev |
| F104 | 설정 파일 정의는 current 규칙과 history 규칙으로 나뉜다. 파일명 일치 방식은 Literal, Glob, Regex 중 하나이고 비어 있으면 Glob이다 | — | — | doc | implemented | FG@30d89a5:docs/04b-configuration-provider.md L54-79; FG@30d89a5:docs/06-reference-data.md L109 | 2026-10-06 | dev |
| F105 | 경로 템플릿은 리터럴과 {yyyy} {MM} {dd} {HH} 토큰을 쓰고, 설정 파일은 regex: 세그먼트도 쓸 수 있다. 날짜는 Asia/Seoul 기준으로 치환한다 | — | — | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L60; FG@30d89a5:docs/04b-configuration-provider.md L70-75 | 2026-10-06 | dev |
| F106 | filePattern은 파일명 전용 glob(*, ?)이다. 파일명 비교는 대소문자를 무시하되 응답에는 원래 대소문자를 남긴다 | — | — | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L62, L76; FG@30d89a5:docs/01-requirements.md L28 | 2026-10-06 | dev |
| F107 | filePattern에 맞아도 필수 metadata를 해석하지 못한 파일은 그 파일만 후보에서 뺀다 | — | — | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L114; FG@30d89a5:docs/05-api-interface.md L187 | 2026-10-06 | dev |
| F108 | 여러 생성 슬롯이 같은 폴더를 계산하면 그 폴더는 한 번만 목록 조회한다. 생성 슬롯과 물리 폴더는 1:1 관계가 아니다 | 1 | 대 1 | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L80; FG@30d89a5:AGENTS.md L71 | 2026-10-06 | dev |
| F109 | cardinality는 생성 슬롯당 파일 수 규칙이다. Single은 슬롯당 최대 1개이고, 그보다 많이 발견되면 FileDefinitionConflict다 | 1 | 개 | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L84-92 | 2026-10-06 | dev |
| F110 | 계산된 폴더가 없으면 정상 결과 0건이며 502가 아니다. 한 요청이 여러 폴더를 읽다 하나라도 FTP I/O 오류가 나면 일부 결과를 성공으로 돌려주지 않고 요청 전체를 실패로 처리한다 | 0 | 건 | doc | implemented | FG@30d89a5:docs/04a-log-provider.md L82; FG@30d89a5:docs/09-security-and-operations.md L137-138; FG@30d89a5:docs/07-extension-and-risks.md L66 | 2026-10-06 | user,dev |

### 10. 설정 파일 이력

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F111 | 별도 시스템이 자정에 현재 설정 파일 집합을 날짜 폴더로 복사해 스냅샷을 만들고, 현재 파일 원본은 그대로 둔다 | — | — | doc | n/a | FG@30d89a5:docs/01-requirements.md L93; FG@30d89a5:docs/04b-configuration-provider.md L138-139 | 2026-10-06 | user,dev |
| F112 | 이력 생산자는 날짜 폴더 복사를 마치면 완료 marker 파일을 만든다. 이름과 위치는 기준정보의 history 규칙이 정하고, FileGateway는 marker가 있는지만 보며 내용은 읽지 않는다 | — | — | code | implemented | FG@30d89a5:docs/04b-configuration-provider.md L143-145; FG@30d89a5:docs/01-requirements.md L95 | 2026-10-06 | user,dev |
| F113 | marker가 있는 날짜 폴더의 스냅샷만 조회 대상이고 복사 중인 폴더는 노출하지 않는다. marker 파일 자체는 결과에 넣지 않는다 | — | — | doc | implemented | FG@30d89a5:docs/04b-configuration-provider.md L146-147 | 2026-10-06 | user,dev |
| F114 | 스냅샷은 만들어진 뒤 바뀌지 않는다. 고칠 일이 있으면 다음 스냅샷에서 새 파일로 반영한다 | — | — | doc | n/a | FG@30d89a5:docs/04b-configuration-provider.md L148 | 2026-10-06 | user,dev |
| F115 | snapshotTimestamp는 metadata 규칙이 없으면 날짜 폴더의 Site local 자정이고, 있으면 파일명에서 뽑은 시각이다. FTP 수정 시각이 아니다 | — | — | doc | implemented | FG@30d89a5:docs/04b-configuration-provider.md L141, L149; FG@30d89a5:docs/00-glossary.md L43-45 | 2026-10-06 | user,dev |
| F116 | 스냅샷 fileId를 다시 열 때도 marker를 다시 확인한다. marker가 사라지면 스냅샷 파일이 남아 있어도 FileNotFound다 | — | — | doc | implemented | FG@30d89a5:docs/04b-configuration-provider.md L197-200; FG@30d89a5:docs/09-security-and-operations.md L74 | 2026-10-06 | user,dev |

### 11. 배포와 운영

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F117 | 비밀은 파일에 두지 않고 환경변수(또는 IIS·Secret 관리 도구)로만 넣는다: Authentication__ApiKeys__0__Key와 CallerId, ConnectionStrings__ReferenceData, FileGateway__Ftp__UserName과 Password, DataProtection__KeyDirectory. 공식: 비밀 4종 = API Key 쌍, 연결 문자열, FTP 계정 쌍, 키 디렉터리 | 4 | 종 | derived | implemented | FG@30d89a5:README.md L138-145 | 2026-10-06 | dev |
| F118 | DataProtection:KeyDirectory가 Development 외 환경에서 비어 있으면 앱이 기동에 실패한다(InvalidOperationException) | — | — | code | implemented | FG@30d89a5:src/FileGateway.Api/Program.cs L111-119; FG@30d89a5:README.md L145 | 2026-10-06 | dev |
| F119 | 키 디렉터리는 App Pool을 재시작해도 남는 경로여야 하고, 키를 잃으면 발급한 모든 fileId가 무효가 된다. Windows에서는 키가 App Pool 계정 범위의 DPAPI로 보호되므로 Load User Profile을 true로 둬야 한다 | — | — | code | implemented | FG@30d89a5:README.md L161; FG@30d89a5:src/FileGateway.Api/Program.cs L52-61 | 2026-10-06 | dev |
| F120 | IIS에는 .NET Hosting Bundle(ASP.NET Core Module V2)을 설치하고 In-process로 호스팅한다 | — | — | code | implemented | FG@30d89a5:README.md L160; FG@30d89a5:src/FileGateway.Api/web.config L5-8 | 2026-10-06 | dev |
| F121 | 감사 로그는 callerId, clientIp, 메서드, 경로, equipmentId, logType, configurationType, fileId, fileName, fileSize, 상태, errorCode, 소요 시간(ms)을 남기고 /health는 기록하지 않는다. 파이프라인 순서는 Audit, ErrorMapping, ApiKey, endpoints다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Api/Audit/AuditMiddleware.cs L23-48; FG@30d89a5:src/FileGateway.Api/Program.cs L121-123; FG@30d89a5:docs/09-security-and-operations.md L98 | 2026-10-06 | dev |
| F122 | /health/live는 프로세스 생존만 보고, /health/ready는 기준정보를 확보했는지 본다. 마지막 정상본이 있으면 stale이어도 200 Degraded(stale true)이고, 정상본이 한 번도 없으면 503 Unhealthy다 | 200, 503 | 상태 코드 | code | implemented | FG@30d89a5:src/FileGateway.Api/Endpoints/HealthEndpoints.cs L11-40; FG@30d89a5:docs/09-security-and-operations.md L178-183; FG@30d89a5:README.md L81-85 | 2026-10-06 | dev |
| F123 | /health/ready는 파일 서버를 순회하지 않는다. 개별 파일 서버 상태는 실제 요청에서 판정한다 | — | — | code | implemented | FG@30d89a5:docs/09-security-and-operations.md L184-185; FG@30d89a5:src/FileGateway.Api/Endpoints/HealthEndpoints.cs L13 | 2026-10-06 | dev |
| F124 | /health/ready의 503은 code·traceId를 담은 Problem Details가 아니라 health 전용 모양이다 | — | — | code | implemented | FG@30d89a5:README.md L85; FG@30d89a5:src/FileGateway.Api/Endpoints/HealthEndpoints.cs L39 | 2026-10-06 | dev |
| F125 | /tester와 /scalar/v1(OpenAPI 문서 뷰어)은 Development 환경이거나 FileGateway:DevTools:Enabled=true일 때만 열린다. 기본값은 false이고, 켜면 접근 가능한 네트워크 범위를 제한해야 한다 | — | — | code | implemented | FG@30d89a5:README.md L87-116; FG@30d89a5:src/FileGateway.Api/Program.cs L130-148 | 2026-10-06 | dev |
| F126 | stale 기준정보를 계속 쓰는 데 최대 시간 제한이 없다. DB 장애가 길어지면 삭제·변경된 정의가 오래 쓰일 수 있어, stale 사용 여부와 마지막 정상 갱신 시각을 관측해야 한다 | — | — | doc | n/a | FG@30d89a5:docs/07-extension-and-risks.md L76-78; FG@30d89a5:docs/06-reference-data.md L285 | 2026-10-06 | dev |
| F127 | 21번 포트만으로는 FTPS 여부를 알 수 없다. 배포 전에 IIS의 FTP SSL Settings와 인증서를 확인해야 하고, 일반 FTP면 내부망에서도 credential과 내용이 평문일 수 있다 | 21 | 포트 | doc | n/a | FG@30d89a5:docs/07-extension-and-risks.md L48-50 | 2026-10-06 | dev |
| F128 | 21번은 제어 연결 포트이고, 목록·다운로드에는 Passive 데이터 포트 범위와 방화벽·NAT 정책이 필요할 수 있다 | 21 | 포트 | doc | n/a | FG@30d89a5:docs/07-extension-and-risks.md L52-54; FG@30d89a5:README.md L162 | 2026-10-06 | dev |
| F129 | MVP에서는 API Key 하나가 전체 설비와 파일에 미친다. 유출 영향이 크므로 key 회전과 감사 로그를 필수 운영 항목으로 둔다 | — | — | doc | n/a | FG@30d89a5:docs/07-extension-and-risks.md L56-58 | 2026-10-06 | dev |
| F130 | FTP 타임아웃·동시성 수치는 실제 환경을 측정한 뒤 확정한다. 기본값은 초기값이다 | — | — | doc | n/a | FG@30d89a5:docs/07-extension-and-risks.md L68-70; FG@30d89a5:docs/09-security-and-operations.md L141-142, L169 | 2026-10-06 | dev |
| F131 | 주요 파일은 대부분 100MB 이하이고, 파일 서버는 수십~수백 대, 동시 다운로드는 수십 건 수준을 설계 전제로 한다 | 100 | MB | doc | n/a | FG@30d89a5:docs/01-requirements.md L144-145; FG@30d89a5:docs/superpowers/specs/2026-08-22-filegateway-design.md L35-36 | 2026-10-06 | exec,dev |

### 12. 범위

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F132 | MVP 제외 항목은 13개다: 설비 직접 접근·로그 수집·가공, 이력 생성·복사·보관, 현재 설정 파일과 Hourly·Daily 로그의 생산 방식 제어, 생산 중 파일의 원자적 교체·잠금·일관성 보장, FileGateway 자체 snapshot 복사·버전 고정, Linux 실제 배포·검증, SMB·SFTP 어댑터, Site별 다중 credential, Range·Resume 다운로드, 설정 파일 직접 다운로드의 여러 파일 자동 ZIP, API Key별 설비·로그 권한, Web UI·WPF 클라이언트 자체 구현, 고가용성·분산 캐시 | 13 | 개 | doc | n/a | FG@30d89a5:docs/01-requirements.md L147-161 | 2026-10-06 | exec,dev |
| F133 | 확정된 MVP 밖 확장은 Linux 배포, 다른 Site·credential, 다른 파일 프로토콜(SMB·SFTP), 권한 세분화, Range·Resume, 다중 파일 다운로드, 다중 discovery rule이다. 실제 요구가 생길 때 판단하며 지금은 구현하지 않는다 | — | — | doc | planned | FG@30d89a5:docs/07-extension-and-risks.md L12-44; FG@30d89a5:AGENTS.md L18 | 2026-10-06 | exec,dev |
| F134 | Range·Resume은 주요 파일이 대부분 100MB 이하라 MVP에서 제외한다. 설정 파일의 여러 파일 자동 ZIP은 필요할 때 별도 요구사항으로 설계한다 | 100 | MB | doc | planned | FG@30d89a5:docs/07-extension-and-risks.md L32-38 | 2026-10-06 | dev |

### 13. 구현 현황과 배포 확인

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F135 | MVP 구현이 완료되어 통합 검증 단계를 거쳤다(README의 표현: 단위·통합 테스트 전 통과). 배포는 "배포 전 필수 확인" 목록을 통과해야 MVP 완료로 간주한다 | — | — | doc | implemented | FG@30d89a5:README.md L11; FG@30d89a5:docs/10-testing-and-deployment.md L199-201 | 2026-10-06 | exec,dev |
| F136 | 자동화 게이트(build·test)는 구현 완료 조건일 뿐 MVP 완료가 아니다. MVP 완료는 Windows Server + IIS + 실제 MSSQL + 파일 서버(FTP/FTPS) 환경에서 사람이 하는 배포 검증까지 통과해야 한다 | — | — | doc | implemented | FG@30d89a5:docs/10-testing-and-deployment.md L201; FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L3; FG@30d89a5:docs/superpowers/plans/2026-08-23-filegateway-mvp.md L41, L4342 | 2026-10-06 | exec,dev |
| F137 | 자동화 테스트는 단위·통합 프로젝트 둘로 나뉜다. 통합 테스트는 Testcontainers MSSQL 컨테이너와 테스트용 FTP 서버(FubarDev.FtpServer)를 쓰며, 실제 Windows Server·IIS·운영 유사 FTP/FTPS 검증을 대체하지 않는다 | 2 | 개 | code | implemented | FG@30d89a5:docs/10-testing-and-deployment.md L143-149; FG@30d89a5:FileGateway.slnx L9-11; FG@30d89a5:tests/FileGateway.IntegrationTests/FileGateway.IntegrationTests.csproj L17-20 | 2026-10-06 | dev |
| F138 | 배포 전 필수 확인은 20개 항목이다 | 20 | 개 | doc | planned | FG@30d89a5:docs/10-testing-and-deployment.md L178-197 | 2026-10-06 | exec,dev |
| F139 | 배포 검증 체크리스트는 Step 1(배포 전 필수 확인) 19행과 Step 2(MVP 완료 기준) 10행으로, 항목마다 통과·차단과 원인을 기록한다. 전 항목이 통과해야 MVP 완료를 선언한다 | 19, 10 | 행 | doc | planned | FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L6, L11-31, L35-46 | 2026-10-06 | exec,dev |
| F140 | 20번째 항목(fileNameTemplate을 설정하면 LIST 없이 확정되는지, 추정 실패가 진단 테이블에 기록돼도 응답이 정상인지)은 배포 검증 체크리스트 Step 1에 없다. 공식: 20 - 19 = 1. 입력: F138, F139 | 1 | 항목 | derived | n/a | FG@30d89a5:docs/10-testing-and-deployment.md L197; FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L13-31 | 2026-10-06 | dev |
| F141 | 환경 제약으로 일부를 미룰 때는 "MVP 완료"가 아니라 "구현 완료, 배포 검증 보류"로 기록하고 미실행 항목, 사유, 재검증 예정일을 남긴다 | — | — | doc | n/a | FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L6, L50-56 | 2026-10-06 | exec,dev |
| F142 | 저장소의 배포 검증 체크리스트 사본에는 통과·차단을 표시한 항목이 하나도 없다(Step 1·2 모두 미표시) | 0 | 개 | measured | n/a | FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L13-46 | 2026-10-06 | exec,dev |
| F143 | HANDOFF는 자동화 구현(작업 0~20번)이 모두 끝났고 남은 일은 실제 환경에서 사람이 하는 배포 검증(작업 21번) 하나뿐이라고 적는다. 작성 시점은 세션 #13(2026-09-03)이다 | — | — | doc | planned | FG@30d89a5:HANDOFF.md L207-211, L249 | 2026-09-03 | exec,dev |
| F144 | HANDOFF는 남은 open issue로 #12(FTP localhost 조회 502, PASV 데이터채널 의심, 코드 버그)와 #13(HTTPS 서버 인증서 확보, 인프라 단계)을 적는다. 작성 시점은 2026-09-03이다 | — | — | doc | planned | FG@30d89a5:HANDOFF.md L24, L48 | 2026-09-03 | exec,dev |
| F145 | 클라이언트 샘플은 Python(requests)과 C#(HttpClient) 두 언어로 8개 유스케이스를 담는다 | 8 | 개 | code | implemented | FG@30d89a5:samples/README.md L16-25; FG@30d89a5:samples/python/scenarios; FG@30d89a5:samples/csharp/Scenarios | 2026-10-06 | user,dev |

### 14. 보충

| id | 사실 | 값 | 단위 | 종류 | 상태 | 출처 | as-of | 독자 |
|---|---|---|---|---|---|---|---|---|
| F146 | equipmentId는 API 사용자와 기준정보가 함께 쓰는 안정적인 논리 설비 식별자다. 표시명과 다르고 한 배포 범위 안에서 유일하며, 표시명이 바뀌어도 같은 설비를 가리킨다 | — | — | doc | implemented | FG@30d89a5:docs/00-glossary.md L7-9; FG@30d89a5:docs/01-requirements.md L22 | 2026-10-06 | user,dev |
| F147 | 설비사나 설비 종류에 따라 제공 파일이 달라도 equipmentId별 기준정보의 차이로 표현한다. 설비사 전용 query parameter나 코드 분기를 만들지 않는다 | — | — | code | implemented | FG@30d89a5:docs/05-api-interface.md L93; FG@30d89a5:docs/01-requirements.md L38; FG@30d89a5:README.md L295; FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L30-37 | 2026-10-06 | user,dev |
| F148 | 현재 설정 파일 조회는 시간 필터를 쓰지 않는다 | — | — | doc | implemented | FG@30d89a5:docs/01-requirements.md L113; FG@30d89a5:docs/05-api-interface.md L200 | 2026-10-06 | user,dev |
| F149 | 갱신이 실패하면 마지막 성공 시각이 그대로라서, 이후 요청이 single-flight로 갱신을 계속 다시 시도한다. 성공하면 기준정보를 교체하고 실패 기록을 지운다 | — | — | code | implemented | FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs L29-33, L117-122, L151-158 | 2026-10-06 | dev |
| F150 | MVP 완료 기준은 10개다: Windows Server + IIS 기동, MSSQL 기준정보 조회·검증·캐시, 설비별 제공 파일 종류 조회, 실제 FTP/FTPS 대상 목록·metadata·download, 대표 로그 규칙, 현재 설정 파일과 이력 규칙, API Key/HTTPS, 감사 로그와 Health, 주요 오류 시나리오, 테스트·빌드 성공 | 10 | 개 | doc | planned | FG@30d89a5:docs/10-testing-and-deployment.md L203-214; FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L35-46 | 2026-10-06 | exec,dev |
| F151 | 배포 전 필수 확인 20개 항목: (1) HTTPS 인증서·바인딩, (2) IIS ASP.NET Core Hosting Bundle·권한, (3) 여러 X-Api-Key 인증·호출자 구분과 query string key 비허용, (4) API Key 신/구 overlap 회전, (5) MSSQL 연결, (6) 설비별 제공 파일 종류 API가 DB 기준정보와 일치하고 파일 서버 접근 없이 동작, (7) 기준정보 구조 검증·atomic 교체·stale fallback·single-flight 동작, (8) 기준정보 갱신이 파일 서버 실재 검사를 하지 않음, (9) 각 파일 서버 21번 제어 연결, (10) IIS FTP SSL 설정(FTP 또는 FTPS), (11) Passive 데이터 포트 범위·방화벽, (12) 실제 파일 목록·다운로드, (13) 여러 생성 슬롯이 같은 물리 폴더를 쓰는 로그 탐색, (14) 폴더 없음·파일 서버 장애·일부 FTP 실패의 구분, (15) FTP 전체·서버별 동시성 제한, (16) 설정 파일 이력 완료 marker 존재 조건과 스냅샷 fileId 재검증, (17) token 보호 key 재시작 내구성과 rotation 때 기존 fileId 유지, (18) rootPath 경계·traversal 차단, (19) 로그와 Secret에 민감정보 비노출, (20) fileNameTemplate 설정 시 LIST 없이 StatFileAsync로 확정되는지와 추정 실패가 진단 테이블에 기록돼도 응답이 정상인지 | 20 | 개 | doc | planned | FG@30d89a5:docs/10-testing-and-deployment.md L178-197 | 2026-10-06 | exec,dev |

## T — 용어

| 용어 | 뜻 | 처음 나올 때 | 쓰지 않을 말 |
|---|---|---|---|
| FileGateway | 분산 파일 서버의 설비 로그와 설정 파일을 읽기 전용으로 내주는 게이트웨이 | FileGateway(읽기 전용 파일 제공 게이트웨이) | File Gateway, 파일 게이트웨이 |
| API 사용자 | FileGateway를 호출하는 클라이언트 개발자와 그 프로그램(WPF 앱, Web Backend/BFF, 다른 서버) | API 사용자(클라이언트 개발자) | API 소비자, 소비자, Consumer |
| API 제공자 | FileGateway를 배포하고 기준정보와 파일 서버를 등록·운영하는 사람 | API 제공자(운영자) | 운영자, 서버 관리자, 기준정보 담당자 |
| 설비 | 로그와 설정 파일을 만들어 내는 생산 설비 한 대. equipmentId로 구분한다 | 설비 | 장비, 기기 |
| equipmentId | 설비를 가리키는 안정적인 논리 식별자. 표시명과 다르고 배포 범위 안에서 유일하다 | 설비 식별자(equipmentId) | 설비 ID, Equipment ID, 설비명 |
| logType | 업무로 나눈 로그의 분류. 생성 유형과 다른 축이다 | 로그 종류(logType) | Log Type, 로그 타입 |
| generationType | 로그 파일이 언제 어떻게 만들어지는지를 가르는 분류. 값은 Hourly, Daily, Continuous다 | 생성 유형(generationType) | 생성 정책, 로그 생성 정책, 생성 주기, Generation Type |
| configurationType | 설정 파일을 업무 의미로 나눈 분류. 실제 파일명과 별개다 | 설정 파일 종류(configurationType) | 구성 종류, Configuration Type |
| 설정 파일 | 설비가 실제 동작에 쓰는 파라미터 값이 담긴 파일. 로그가 아니다 | 설정 파일(Configuration File) | 설정파일, 구성 파일, 컨피그, Configuration File, Configuration 로그 |
| 현재 설정 파일 | 설비가 지금 쓰는 설정 파일. 한 설정 파일 종류에 여러 개일 수 있고 API 경로는 /configurations/current다 | 현재 설정 파일(Current) | 현재 설정파일, 현재 구성 파일, Current Configuration |
| 설정 파일 이력 | 별도 시스템이 날짜 폴더로 복사해 둔 스냅샷의 모음. API 경로는 /configurations/history다 | 설정 파일 이력(History) | 히스토리, Configuration History, 설정 히스토리, 변경 이력 |
| 스냅샷 | 이력에서 한 시점에 복사된 개별 설정 파일. 복사가 끝난 뒤 바뀌지 않는다 | 스냅샷(Snapshot) | 스냅숏, 히스토리 파일 |
| 완료 marker | 날짜 폴더 복사가 끝났음을 알리는 파일. 이름과 위치는 기준정보가 정하고 FileGateway는 있는지만 본다 | 완료 marker(복사 완료를 알리는 파일) | 완료 마커, 마커, completion marker, 완료 표지 |
| 기준정보 | MSSQL에 등록한, 어떤 설비가 어떤 파일 서버의 어떤 규칙으로 파일을 내놓는지에 대한 정의 모음 | 기준정보(Reference Data) | 레퍼런스 데이터, 참조 데이터, 마스터 데이터 |
| 로그 정의 | 기준정보에서 한 설비의 로그 종류 하나를 어떤 서버와 규칙으로 내놓을지 적은 한 건 | 로그 정의(Log Definition) | 로그정의, 로그 설정 |
| 설정 파일 정의 | 기준정보에서 한 설비의 설정 파일 종류 하나를 어떤 서버와 규칙으로 내놓을지 적은 한 건 | 설정 파일 정의(Configuration Definition) | 구성정의, 구성 정의, 설정정의 |
| 파일 서버 | 로그와 설정 파일이 실제로 저장된 서버. FTP/FTPS로 접근하며, localhost로 등록하면 같은 머신의 디스크를 직접 읽는다 | 파일 서버 | 파일 저장소, 원격 서버, 스토리지, 원격 저장소 |
| rootPath | 파일 서버에서 FileGateway가 접근할 수 있는 최상위 폴더. 모든 경로는 이 아래에 있어야 한다 | 루트 경로(rootPath) | root 경로, 루트 폴더, 루트 디렉터리 |
| 물리 경로 | 파일 서버 안의 실제 폴더·파일 경로. API 사용자는 알 필요가 없다 | 물리 경로 | 실제 경로, 물리적 경로, 실경로 |
| 논리 조회 조건 | 설비 식별자, 로그 종류, 시간 범위처럼 물리 위치와 무관한 조회 조건 | 논리 조회 조건 | 논리 조건, 논리적 조회 조건 |
| 탐색 규칙 | 기준정보가 정하는, 파일을 찾을 폴더와 파일명의 규칙 | 탐색 규칙(discovery rule) | 탐색규칙, 검색 규칙, 발견 규칙 |
| 생성 슬롯 | Hourly는 한 시간, Daily는 하루로 나눈 논리 구간. 물리 폴더와 1대 1이 아니다 | 생성 슬롯 | 시간 슬롯, 논리 슬롯, 논리 생성 슬롯 |
| fileId | 논리 파일 하나를 가리키는, 24시간 유효한 opaque 토큰. 물리 경로를 담지 않는다 | fileId(24시간 유효한 opaque 토큰) | 파일 ID, File ID, 파일 아이디 |
| continuationToken | 목록의 다음 페이지를 가리키는 stateless 커서 | continuationToken(다음 페이지 커서) | 페이지 토큰, Continuation Token, continuation token |
| 직접 다운로드 | 목록 조회 없이 조건만으로 파일을 바로 받는 방식 | 조건 기반 직접 다운로드 | 조건부 다운로드, 조건부 직접 다운로드, 원스텝 |
| API Key | 호출하는 쪽을 가려내는 인증 값. X-Api-Key 헤더로만 보낸다 | API Key | API 키, 인증 키 |
| callerId | API Key와 짝을 이루는 호출자 식별자. 감사 로그에 남는다 | 호출자 식별자(callerId) | 호출자 ID, Caller ID |
| 마지막 정상본 | 기준정보 갱신이 실패했을 때 계속 쓰는, 마지막으로 검증을 통과한 기준정보 | 마지막 정상본(last-known-good) | LKG, 마지막 정상 캐시, 직전 정상본 |
| 감사 로그 | 요청마다 남기는 기록(호출자, 설비, 결과 상태, 소요 시간) | 감사 로그 | 감사로그, Audit Log |
| fileNameTemplate | 파일명이 시간을 담는 고정 포맷일 때, 목록 조회 없이 파일을 바로 확인하게 하는 선택 설정 | 결정적 파일명 추정(fileNameTemplate) | 파일명 템플릿, 파일명 예측 |
| 반개구간 | [from, to) 형태로 from은 포함하고 to는 제외하는 시간 범위 | 반개구간 [from, to) | 반열린 구간, half-open |
| 배포 전 필수 확인 | 배포 전에 확인할 20개 항목(docs/10 기준). 항목 정본이다 | 배포 전 필수 확인 | 배포 전 점검, 사전 점검 |
| 배포 검증 체크리스트 | 배포 전 필수 확인과 MVP 완료 기준의 결과를 항목별로 통과·차단으로 적는 기록 문서 | 배포 검증 체크리스트 | 수동 배포 검증, Task 21, MVP 완료 게이트 |
| MVP 완료 기준 | 배포 검증 체크리스트의 두 번째 단계 10개 항목. 전부 통과해야 MVP 완료다 | MVP 완료 기준 | MVP 완료 조건, 완료 게이트 |

## Q — 열린 질문

| id | 질문 | 필요 섹션 | 누구 | 상태 |
|---|---|---|---|---|
| Q01 | README의 "API 제공자 가이드"와 기존 소개 자료는 "정의가 1건이라도 검증에 실패하면 refresh 전체를 거부하고 last-known-good을 유지한다"고 적는다. 06 문서(L267, L278, L281, L295)와 코드(ReferenceDataSnapshotBuilder.cs L21-22, L54-57)는 개별 정의만 빼고 나머지로 교체한다. 이 계획은 06 문서와 코드를 따랐다. README를 고쳐야 하는가 | s11 | FileGateway 문서 담당 | open |
| Q02 | README L380·L228과 기존 소개 자료, 클라이언트 샘플(samples/README L21, python 04번)은 /logs/download가 2건 이상 일치하면 409 MultipleFilesMatched를 돌려준다고 적는다. docs/05 L398-409와 코드(LogEndpoints.cs L38-48)는 zip 스트림이고 409는 현재 설정 파일 직접 다운로드에서만 나온다. 이 계획은 docs/05와 코드를 따랐다. README와 샘플을 고쳐야 하는가 | s5, s8 | FileGateway 문서 담당 | open |
| Q03 | 배포 검증 체크리스트 Step 1은 19행이고 docs/10의 배포 전 필수 확인은 20개다(fileNameTemplate 항목이 체크리스트에 없다). 체크리스트가 근거 문서로 docs/10을 가리키므로(DEPLOYMENT-CHECKLIST L5) 이 계획은 20개를 따랐다. 체크리스트에 20번째 행을 추가할 것인가 | s15 | FileGateway 문서 담당 | open |
| Q04 | HANDOFF(2026-09-03 기준)가 남은 open issue로 적은 #12(FTP localhost 조회 502)와 #13(HTTPS 서버 인증서 확보)이 2026-10-06 현재도 열려 있는가. #12는 localhost 로컬 접근(2026-08-30)으로 사라졌는가. 이 계획의 현황 섹션은 두 이슈를 싣지 않았다 | s14 | 저장소 소유자 | open |
| Q05 | 실제 Windows Server·IIS 환경에서 사람이 하는 배포 검증을 시작했는가. 저장소의 체크리스트 사본은 모두 비어 있고(F142) HANDOFF는 2026-09-03 시점에 미착수라고 적는다. 시작했다면 통과·차단 항목 수를 owner 사실로 받아야 현황 섹션의 진척을 쓸 수 있다 | s14, s15 | 저장소 소유자 | open |
| Q06 | 기준 커밋 30d89a5(결정적 파일명 추정 #45 포함)에서 dotnet build·test를 돌린 결과가 문서에 없다. README는 "전 통과"만 쓰고, HANDOFF의 457/457은 #43 병합 시점(2026-09-03)의 값이다. 현황 섹션에 테스트 통과 수를 실을 것인가, 싣는다면 누가 다시 돌리는가 | s14 | 저장소 소유자 | open |
| Q07 | 도입 배경(현재 클라이언트가 파일 서버에 직접 접속하며 겪는 문제)을 적은 문서가 없다. 기존 소개 자료의 "문제점" 네 가지(경로 지식 분산, FTP 계정 다중 보유 등)는 근거 문서가 없어 계획에 넣지 않았다(has-as-is: false). 배경 섹션을 넣으려면 owner 진술이 필요하다 | s1 | 저장소 소유자 | open |
| Q08 | 실제 규모(운영 설비 수, 파일 서버 수, 호출하는 시스템 수, 하루 호출량)가 문서에 없다. "파일 서버 수십~수백 대, 동시 다운로드 수십 건"은 설계 전제(F131)다. 규모 숫자를 owner 사실로 받을 수 있는가 | s1, s14 | 저장소 소유자 | open |
| Q09 | API 사용자가 API Key를 누구에게 어떻게 신청하는지(절차, 담당 창구)가 문서에 없다. README 호출 순서는 "API Key 발급받기"까지만 적는다. 신청 절차를 owner 사실로 받아 s5에 넣을 것인가 | s5 | 저장소 소유자 | open |
| Q10 | 운영 호출 주소(gateway.example은 예시 값)와 배포 대상 호스트가 문서에 없다. 실제 주소를 이 설명서에 실을 것인가, 비워 둘 것인가 | s5, s13 | 저장소 소유자 | open |
| Q11 | 문서가 같은 개념을 "생성 유형"(01 문서, 용어집 Generation Type), "생성 정책"(README, 04a 제목), "생성 주기"(용어집 설명)로 섞어 쓴다. 이 계획은 "생성 유형(generationType)"으로 통일했다. 이 선택을 확정하는가 | 전체(T 표) | 저장소 소유자 | open |
| Q12 | 후속 확장(Linux, SMB·SFTP, 다른 Site, API Key별 권한)에 일정이 없다. 범위 표는 일정 없이 "필요해질 때"로만 쓴다. 일정이 정해져 있는가 | s2 | 저장소 소유자 | open |
| Q13 | 결정적 파일명 추정이 확정하지 못했을 때 코드는 사유를 FileNotFound, MetadataMismatch, FilePatternMismatch 세 가지로 기록한다(LogResolver.cs L10). docs/09 L206은 앞 두 가지만 적는다. 이 설명서는 사유 이름을 싣지 않았다. docs/09를 고칠 것인가 | s11 | FileGateway 문서 담당 | open |

## C — 주장

| id | 주장 | 근거 F-ids | 한계 |
|---|---|---|---|
| C01 | FileGateway는 읽기 전용이며 설비 직접 접속, 로그 수집·가공, 설정 파일 이력 생성은 하지 않는다 | F001, F004, F018, F132 | 이 범위는 문서와 코드로 확인했다. 별도 시스템이 실제로 어떻게 돌고 있는지는 이 저장소에 없다 |
| C02 | API 사용자는 설비 식별자와 논리 조회 조건만으로 호출하고, 파일 서버 주소와 물리 경로는 응답 어디에도 나오지 않는다 | F002, F012, F013, F060 | 응답 필드는 코드로 확인했으나 모든 오류 경로를 실행해 본 것은 아니다 |
| C03 | 기존 계약 안의 새 로그 종류와 설정 파일 종류는 기준정보 등록만으로 노출된다 | F019, F033, F096 | 계약으로 표현할 수 없는 새 종류는 코드 변경이 필요하다 |
| C04 | 기준정보 갱신이 실패해도 마지막 정상본으로 계속 서비스하며, 정의 한 건의 위반은 그 정의만 제외한다 | F092, F094, F095, F096, F122 | 정상본의 유효 기간 상한이 없다(F126) |
| C05 | MVP 구현은 끝났고, 실제 환경의 배포 전 필수 확인 20개 항목이 남았다 | F135, F136, F138, F142, F143 | 확인이 시작됐는지는 알 수 없다(Q05). 상태 근거인 HANDOFF는 2026-09-03 시점이다 |
