---
doc-type: explainer
audience: developer
reader-action: 자기 시스템에서 FileGateway를 호출할 수 있는지 판단하고, 배포 전 필수 확인을 시작하며, 구현 상태와 남은 확인을 파악한다
has-as-is: false
metrics-mode: partial
act-structure: act-grouped
narrative-lens: use-case-first
source-ref: FG@30d89a5 (GitHub hjung3113/FileGateway main, 커밋 2026-09-04) — README.md, AGENTS.md, docs/INDEX.md, docs/00-glossary.md ~ docs/10-testing-and-deployment.md, docs/DEPLOYMENT-CHECKLIST.md, HANDOFF.md, db/*.sql, samples/README.md, src/, tests/ — 항목별 출처는 facts.md
labels: ko
eyebrow: 설명서 · API 사용자와 API 제공자용
title: FileGateway 안내 — 설비 파일을 논리 조회 조건으로 내려받는 읽기 전용 게이트웨이
thesis: FileGateway(읽기 전용 파일 제공 게이트웨이)는 분산 파일 서버에 이미 저장된 설비 로그와 설정 파일(Configuration File)을 조회·다운로드 형태로 내준다. API 사용자(클라이언트 개발자)는 파일 서버 주소와 물리 경로를 몰라도 설비 식별자(equipmentId)와 논리 조회 조건만으로 호출한다. 구현은 끝났고, API 제공자(운영자)가 배포 전 필수 확인 20개 항목을 통과해야 MVP가 완료된다. [F001][F002][F135][F138]
cover-tokens: MVP=구현 완료, 배포 전 필수 확인 대기 [F135] ; 9개=API 엔드포인트 [F030] ; 3종=로그 생성 유형 [F022] ; 20개=배포 전 필수 확인 항목 [F138]
facts: facts.md
as-of: 2026-10-06
---

<!--
act-plan — /build가 구분선 div를 만들 때 쓰는 메모다(구분선은 section이 아니다). 계획은 section 15개 + 부록 1개다.
막 1 · 개요 (nav 개요): s1 s2 s3 — 누가 쓰고 무엇을 하며 무엇을 하지 않는지
막 2 · API 사용자 (nav 사용자): s4 s5 s6 s7 s8 — 호출 순서, 흐름, 규칙, 오류
막 3 · API 제공자 (nav 제공자): s9 s10 s11 s12 s13 — 구조, 기준정보, 갱신, 이력 인계, 설정
막 4 · 현황과 남은 일 (nav 현황): s14 s15 — 구현 상태와 배포 전 필수 확인
선택 근거 한 줄: doc-type은 explainer다. 소개·구조·흐름·상태·범위가 내용의 중심이고, feature-guide가 전제하는 화면(ui-surface)이 FileGateway에는 없다.
-->

## 1. 개요 — 두 독자가 쓰는 하나의 입구
- intent: FileGateway를 누가 어떤 일로 쓰는지 한눈에 보이고, 범위 밖의 일을 한 문장으로 못 박는다
- shape: actor-goals
- payload: 리드: FileGateway(읽기 전용 파일 제공 게이트웨이)는 분산 파일 서버에 저장된 설비 로그와 설정 파일을 조회·다운로드로 내준다 [F001]. 호출하는 쪽은 설비 식별자와 논리 조회 조건만 안다. 파일 서버 주소와 물리 경로는 몰라도 된다 [F002]. 쓰는 사람은 둘이다. API 사용자는 WPF 앱, Web Backend/BFF, 파일을 받아가는 다른 서버·서비스이고 .NET이나 Python 같은 일반 HTTP 클라이언트로 호출한다 [F006]. 브라우저는 API Key를 직접 갖지 않고 Backend/BFF를 거친다 [F007]. API 제공자는 기준정보(Reference Data)와 파일 서버를 등록하고 배포·운영한다 [F086][F087][F120]. 그림의 목표와 근거는 다음과 같다. 설비 조회는 GET /api/v1/equipments다 [F032]. 제공 종류 확인은 file-types다 [F033]. 로그 조회는 /logs다 [F034]. 설정 파일 조회는 /configurations/current와 /history다 [F038][F040]. 파일 다운로드는 조건 기반 직접 다운로드와 /files/download다 [F036][F043]. 기준정보 등록 [F086]. 파일 서버 등록은 Servers의 Host와 FileRootPath다 [F087]. API Key 회전 [F048]. IIS 배포 [F120]. 상태 점검은 Health와 감사 로그다 [F121][F122]. 콜아웃 KEY(한 문장): 설비 직접 접속, 로그 수집·가공, 설정 파일 이력(History) 생성은 별도 시스템의 책임이며 FileGateway의 범위가 아니다 [F004]. 작성 메모(본문에 쓰지 않음): 도입 배경(현재 클라이언트가 FTP에 직접 접속하는 문제)은 근거 문서가 없어 넣지 않는다 [Q07]. 실제 운영 규모(설비 수, 파일 서버 수)도 문서에 없다 [Q08].
- figure-data: system: FileGateway | actors: API 사용자: 설비 조회, 제공 종류 확인, 로그 조회, 설정 파일 조회, 파일 다운로드 ‖ API 제공자: 기준정보 등록, 파일 서버 등록, API Key 회전, IIS 배포, 상태 점검
- source-span: FG@30d89a5:README.md L3-11, L50-51, L169-171, L205-211; FG@30d89a5:docs/01-requirements.md L11-17; [F001] [F002] [F004] [F006]

## 2. 범위 — 하는 일과 하지 않는 일
- intent: 범위 밖(별도 시스템의 책임)과 MVP 제외, 후속 확장을 가르고, 빠진 기능처럼 읽히지 않게 한다
- shape: text-table
- payload: 리드: 범위 밖의 일은 설계상 다른 시스템이 맡는 일이다. FileGateway가 못 하는 일이 아니다 [F004]. 별도 시스템이 파일을 만들어 파일 서버에 저장하고, FileGateway는 저장소에 보이는 파일을 읽어 제공한다 [F001][F018]. MVP 제외 항목은 13개이고 표는 이를 묶음별로 보인다 [F132]. 후속 확장은 실제 요구가 생길 때 판단하며 지금은 구현하지 않는다. 계획 어조로만 쓰고 일정은 싣지 않는다 [F020][F133]. 지금은 활성 API Key의 권한 범위가 모두 같다 [F047]. Range와 Resume은 주요 파일이 대부분 100MB 이하라 뺐다. 설정 파일의 여러 파일 자동 ZIP은 필요할 때 별도 요구사항으로 설계한다 [F134]. 클라이언트와 Web UI는 API 사용자가 만들고 기준정보 캐시는 프로세스 메모리 하나다 [F132][F091]. 처음 나올 때 형태: 설정 파일 이력(History), 스냅샷(Snapshot), 완료 marker(복사 완료를 알리는 파일), 호출자 식별자(callerId)는 표 안에서 이 형태로 쓴다. 작성 메모(본문에 쓰지 않음): 후속 일정은 없다 [Q12].
- figure-data: columns: 구분, 항목, 이유와 담당 | rows: 별도 시스템의 책임 · 설비 직접 접속, 로그 수집과 가공 · 설비 쪽 시스템이 파일을 만들어 파일 서버에 저장한다. FileGateway의 범위가 아니다 ; 별도 시스템의 책임 · 설정 파일 이력(History)과 스냅샷(Snapshot)의 생성, 복사, 보관 · 별도 시스템이 자정에 날짜 폴더로 복사하고 완료 marker(복사 완료를 알리는 파일)를 만든다. FileGateway는 읽어 제공만 한다 ; 생산 쪽의 책임 · 생산 중 파일의 원자적 교체, 잠금, 내용 일관성 · FileGateway는 파일 서버에 보이는 파일을 읽을 뿐 자체 사본 복사, 잠금, 버전 고정을 하지 않는다 ; MVP 밖 확장 · Linux 배포, SMB와 SFTP 어댑터, 다른 Site의 credential · 후속 단계다. 지금은 만들지 않으며 파일 접근 프로토콜은 필요해지면 IFileAccess 구현체로 더한다 ; MVP 밖 확장 · API Key별 설비와 로그 권한 · 지금은 활성 key의 권한 범위가 모두 같다. 필요해지면 호출자 식별자(callerId) 기준으로 제한할 수 있다 ; MVP 제외 · Range와 Resume 다운로드, 설정 파일 여러 개 자동 ZIP · 주요 파일이 대부분 100MB 이하라 뺐다. 자동 ZIP은 필요할 때 별도 요구사항으로 설계한다 ; MVP 제외 · Web UI와 WPF 클라이언트 자체, 고가용성과 분산 캐시 · 클라이언트는 API 사용자가 만들고, 기준정보 캐시는 프로세스 메모리 하나다
- source-span: FG@30d89a5:docs/01-requirements.md L147-161; FG@30d89a5:docs/07-extension-and-risks.md L12-44; [F004] [F018] [F132] [F133] [F134]

## 3. 설계 원칙 — 물리 구조를 감추는 여섯 가지
- intent: 호출하는 쪽이 의존해도 되는 보장 여섯 가지를 먼저 보여 주고, 뒤 섹션이 이를 어떻게 지키는지 잇는다
- shape: peer-list
- payload: 리드: 아래 여섯 가지는 설계 지침이자 코드로 확인된 동작이다. 호출하는 쪽은 논리 조회 조건만 알면 되고, 파일이 어디에 어떻게 저장돼 있는지는 FileGateway 안에 머문다 [F002]. 1 물리 정보 비노출: 서버 주소와 물리 경로는 성공·오류 응답 어디에도 없다 [F012]. 2 경로를 입력으로 만들지 않음: 파일 접근은 서버의 루트 경로(rootPath) 아래로 정규화한 경로만 쓴다 [F013]. 3 스트리밍: 파일을 메모리에 올리지 않고 시작 직전에 확인한 크기로 내려 보낸다 [F014]. 4 같은 Resolver: 목록 조회와 다운로드 요청이 같은 규칙으로 파일을 찾는다 [F015]. 5 오류 구분: 기준정보 없음, 파일 서버 접근 실패, 경로 없음, 파일 없음을 한 오류로 뭉개지 않는다 [F016]. 6 읽기 전용: 저장된 파일을 읽어 줄 뿐 생산 중 파일의 일관성은 보정하지 않는다 [F018]. 처음 나올 때 형태: 루트 경로(rootPath)는 2번 카드에서 쓴다. 카드의 번호 칩은 1부터 맨 정수다.
- figure-data: items: 1 · 물리 정보 비노출 · 서버 주소와 물리 경로는 성공 응답과 오류 응답 어디에도 나오지 않는다 ; 2 · 경로를 입력으로 만들지 않음 · 파일 접근은 서버의 루트 경로(rootPath) 아래로 정규화한 경로만 쓴다 ; 3 · 스트리밍 · 파일을 메모리에 올리지 않고 시작 직전에 확인한 크기로 내려 보낸다 ; 4 · 같은 Resolver · 목록 조회와 다운로드 요청이 같은 규칙으로 파일을 찾는다 ; 5 · 오류 구분 · 기준정보 없음, 파일 서버 접근 실패, 경로 없음, 파일 없음을 한 오류로 뭉개지 않는다 ; 6 · 읽기 전용 · 저장된 파일을 읽어 줄 뿐 생산 중 파일의 일관성은 보정하지 않는다
- source-span: FG@30d89a5:AGENTS.md L64-75; FG@30d89a5:README.md L626-634; [F012] [F013] [F014] [F015] [F016] [F018]

## 4. 제공 파일 — 설비가 내놓는 두 갈래
- intent: 한 설비에서 받을 수 있는 파일이 로그와 설정 파일 두 갈래이고, 로그는 생성 유형 하나를 가진다는 구분을 잡아 준다
- shape: hierarchy
- payload: 리드: 설비 식별자(equipmentId)는 표시명과 다른 안정적인 논리 식별자이고 한 배포 범위 안에서 유일하다 [F146]. 한 설비는 두 갈래를 내놓는다. 로그 종류(logType)와 설정 파일 종류(configurationType)다 [F021][F029]. 로그 종류 하나는 생성 유형(generationType) 하나를 가진다. 생성 유형은 Hourly, Daily, Continuous 세 가지다 [F022][F025]. Hourly는 시간 범위로, Daily는 일자 범위로 조회하고, Continuous는 시간 범위 없이 현재 파일만 조회한다 [F023][F024]. 로그 종류와 생성 유형은 서로 다른 축이다 [F021]. 설정 파일은 로그가 아니다. 설정 파일 종류 아래에 현재 설정 파일(Current)과 설정 파일 이력(History)이 있고, 현재 설정 파일은 한 종류에 여러 개(예: PM1~PM4)일 수 있다 [F005][F026][F027]. 설비마다 내놓는 파일이 달라도 기준정보의 차이로 표현하며 설비사 전용 분기는 없다 [F147]. 제공 종류는 file-types로 미리 확인하고, 이 조회는 파일 서버를 스캔하지 않는다 [F033]. 처음 나올 때 형태: 로그 종류(logType), 생성 유형(generationType), 설정 파일 종류(configurationType), 현재 설정 파일(Current). 작성 메모(본문에 쓰지 않음): 문서마다 생성 유형을 생성 정책이나 생성 주기로도 부르는데 이 설명서는 하나로 통일했다 [Q11].
- figure-data: root: 설비(equipmentId) | children: 로그 종류(logType)와 생성 유형(generationType)[Hourly: 시간 범위로 조회, Daily: 일자 범위로 조회, Continuous: 현재 파일만 조회], 설정 파일 종류(configurationType)[현재 설정 파일(Current): 지금 있는 파일 집합, 설정 파일 이력(History): 날짜별 스냅샷]
- source-span: FG@30d89a5:docs/00-glossary.md L7-25; FG@30d89a5:docs/04a-log-provider.md L33, L183-197; FG@30d89a5:docs/04b-configuration-provider.md L31-52; [F021] [F022] [F026] [F027]

## 5. 호출 순서 — 설비 목록에서 파일 수신까지
- intent: API 사용자가 설비 조회에서 다운로드까지 밟는 순서와, 조건 기반 직접 다운로드와 fileId 다운로드 중 무엇을 고를지를 보여 준다
- shape: branching-flow
- payload: 리드: 호출은 API Key로 시작한다. X-Api-Key 헤더로만 보내며 query string은 받지 않고, 누락과 불일치는 모두 401 InvalidApiKey다 [F046]. 순서는 설비 목록(GET /api/v1/equipments) → 제공 종류(GET /api/v1/equipments/{equipmentId}/file-types) → 로그나 설정 파일 목록 조회다 [F032][F033][F034][F038][F040]. 그다음 조건만으로 파일 하나가 정해지면 조건 기반 직접 다운로드를 쓴다. 목록을 거치는 왕복이 줄어든다. 로그의 /logs/download는 0건이면 404 FileNotFound, 1건이면 파일, 2건 이상이면 zip 스트림이다 [F036]. 현재 설정 파일의 /configurations/current/download는 1개면 파일이고 2개 이상이면 409 MultipleFilesMatched다 [F039][F037]. 설정 파일 이력에는 직접 다운로드가 없어 목록에서 받은 fileId(24시간 유효한 opaque 토큰)로 /files/download를 쓴다 [F041][F043][F059]. 콜아웃 WARN(한 문장): API Key를 query string에 실으면 인증되지 않는다 [F046]. 처음 나올 때 형태: 조건 기반 직접 다운로드, fileId(24시간 유효한 opaque 토큰)는 리드에서 쓴다. 작성 메모(본문에 쓰지 않음): README와 샘플은 로그 직접 다운로드가 여러 건이면 409라고 적지만 docs/05와 코드는 zip이다 [Q02]. API Key 신청 절차와 호출 주소는 문서에 없다 [Q09][Q10].
- figure-data: start: 호출 시작 | actions: API Key로 인증(X-Api-Key) → 설비 목록 조회 → 제공 종류 확인 → 로그 또는 설정 파일 목록 조회 | decision: 조건만으로 받을 파일이 정해지는가? | outcomes: [예] 조건 기반 직접 다운로드: 한 건은 파일이고 로그 여러 건은 zip(normal), [아니오 또는 이력] 목록의 fileId로 /files/download(target), [현재 설정 파일이 여러 건] 409 MultipleFilesMatched(negative), [0건] 404 FileNotFound(negative) | loop?: 409 MultipleFilesMatched → 목록 조회 | end?: 파일 수신
- source-span: FG@30d89a5:README.md L213-229; FG@30d89a5:docs/05-api-interface.md L14-24, L217-229, L388-414; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L28-49; [F036] [F039] [F041] [F046]

## 6. 호출 흐름 — 목록 조회에서 스트리밍까지
- intent: 목록 조회에서 fileId를 받고, 그 fileId로 스트리밍 다운로드하는 동안 FileGateway 안팎에서 오가는 요청과 응답의 순서를 보여 준다
- shape: interaction
- payload: 리드: 목록 요청이 오면 FileGateway는 API Key를 확인하고 기준정보 캐시에서 서버와 탐색 규칙(discovery rule)을 읽는다. 파일 서버에서 디렉터리 목록을 받아 items, fileId, continuationToken(다음 페이지 커서)을 돌려준다 [F034][F060]. 이 커서는 서버에 결과를 저장하지 않는 stateless 방식이고 유효기간은 30분이다 [F057]. 다운로드 요청은 fileId를 검증하고 현재 기준정보로 물리 경로를 다시 계산한다. 그래서 서버나 경로가 바뀌어도 같은 논리 파일이면 기존 fileId가 유효하다 [F060]. 파일 서버에서 stat과 읽기 열기를 한 뒤 시작 직전 크기를 Content-Length로 쓰며 스트리밍한다 [F014][F043]. fileId는 경로가 아니라 query parameter로 보낸다. 토큰이 260자를 넘기 쉬워 경로에 두면 IIS의 URL 세그먼트 한도에 걸린다 [F044]. 서버가 localhost로 등록돼 있으면 같은 흐름이지만 FTP 대신 같은 머신의 디스크를 읽는다 [F003]. 콜아웃 WARN(한 문장): 다운로드가 시작된 뒤 원격 I/O 오류가 나면 JSON 오류 대신 스트림이 끊기므로 Content-Length와 받은 바이트 수를 비교한다 [F063]. 처음 나올 때 형태: continuationToken(다음 페이지 커서), 탐색 규칙(discovery rule)은 리드와 그림에서 이 형태로 쓴다.
- figure-data: participants: API 사용자, FileGateway, 파일 서버 | messages: API 사용자→FileGateway GET /api/v1/logs 목록 요청 · FileGateway↻ 인증 후 기준정보 캐시에서 서버와 탐색 규칙(discovery rule) 조회 · FileGateway→파일 서버 디렉터리 목록 조회 · 파일 서버⇢FileGateway 파일 목록 · FileGateway⇢API 사용자 200 items와 fileId와 continuationToken · API 사용자→FileGateway GET /api/v1/files/download?fileId · FileGateway↻ fileId 검증 후 현재 기준정보로 물리 경로 다시 계산 · FileGateway→파일 서버 stat과 읽기 열기 · 파일 서버⇢FileGateway 파일 크기와 스트림 · FileGateway⇢API 사용자 200 스트림과 Content-Length
- source-span: FG@30d89a5:README.md L231-260; FG@30d89a5:docs/05-api-interface.md L287-304, L316-327, L358-376; FG@30d89a5:src/FileGateway.Api/Downloading/DownloadResult.cs L9-19; [F034] [F044] [F057] [F060] [F063]

## 7. 시간 범위 규칙 — Hourly·Daily와 Continuous
- intent: from과 to를 어떻게 주면 어떤 범위를 조회하고 언제 InvalidRequest가 되는지 규칙표로 보여 준다
- shape: rule-table
- payload: 리드: 시간 범위는 로그 종류의 생성 유형에 따라 다르다. 시간 값은 반개구간 [from, to)로 읽는다. from은 포함하고 to는 제외한다 [F052]. Hourly와 Daily에서 from과 to가 모두 없으면 최근 2일을 조회한다 [F049]. from만 주면 from부터 2일 구간이다 [F050]. to만 주거나 from이 to보다 앞서지 않으면 InvalidRequest다 [F051]. 범위가 MaxQueryRange(기본 31일)를 넘어도 InvalidRequest이며 이 값은 2일 이상이어야 한다 [F053]. Continuous는 시간 범위 없이 현재 파일만 조회하고 from이나 to가 오면 InvalidRequest다 [F024]. 현재 설정 파일 조회는 시간 필터를 쓰지 않는다 [F148]. 시간 값에 UTC offset이 없으면 Asia/Seoul로 읽고, +09:00은 URL에서 %2B09:00으로 인코딩한다 [F028][F055]. 콜아웃 NOTE(한 문장): 설정 파일 이력 조회는 from과 to를 모두 요구하고 최대 폭의 기본값은 366일이다 [F054]. 표의 조건 첫 칸의 아니오는 Hourly나 Daily다. 처음 나올 때 형태: 반개구간 [from, to).
- figure-data: conditions: Continuous 로그, from 입력, to 입력 | rules: 아니오/아니오/아니오 → 최근 2일 조회(positive) ; 아니오/예/아니오 → from부터 2일 조회(positive) ; 아니오/예/예 → 반개구간 [from, to) 조회. from이 앞서고 폭이 MaxQueryRange 이하일 때(positive) ; 아니오/아니오/예 → InvalidRequest(negative) ; 예/아니오/아니오 → 현재 파일만 조회(positive) ; 예/예/— → InvalidRequest(negative) ; 예/아니오/예 → InvalidRequest(negative)
- source-span: FG@30d89a5:docs/01-requirements.md L99-116; FG@30d89a5:src/FileGateway.Logs/LogListQuery.cs L19-40; [F049] [F050] [F051] [F052] [F053] [F054]

## 8. 오류 응답 — 14종의 code
- intent: 오류를 code로 가르는 방법과 14종이 어느 상태 아래 무엇을 뜻하는지 한 표로 보여 준다
- shape: text-table
- payload: 리드: 오류 code는 14종이다 [F066]. 응답 body는 type, title, status, code, traceId를 담는다. 호출하는 쪽은 code로 분기하고, 원인 추적은 traceId로 서버 로그와 잇는다. 물리 경로, credential, DB 진단은 응답에 없다 [F068]. fileId 오류는 형식·서명 400, 24시간 경과 410, 정의 삭제 404, 파일 없음 404의 순서로 가려진다 [F061]. 409 MultipleFilesMatched는 현재 설정 파일의 직접 다운로드에서만 나온다 [F037]. 스트리밍이 시작된 뒤의 오류는 JSON이 아니라 끊긴 스트림이다 [F063]. IIS나 ARR 단계의 502와 503은 JSON이 아닐 수 있어 본문을 JSON이라 가정하지 않는다 [F071]. 클라이언트가 끊은 요청은 ClientCancelled로 서버에 기록되며 응답 code가 아니다 [F065]. 표 문장은 README의 의미 표를 따른다 [F069].
- figure-data: columns: 상태, code, 뜻 | rows: 400 · InvalidRequest, InvalidFileId · 요청 파라미터, 시간 범위, 토큰 조건이 틀렸거나 fileId의 형식과 서명이 틀리다 ; 401 · InvalidApiKey · X-Api-Key 헤더가 없거나 값이 맞지 않는다 ; 404 · EquipmentNotFound, LogDefinitionNotFound, ConfigurationDefinitionNotFound, FileNotFound · 없는 설비이거나, 기준정보에서 삭제돼 다시 풀 수 없거나, 논리 파일이 실제로 없다 ; 409 · MultipleFilesMatched · 현재 설정 파일 직접 다운로드 조건에 파일이 2개 이상 일치한다 ; 410 · FileIdExpired · fileId의 24시간이 지났다 ; 500 · FileDefinitionConflict, InternalError · 기준정보나 파일 상태가 정의와 어긋났거나(cardinality 위반, metadata 해석 실패) 서버 내부 오류다 ; 502와 503 · FileServerUnavailable, FileServerProtocolError, ReferenceDataUnavailable · 파일 서버 연결이나 프로토콜 오류이거나(502) 사용할 수 있는 기준정보가 없다(503)
- source-span: FG@30d89a5:src/FileGateway.Core/Errors/FileGatewayErrors.cs L17-30; FG@30d89a5:README.md L417-444; FG@30d89a5:docs/05-api-interface.md L380-386, L424-462; [F066] [F067] [F068] [F069]

## 9. 시스템 구조 — 네 층과 외부 자원
- intent: 요청이 지나는 네 층과 맨 아래의 외부 자원, 층 사이의 연결 방식을 보여 준다
- shape: layered-structure
- payload: 리드: 파일 제공 경로는 네 층이다 [F011]. Api는 인증, 요청 검증, 감사 로그, Health, 스트리밍 응답을 맡는다 [F072]. Logs와 Configurations는 로그 탐색·필터·pagination과 현재 설정 파일·이력을 각각 맡는다 [F073][F074]. Core는 IFileAccess 계약과 token codec 계약을 두며 Log, Configuration, FTP, MSSQL, IIS를 모른다 [F075]. Infrastructure는 기준정보 캐시와 파일 접근을 맡는다 [F076]. 파일 접근은 FtpFileAccess와 LocalFileAccess 둘이고 RoutingFileAccess가 호출마다 서버 host로 하나를 고른다. 상위 계층은 IFileAccess만 안다 [F077]. host가 localhost와 정확히 일치할 때만 로컬 디스크로 가고 127.0.0.1, ::1, 머신명, FQDN은 모두 FTP다 [F078]. FTP는 FluentFTP를 쓰고 FtpClientPool이 전체와 서버별 한도 안에서 연결을 재사용한다 [F080][F081]. 외부 자원은 MSSQL과 파일 서버 둘이다 [F003][F086]. 호출하는 쪽은 Api 위쪽만 알면 된다 [F002]. 처음 나올 때 형태: 파일 서버는 이미 쓴 말이다.
- figure-data: layers: FileGateway.Api [key]: API Key 인증, 요청 검증, 감사 로그, Health, 스트리밍 응답 ‖ 기능 층: FileGateway.Logs(로그 탐색·필터·pagination), FileGateway.Configurations(현재 설정 파일과 이력) ‖ FileGateway.Core: IFileAccess 계약, token codec 계약 ‖ FileGateway.Infrastructure: 기준정보 캐시, FtpFileAccess(FluentFTP), LocalFileAccess(localhost 로컬), RoutingFileAccess(host로 선택) ‖ 외부 자원 [external]: MSSQL(FileGateway_GetReferenceData), 파일 서버(FTP/FTPS), 로컬 파일시스템(localhost) | links: Api ↕ 기능 층 = 조회를 위임한다 · 기능 층 ↕ Core = IFileAccess와 token codec 계약만 쓴다 · Core ↕ Infrastructure = 계약을 Infrastructure가 구현한다 · Infrastructure ↕ 외부 자원 = SP 호출과 FTP/FTPS 또는 로컬 읽기
- source-span: FG@30d89a5:docs/02-architecture.md L22-92; FG@30d89a5:docs/03-server-access-core.md L25-44; FG@30d89a5:src/FileGateway.Infrastructure/Ftp/RoutingFileAccess.cs L25-29; [F011] [F072] [F076] [F077] [F078]

## 10. 기준정보 모델 — 설비·서버·정의의 관계
- intent: API 제공자가 MSSQL에 등록하는 기준정보가 어떤 관계로 묶이고 정의가 어떤 키로 식별되는지 보여 준다
- shape: entity-relations
- payload: 리드: 기준정보(Reference Data)는 SP FileGateway_GetReferenceData가 result set 네 개로 돌려준다. Equipments, Servers, LogDefinitions, ConfigurationDefinitions다 [F086]. Servers는 ServerId, Host, FileRootPath를 담는다 [F087]. 로그 정의(Log Definition)는 설비의 로그 종류 하나를 서버와 탐색 규칙에 잇고 키는 EquipmentId + LogType이다. 설정 파일 정의(Configuration Definition)는 설정 파일 종류 하나를 같은 방식으로 잇고 키는 EquipmentId + ConfigurationType이다 [F088]. 모든 정의는 Equipments의 EquipmentId와 Servers의 ServerId를 가리켜야 하고, 아니면 그 정의는 invalid다 [F089]. 같은 키가 여러 행이면 충돌한 모든 행이 invalid다 [F088]. 로그 정의는 11개 컬럼, 설정 파일 정의는 13개 컬럼이고 컬럼 순서는 계약이 아니다 [F087]. 로그 정의의 탐색 규칙은 경로 템플릿, 파일명 glob, 생성 슬롯당 파일 수(Single 또는 Multiple)로 이루어진다 [F105][F106][F109]. 생성 슬롯은 Hourly는 한 시간, Daily는 하루로 나눈 논리 구간이며 물리 폴더와 1대 1이 아니다 [F108]. 결정적 파일명 추정(fileNameTemplate)은 선택 설정이다. Single이고 Hourly·Daily인 정의에만 쓰며 목록 조회 없이 StatFileAsync 한 번으로 파일을 확인한다 [F102]. 추정이 틀려도 응답은 같고 계산된 경로는 API 제공자 전용 진단 테이블에 남는다 [F103]. 설정 파일 정의는 current 규칙과 history 규칙을 따로 갖고 파일명 일치 방식은 Literal, Glob, Regex다. 완료 marker 경로는 history 규칙에 둔다 [F104][F112]. 모든 경로는 루트 경로 아래로 정규화되며 벗어나는 정의는 기준정보 오류로 본다 [F099]. SQL은 테스트·개발용 계약 구현이고 운영 DB 내부 구조는 이 계약만 지키면 자유롭다 [F090]. 기존 계약으로 표현할 수 있는 새 종류는 등록만으로 노출된다 [F019]. 처음 나올 때 형태: 기준정보(Reference Data), 로그 정의(Log Definition), 설정 파일 정의(Configuration Definition), 생성 슬롯, 결정적 파일명 추정(fileNameTemplate)은 리드에서 쓴다.
- figure-data: entities: 설비(EquipmentId), 파일 서버(ServerId, Host, FileRootPath), 로그 정의(EquipmentId+LogType, GenerationType, DirectoryTemplate, FileNamePattern, SlotCardinality), 설정 파일 정의(EquipmentId+ConfigurationType, CurrentDirectoryTemplate, HistoryDirectoryTemplate, HistoryCompletionMarkerPathTemplate) | relations: 설비 1—N 로그 정의 "제공한다" [owned] · 설비 1—N 설정 파일 정의 "제공한다" [owned] · 파일 서버 1—N 로그 정의 "파일을 둔다" [owned] · 파일 서버 1—N 설정 파일 정의 "파일을 둔다" [owned]
- source-span: FG@30d89a5:docs/06-reference-data.md L9-18, L32-63, L89-130, L215-224; FG@30d89a5:db/mvp-schema.sql L1-35; FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L67-127; [F086] [F087] [F088] [F089]

## 11. 기준정보 갱신 — 검증 단계와 마지막 정상본
- intent: 갱신에서 무엇이 통째로 거부되고 무엇이 정의 한 건만 빠지는지, 실패해도 서비스가 이어지는지 보여 준다
- shape: branching-flow
- payload: 리드: 기준정보 캐시의 CacheTtl 기본값은 15분이다 [F091]. TTL이 지난 뒤 첫 요청이 갱신을 시작하지만 그 요청은 기다리지 않고 기존 기준정보로 응답한다. 갱신은 프로세스당 하나만(single-flight) 돈다 [F092]. 갱신은 SP가 돌려주는 result set 네 개를 읽는다 [F086]. 기동 때도 요청 전에 한 번 읽어 둔다 [F093]. 필수 result set·컬럼 shape나 설비·서버 식별자가 틀리면 전역 검증 실패다. 새 데이터를 통째로 거부하고 마지막 정상본(last-known-good)을 stale 상태로 계속 쓴다. 정상본이 없는 최초 로딩이면 503 ReferenceDataUnavailable이다 [F094][F095]. 전역 검증을 통과하면 개별 정의의 실패는 그 정의만 빼고 나머지로 한 번에(atomic) 교체한다. 이전 정의로 메우지 않는다 [F096]. 빠진 정의는 catalog에 나오지 않고 직접 조회하면 LogDefinitionNotFound나 ConfigurationDefinitionNotFound다 [F098]. 실패한 뒤에는 이후 요청이 갱신을 계속 다시 시도한다 [F149]. 갱신은 구조와 문법만 검증하며 파일 서버에 파일이 실제로 있는지는 목록·다운로드 때 확인한다 [F097]. 콜아웃 KEY(한 문장): /health/ready는 stale이어도 200 Degraded이고 정상본이 한 번도 없을 때만 503 Unhealthy다 [F122]. 콜아웃 NOTE(한 문장): 새 컬럼이나 모드는 스키마와 SP, 앱 전 인스턴스, 값 활성화의 세 단계로 배포한다 [F100]. stale에는 최대 시간 제한이 없어 사용 여부와 마지막 정상 갱신 시각을 관측해야 한다 [F126]. 처음 나올 때 형태: 마지막 정상본(last-known-good). 작성 메모(본문에 쓰지 않음): README는 정의 1건 위반에도 전체 거부라고 적지만 06 문서와 코드는 정의 단위 격리다 [Q01]. 파일명 추정 실패 사유 이름은 싣지 않는다 [Q13].
- figure-data: start: TTL 경과 뒤 첫 요청이 갱신을 시작하고 그 요청은 기존 기준정보로 응답 | actions: FileGateway_GetReferenceData 호출 → result set 네 개 읽기 → 필수 result set과 설비·서버 식별자 검증 | decision: 전역 검증을 통과했는가? | outcomes: [아니오 정상본 있음] 새 데이터를 통째로 거부하고 마지막 정상본을 stale로 계속 쓴다(negative), [아니오 정상본 없음] 최초 로딩이라 503 ReferenceDataUnavailable(negative), [예 정의 일부 위반] 위반한 정의만 빼고 나머지로 한 번에 교체한다(target), [예 모두 정상] 전체를 한 번에 교체한다(normal) | loop?: stale 상태 → 이후 요청이 갱신을 다시 시도 | end?: 갱신 종료
- source-span: FG@30d89a5:docs/06-reference-data.md L258-295; FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataCache.cs L21-49, L117-159; FG@30d89a5:src/FileGateway.Infrastructure/ReferenceData/ReferenceDataSnapshotBuilder.cs L21-58; [F092] [F094] [F095] [F096] [F149]

## 12. 설정 파일 이력 — 완료 marker와 인계
- intent: 이력은 FileGateway가 만들지 않고 별도 시스템이 만들며, 완료 marker가 있어야 조회 대상이 된다는 인계를 보여 준다
- shape: role-handoff
- payload: 리드: 별도 시스템이 자정에 현재 설정 파일 집합을 날짜 폴더로 복사해 스냅샷을 만들고 원본은 그대로 둔다 [F111]. 복사를 마치면 완료 marker를 만든다. 이름과 위치는 기준정보의 history 규칙이 정한다 [F112]. FileGateway는 marker가 있는지만 보고 내용은 읽지 않는다. marker가 있는 날짜 폴더의 스냅샷만 조회 대상이며 복사 중인 폴더는 보이지 않는다. marker 파일 자체도 결과에 없다 [F112][F113]. API 사용자는 설정 파일 이력 목록(from과 to 필수)에서 fileId를 받아 /files/download로 내려받는다. 이력 전용 직접 다운로드는 없다 [F040][F041]. 스냅샷 fileId를 다시 열 때도 marker를 확인하고, marker가 사라지면 파일이 남아 있어도 FileNotFound다 [F116]. 스냅샷은 만들어진 뒤 바뀌지 않는다 [F114]. snapshotTimestamp는 metadata 규칙이 없으면 날짜 폴더의 자정이다 [F115]. 콜아웃 KEY(한 문장): 이력을 만드는 일과 marker를 만드는 일은 모두 별도 시스템의 몫이다 [F004][F112].
- figure-data: lanes: 별도 시스템(이력 생산자), FileGateway, API 사용자 | steps: 별도 시스템:자정에 현재 설정 파일 집합을 날짜 폴더로 복사 → 별도 시스템:복사가 끝나면 완료 marker 생성 → FileGateway:◇ 날짜 폴더에 완료 marker가 있는가?(decision) →[예] FileGateway:스냅샷을 이력 목록에 포함 → API 사용자:from과 to로 이력을 조회해 fileId 받기 → API 사용자:fileId로 /files/download 요청 → FileGateway:marker를 다시 확인하고 파일을 내려 보냄
- source-span: FG@30d89a5:docs/04b-configuration-provider.md L136-160, L197-200; FG@30d89a5:docs/01-requirements.md L93-97; FG@30d89a5:docs/05-api-interface.md L231-276; [F111] [F112] [F113] [F116]

## 13. 운영 설정 — 기본값과 비밀 값
- intent: API 제공자가 배포 때 다루는 설정의 기본값과, 환경변수로만 넣는 비밀 값을 한 표로 정리한다
- shape: text-table
- payload: 리드: 설정은 appsettings.json의 기본값이고 환경별 값으로 덮어쓴다. 비밀은 파일에 두지 않고 환경변수(또는 IIS·Secret 관리 도구)로만 넣는다 [F117]. 기본값은 초기값이다. FTP 타임아웃과 동시성은 실제 환경을 측정한 뒤 확정한다 [F130]. 표의 행은 다음과 같다. 조회 범위는 로그 31일과 이력 366일이다 [F053][F054]. 페이지 크기는 기본 100, 최대 1000이다 [F056]. 토큰은 fileId 24시간과 continuationToken 30분이다 [F059][F057]. 기준정보 캐시는 15분이다 [F091]. FTP 보안의 기본은 Plain이다 [F082]. FTP 타임아웃은 연결 15초와 읽기 60초이고 동시성은 전체 50과 서버별 5다 [F083]. 비밀 값은 4종이다 [F117]. 표 아래 각주(*): 키 이름은 appsettings.json의 FileGateway 섹션 기준이며 비밀 값의 환경변수 이름만 전체 경로다 [F117]. 키 디렉터리가 Development 외 환경에서 비면 앱이 기동에 실패한다 [F118]. 콜아웃 WARN(한 문장): 키 디렉터리를 잃으면 발급한 모든 fileId가 무효가 되므로 App Pool을 재시작해도 남는 경로를 쓰고 Load User Profile을 true로 둔다 [F119]. 콜아웃 NOTE(한 문장): /tester와 /scalar/v1은 Development 환경이거나 FileGateway:DevTools:Enabled=true일 때만 열리며 기본값은 false다 [F125]. IIS에는 .NET Hosting Bundle을 설치하고 In-process로 호스팅한다 [F120]. 처음 나올 때 형태: 이미 쓴 말만 쓴다.
- figure-data: columns: 설정, 기본값, 뜻 | rows: 조회 범위 · 로그 31일, 이력 366일 · Logs:MaxQueryRange와 Configurations:HistoryMaxQueryRange. 로그 값은 2일 이상이어야 하며 기동 때 검증한다 ; 페이지 크기 · 기본 100, 최대 1000 · Paging:LimitDefault와 LimitMax. 최댓값을 넘는 limit은 InvalidRequest다 ; 토큰 유효기간 · fileId 24시간, continuationToken 30분 · Tokens:FileIdTtl과 ContinuationTtl ; 기준정보 캐시 · 15분 · ReferenceData:CacheTtl. 강제 폐기가 아니라 갱신을 다시 시도할 시점이다 ; FTP 보안 · Plain, 인증서 비신뢰 허용 false · Plain, ExplicitTls, ImplicitTls 중에서 고른다. self-signed 인증서는 Ftp:AcceptUntrustedCertificates로만 허용한다 ; FTP 타임아웃과 동시성 · 연결 15초, 읽기 60초, 전체 50, 서버별 5 · Ftp:ConnectTimeoutSeconds, ReadTimeoutSeconds, MaxConcurrentGlobal, MaxConcurrentPerServer. 실제 환경을 측정한 뒤 확정한다 ; 비밀 값 4종 · 환경변수로만 넣는다 · Authentication__ApiKeys__0__Key와 CallerId, ConnectionStrings__ReferenceData, FileGateway__Ftp__UserName과 Password, DataProtection__KeyDirectory
- source-span: FG@30d89a5:README.md L118-165; FG@30d89a5:src/FileGateway.Api/appsettings.json L1-40; FG@30d89a5:src/FileGateway.Api/Options/FileGatewayOptions.cs L18-49; [F053] [F054] [F056] [F082] [F083] [F091] [F117]

## 14. 현재 상태 — 구현 완료와 배포 확인 대기
- intent: 무엇이 끝났고 무엇이 남았는지를 팀 리더가 한 장에서 읽게 한다
- shape: status
- payload: 리드: MVP 구현은 끝났고 통합 검증 단계를 거쳤다. README는 단위·통합 테스트가 전부 통과했다고 적는다 [F135]. 그러나 자동화 게이트는 구현 완료의 조건일 뿐 MVP 완료가 아니다. MVP 완료는 Windows Server + IIS + 실제 MSSQL + 파일 서버(FTP/FTPS) 환경에서 사람이 하는 배포 검증까지 통과해야 한다 [F136]. 남은 일은 배포 전 필수 확인 20개와 MVP 완료 기준 10개를 배포 검증 체크리스트에 기록하는 것이다 [F138][F139][F150]. 저장소의 체크리스트 사본에는 통과·차단 표시가 없고 [F142], HANDOFF(2026-09-03)는 실제 환경의 배포 검증이 남은 유일한 일이라고 적는다 [F143]. 환경 때문에 미루면 "구현 완료, 배포 검증 보류"로 쓰고 미실행 항목과 사유를 남긴다 [F141]. 표의 완료 행은 다음과 같다. API 엔드포인트 9개와 Health 2개 [F030][F031]. 기준정보 캐시와 정의 단위 검증 [F092][F096]. 파일 접근과 연결 재사용 [F077][F081]. 인증, 감사 로그, Health [F046][F121][F122]. 클라이언트 샘플 8개 [F145]. 자동화 테스트는 단위·통합 프로젝트이고, 통합 테스트는 운영 유사 환경 검증을 대체하지 않는다 [F137]. 처음 나올 때 형태: 배포 검증 체크리스트, MVP 완료 기준은 리드와 표에서 이 형태로 쓴다. 작성 메모(본문에 쓰지 않음): 배포 검증을 시작했는지, 테스트를 현재 커밋에서 다시 돌린 결과, 이슈 #12와 #13의 현재 상태는 모른다 [Q04][Q05][Q06]. 진척 %는 근거가 없어 "—"로 둔다.
- figure-data: rows: 실 환경 배포 확인(API 제공자) · 대기 · — · 배포 전 필수 확인 20개와 MVP 완료 기준 10개를 Windows Server와 IIS, 실제 MSSQL, 파일 서버에서 확인 ; 자동화 테스트 · 완료 · — · 단위와 통합 프로젝트 둘. README 기준 전부 통과이며 이 설명서는 다시 돌리지 않았다 ; 파일 제공 API · 완료 · — · /api/v1 엔드포인트 9개와 Health 2개 ; 기준정보 캐시와 검증 · 완료 · — · SP 하나와 result set 4개, 정의 단위 격리, 마지막 정상본 ; 파일 접근 · 완료 · — · FTP/FTPS와 localhost 로컬 읽기, 연결 재사용 ; 인증과 감사 로그와 Health · 완료 · — · X-Api-Key 인증, 감사 로그, Health 두 개 ; 클라이언트 샘플 · 완료 · — · Python과 C#, 유스케이스 8개
- source-span: FG@30d89a5:README.md L9-11; FG@30d89a5:docs/10-testing-and-deployment.md L199-214; FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L1-61; FG@30d89a5:HANDOFF.md L207-211, L249; [F135] [F136] [F138] [F142] [F143]

## 15. 배포 전 필수 확인 — 20개 항목
- intent: API 제공자가 배포 전에 확인할 20개 항목을 영역별로 묶어 보여 주고, 결과를 기록하는 방법을 알린다
- shape: text-table
- payload: 리드: 배포 전 필수 확인은 docs/10의 20개 항목이고 영역 일곱 개로 묶었다 [F138]. 결과는 배포 검증 체크리스트에 항목별 통과·차단과 원인으로 기록한다. MVP 완료 기준 10개까지 전부 통과해야 MVP 완료를 선언한다 [F139][F150]. 환경 제약으로 미루면 "구현 완료, 배포 검증 보류"와 함께 미실행 항목, 사유, 재검증 예정일을 남긴다 [F141]. 표는 20개 항목을 빠짐없이 싣는다 [F151]. 영역별 근거는 다음과 같다. 기동 환경 [F009][F120]. 인증과 비밀 [F046][F048][F017]. 기준정보 [F033][F096][F097]. 파일 서버 연결 [F127][F128]. 목록과 다운로드 [F108][F102][F103]. 오류와 한도 [F110][F083][F099]. 이력과 토큰 [F116][F119]. 콜아웃 NOTE(한 문장): 배포 검증 체크리스트 Step 1은 19행이라 20번째 항목(fileNameTemplate)은 따로 확인해야 한다 [F139][F140]. 처음 나올 때 형태: 이미 쓴 말만 쓴다. 작성 메모(본문에 쓰지 않음): 체크리스트에 20번째 행을 추가할지는 문서 담당이 정한다 [Q03].
- figure-data: columns: 영역, 확인 항목 | rows: 기동 환경 · HTTPS 인증서와 바인딩 ¶ IIS ASP.NET Core Hosting Bundle과 권한 ¶ MSSQL 연결 ; 인증과 비밀 · 여러 X-Api-Key 인증과 호출자 구분, query string key 비허용 ¶ API Key 신/구 overlap 회전 ¶ 로그와 Secret에 민감정보 비노출 ; 기준정보 · 설비별 제공 파일 종류 API가 DB 기준정보와 일치하고 파일 서버 접근 없이 동작 ¶ 기준정보 구조 검증, atomic 교체, stale fallback, single-flight 동작 ¶ 기준정보 갱신이 파일 서버 실재 검사를 하지 않음 ; 파일 서버 연결 · 각 파일 서버 21번 제어 연결 ¶ IIS FTP SSL 설정(FTP 또는 FTPS) ¶ Passive 데이터 포트 범위와 방화벽 ; 목록과 다운로드 · 실제 파일 목록과 다운로드 ¶ 여러 생성 슬롯이 같은 물리 폴더를 쓰는 로그 탐색 ¶ fileNameTemplate 설정 시 LIST 없이 StatFileAsync로 확정되는지와 추정 실패가 진단 테이블에 기록돼도 응답이 정상인지 ; 오류와 한도 · 폴더 없음과 파일 서버 장애와 일부 FTP 실패의 구분 ¶ FTP 전체와 서버별 동시성 제한 ¶ 루트 경로 경계와 traversal 차단 ; 이력과 토큰 · 설정 파일 이력 완료 marker 존재 조건과 스냅샷 fileId 재검증 ¶ token 보호 key의 재시작 내구성과 rotation 때 기존 fileId 24시간 유지
- source-span: FG@30d89a5:docs/10-testing-and-deployment.md L176-214; FG@30d89a5:docs/DEPLOYMENT-CHECKLIST.md L1-61; [F138] [F139] [F140] [F141] [F150] [F151]

## 부록 — API 엔드포인트 9개
- intent: API 사용자가 찾아보는 엔드포인트 일람을 한 표로 둔다
- shape: text-table
- payload: 엔드포인트는 /api/v1 아래 9개다 [F030]. 로그 목록과 이력 목록은 items와 continuationToken을 담은 JSON이고, 현재 설정 파일 목록은 단순 배열이다 [F034][F038][F040]. Health 두 개는 /api 밖이며 인증이 없다 [F031]. 다운로드 두 곳은 스트림이고 로그 직접 다운로드만 여러 건이면 zip이다 [F036].
- figure-data: columns: 엔드포인트, 하는 일, 응답 | rows: GET /api/v1/equipments · 전체 설비 식별자 목록 · items ; GET /api/v1/equipments/{equipmentId}/file-types · 설비가 내놓는 로그 종류와 생성 유형, 설정 파일 종류 · logs와 configurations ; GET /api/v1/logs · 로그 목록(Hourly, Daily, Continuous) · items와 continuationToken ; GET /api/v1/logs/download · 직접 다운로드(한 건은 파일, 여러 건은 zip) · 스트림 ; GET /api/v1/configurations/current · 현재 설정 파일 목록 · 단순 배열 ; GET /api/v1/configurations/current/download · 직접 다운로드(한 개일 때만) · 스트림 ; GET /api/v1/configurations/history · 설정 파일 이력 목록(from과 to 필수) · items와 continuationToken ; GET /api/v1/files · fileId로 파일 정보 확인 · fileId, fileName, size ; GET /api/v1/files/download · fileId로 스트리밍 다운로드 · 스트림
- source-span: FG@30d89a5:src/FileGateway.Api/Endpoints/CatalogEndpoints.cs L11, L23; FG@30d89a5:src/FileGateway.Api/Endpoints/LogEndpoints.cs L17, L28; FG@30d89a5:src/FileGateway.Api/Endpoints/ConfigurationEndpoints.cs L17, L28, L48; FG@30d89a5:src/FileGateway.Api/Endpoints/FileEndpoints.cs L17, L25; [F030] [F031]
