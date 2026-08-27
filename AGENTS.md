# AGENTS.md

FileGateway 프로젝트에서 작업하는 코딩 에이전트 공통 지침입니다.

이 파일을 프로젝트 지침의 단일 기준으로 사용합니다. `CLAUDE.md`는 이 파일을 가리키는 심볼릭 링크입니다.

## Project Context

- 목적: 분산 파일 서버에 저장된 설비 로그 및 Configuration File을 클라이언트에 제공하는 File Gateway 구축
- 설비 로그의 직접 수집/가공과 Configuration History 생성은 별도 시스템 책임이며 FileGateway 범위가 아님
- 클라이언트: .NET, Python/FastAPI, WPF, Web Backend, 다른 서버 시스템
- 외부 인터페이스: HTTPS + JSON, 파일은 streaming download
- MVP 서버: ASP.NET Core/.NET, Windows Server + IIS
- 기준정보: MSSQL Stored Procedure를 통해 설비-서버-탐색규칙 관계 조회
- 파일 서버 접근: 공통 `IFileAccess` 뒤의 FTP/FTPS Adapter
- 핵심 원칙: 클라이언트는 실제 서버/물리 경로 구조를 몰라야 함
- 구조: API → Logs/Configurations → Core contracts → Infrastructure(MSSQL, FTP/FTPS)
- 향후 Linux, 다른 Site/credential, 다른 파일 Provider 확장을 허용하되 MVP에 선구현하지 않음

**설계·요구사항을 확인하거나 변경하기 전에는 반드시 `docs/INDEX.md`부터 읽고, 인덱스가 안내하는 역할별 문서를 확인합니다.**

## Engineering Guidelines

아래 원칙은 `multica-ai/andrej-karpathy-skills`의 취지를 프로젝트 지침에 맞게 적용한 것입니다.

### 1. 구현 전에 검증

- 요구사항이 명확하지 않으면 임의로 가정하지 않는다.
- 여러 해석이 가능한 부분은 중요한 차이를 드러낸다.
- 더 단순한 해결책이 있으면 과도한 설계 대신 그것을 우선 검토한다.
- 기존 코드/문서와 충돌하는 요구사항이 있으면 구현 전에 문제를 명시한다.

### 2. 단순성 우선

- **YAGNI(You Aren't Gonna Need It)를 기본 원칙으로 적용한다. 현재 요구사항과 검증된 사용 사례에 필요하지 않은 기능·추상화·확장 포인트는 구현하지 않는다.**
- 요청되지 않은 기능을 미리 추가하지 않는다.
- 한 번만 쓰는 기능에 불필요한 추상화를 만들지 않는다.
- 확장성은 현재 요구사항에서 실제로 필요한 경계에만 둔다.
- 미래 가능성만을 이유로 인터페이스, 계층, 설정, Provider, 옵션을 선구현하지 않는다.
- 같은 목적을 더 적은 코드와 더 단순한 구조로 달성할 수 있으면 단순한 쪽을 선택한다.

### 3. 변경 범위 최소화

- 요청과 직접 관계없는 리팩터링, 포맷 변경, 주석 수정은 하지 않는다.
- 기존 코드 스타일과 구조를 우선 존중한다.
- 이번 변경으로 새로 발생한 unused 코드/참조는 정리한다.
- 기존에 존재하던 별도 문제는 임의로 수정하지 말고 필요 시 별도로 보고한다.

### 4. 검증 가능한 완료 기준

- 구현 전에 성공 조건을 확인한다.
- 버그 수정은 가능하면 재현/검증 가능한 테스트 또는 명령으로 증명한다.
- 변경 후 관련 테스트, 빌드, 정적 검사 등 가능한 검증을 실행한다.
- 검증하지 않은 상태에서 완료됐다고 단정하지 않는다.

### 5. 보안은 요구사항과 위협모델 범위에서

- 보안상 반드시 필요한 기본 보호와 명시된 요구사항은 구현한다.
- **구체적인 위협, 요구사항, 규정, 운영 필요성이 확인되지 않은 과도한 보안 하드닝은 하지 않는다.**
- 미래의 가상 위협만을 이유로 인증/인가 계층, 암호화 계층, 별도 보안 서비스, 복잡한 정책 엔진 등을 선제적으로 추가하지 않는다.
- 보안 조치가 구조·운영·테스트 복잡도를 크게 증가시키면 실제 위험과 요구사항을 먼저 확인하고 최소한의 해결책을 선택한다.
- 단, 외부 입력 검증, credential/secret 비노출, 경로 조작 방지, opaque token 보호처럼 현재 기능에 직접 필요한 기본 안전장치는 YAGNI를 이유로 제거하거나 생략하지 않는다.

## FileGateway Design Guardrails

- 실제 서버 주소와 물리 경로를 외부 API 모델에 노출하지 않는다.
- 클라이언트 입력으로 파일 시스템/FTP 경로를 직접 조합하지 않는다.
- 로그 종류별 경로/조회 규칙을 공통 파일 접근 계층에 넣지 않는다.
- Hourly/Daily/Continuous 로그 정책을 구분한다.
- Hourly/Daily는 논리 시간 범위를 사용하고, Continuous는 현재 파일만 조회하며 `from`/`to` 입력을 `InvalidRequest`로 거부한다.
- Hourly/Daily의 논리 생성 슬롯과 물리 디렉터리는 1:1이라고 가정하지 않는다. 여러 시간대 파일이 같은 디렉터리에 모일 수 있다.
- 파일 전체 메모리 적재가 아닌 스트리밍을 기본으로 한다.
- 목록/직접 다운로드는 동일 Resolver 규칙을 사용한다.
- DB 기준정보 없음, 파일 서버 접근 실패, 경로 없음, 대상 파일 없음을 같은 오류로 뭉개지 않는다.
- FTP credential/API Key 원문/물리 경로를 로그에 남기지 않는다.

## Agent Skills

### Superpowers

Superpowers runtime skills는 프로젝트 내부 `.superpowers/skills`에 일반 Git 파일로 포함되어 있습니다. 별도 submodule 초기화는 필요하지 않습니다.

각 에이전트는 다음 프로젝트 로컬 심볼릭 링크를 통해 동일한 skills를 사용합니다.

- Claude Code: `.claude/skills/superpowers`
- Codex: `.agents/skills/superpowers`
- OpenCode: `.opencode/skills/superpowers`
- OMP: `.omp/skills/superpowers`

Superpowers 출처, 기준 버전 및 프로젝트 로컬 조정 내용은 `.superpowers/UPSTREAM.md`를 참조합니다.

### Matt Pocock design skills

`mattpocock/skills`에서는 설계에 직접 필요한 subset만 `.mattpocock/skills`에 vendor합니다.

포함 skills:

- `codebase-design`
- `domain-modeling`
- `improve-codebase-architecture`
- `grilling` — architecture 개선 skill의 직접 의존 설계 인터뷰 skill

각 에이전트는 다음 심볼릭 링크를 통해 동일한 subset을 사용합니다.

- Claude Code: `.claude/skills/mattpocock`
- Codex: `.agents/skills/mattpocock`
- OpenCode: `.opencode/skills/mattpocock`
- OMP: `.omp/skills/mattpocock`

### Impeccable

Impeccable frontend design/audit skill은 각 에이전트 runtime에 프로젝트 로컬 산출물로 포함합니다.

- Claude Code: `.claude/skills/impeccable`
- Codex: `.agents/skills/impeccable`
- OpenCode: `.opencode/skills/impeccable`
- UI detector hooks: `.claude/settings.local.json`, `.codex/hooks.json`

프로젝트 규칙과 `docs/INDEX.md`/역할별 문서가 skill 기본 지침보다 우선합니다.

**프로젝트 규칙 우선순위:** vendored skill의 기본 문서 구조보다 이 `AGENTS.md`와 `docs/INDEX.md`가 우선합니다. 특히 `domain-modeling`이 제안하는 루트 `CONTEXT.md`/`docs/adr/` 자동 생성은 FileGateway에서 기본 동작으로 채택하지 않습니다. 기존 역할별 설계 문서에 먼저 반영하고, 새 문서가 정말 필요할 때만 생성한 뒤 `docs/INDEX.md`에 등록합니다.

출처, 기준 커밋, 포함/제외 범위는 `.mattpocock/UPSTREAM.md`를 참조합니다.
