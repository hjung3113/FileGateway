# Matt Pocock Skills Upstream

FileGateway에는 `mattpocock/skills` 전체를 설치하지 않고 설계에 직접 필요한 skills만 vendor한다.

## Source

- Repository: `mattpocock/skills`
- Commit: `5b15a47f2d7150f545fbcacbfe381787fc0230dc`
- License: MIT (`.mattpocock/LICENSE`)

## Included skills

- `codebase-design`: deep module, interface, seam, adapter 중심의 코드베이스 설계 원칙
- `domain-modeling`: 도메인 용어 정제와 설계 의사결정 기록 원칙
- `improve-codebase-architecture`: 기존 코드베이스의 구조 개선 후보 탐색
- `grilling`: `improve-codebase-architecture`의 후속 설계 검토에 필요한 직접 의존 skill

## Excluded

구현, 디버깅, 코드 리뷰, TDD, 리서치, 티켓 관리, 프로토타이핑 등 설계 자체와 직접 관련 없는 skills는 포함하지 않는다.

## Project precedence

Upstream `domain-modeling`은 기본적으로 루트 `CONTEXT.md`와 `docs/adr/` 구조를 가정한다. FileGateway에서는 기존 설계 문서 체계가 우선한다.

- 설계/요구사항 작업은 항상 `docs/INDEX.md`부터 확인한다.
- 기존 역할별 문서에 기록 가능한 내용은 새 `CONTEXT.md`나 ADR 파일을 만들기보다 해당 문서를 갱신한다.
- 새 설계 문서가 실제로 필요하면 `docs/INDEX.md`에도 함께 등록한다.
- `AGENTS.md`의 FileGateway Design Guardrails가 vendored skill보다 우선한다.

## Update policy

업데이트 시 upstream 최신 변경을 검토하되 전체 repo를 동기화하지 않는다. 위 포함 목록과 직접 지원 파일만 갱신하고, 프로젝트 문서 구조와 충돌 여부를 다시 확인한다.
