# Agent Tooling

## 공통 지침

프로젝트의 에이전트 공통 지침은 루트 `AGENTS.md`를 단일 기준으로 사용한다.

설계/요구사항 관련 작업은 **`docs/INDEX.md`를 먼저 읽고** 현재 작업에 해당하는 역할별 문서를 확인한다.

`CLAUDE.md`는 별도 내용을 유지하지 않고 `AGENTS.md`를 가리키는 심볼릭 링크다.

`AGENTS.md`에는 `multica-ai/andrej-karpathy-skills`의 핵심 원칙을 FileGateway에 맞게 통합한다.

적용 원칙:

- 구현 전에 가정과 충돌 검증
- 단순성 우선
- 요청 범위에 한정된 변경
- 테스트/빌드 등 검증 가능한 완료 기준

참고 기준 커밋: `multica-ai/andrej-karpathy-skills@2c606141936f1eeef17fa3043a72095b4765b9c2`

## Superpowers

Superpowers는 submodule을 사용하지 않고 프로젝트 내부 `.superpowers`에 일반 Git 파일로 vendor한다.

현재 기준:

- Repository: `obra/superpowers`
- Release: `v6.3.0`
- Commit: `b36e0829c6d0140e93cfef2ca599b1b07d4a7797`
- 상세 출처/조정 내역: `.superpowers/UPSTREAM.md`

프로젝트에서 사용하는 runtime skills와 필요한 지원 파일만 포함한다.

## Matt Pocock design skills

`mattpocock/skills` 전체가 아니라 설계에 직접 필요한 subset만 `.mattpocock/skills`에 vendor한다.

포함 범위:

- `codebase-design`
- `domain-modeling`
- `improve-codebase-architecture`
- `grilling` — `improve-codebase-architecture`의 직접 의존성

현재 기준:

- Repository: `mattpocock/skills`
- Commit: `5b15a47f2d7150f545fbcacbfe381787fc0230dc`
- 상세 출처/포함·제외 범위: `.mattpocock/UPSTREAM.md`

구현, 디버깅, 코드리뷰, TDD, 리서치, 티켓 관리, 프로토타입 관련 skills는 설치하지 않는다.

Upstream `domain-modeling`의 기본 `CONTEXT.md`/`docs/adr/` 구조보다 FileGateway의 `docs/INDEX.md`와 역할별 설계 문서 구조를 우선한다.

## 프로젝트별 skill 연결

```text
.claude/skills/superpowers   -> ../../.superpowers/skills
.agents/skills/superpowers   -> ../../.superpowers/skills
.opencode/skills/superpowers -> ../../.superpowers/skills
.omp/skills/superpowers      -> ../../.superpowers/skills

.claude/skills/mattpocock   -> ../../.mattpocock/skills
.agents/skills/mattpocock   -> ../../.mattpocock/skills
.opencode/skills/mattpocock -> ../../.mattpocock/skills
.omp/skills/mattpocock      -> ../../.mattpocock/skills
```

skills 파일을 각 에이전트 디렉터리에 중복 복사하지 않는다.

## Clone

별도 submodule 초기화가 필요 없다.

```bash
git clone https://github.com/hjung3113/FileGateway.git
```

Windows에서 symlink가 일반 파일로 체크아웃되는 환경에서는 Developer Mode 또는 Git symlink 설정을 확인한다.

## 업데이트

Superpowers 및 Matt Pocock skills 업데이트 시 각 upstream 기준 커밋과 변경사항을 검토하고, 해당 `UPSTREAM.md`를 함께 갱신한다.

외부 지침을 그대로 덮어쓰기보다 FileGateway 프로젝트 규칙과 충돌 여부를 확인하고 필요한 범위만 반영한다.
