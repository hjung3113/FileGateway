# Agent Tooling

## 공통 지침

프로젝트의 에이전트 공통 지침은 루트 `AGENTS.md`를 단일 기준으로 사용한다.

`CLAUDE.md`는 별도 내용을 유지하지 않고 `AGENTS.md`를 가리키는 심볼릭 링크다.

`AGENTS.md`에는 `multica-ai/andrej-karpathy-skills`의 핵심 원칙을 FileGateway에 맞게 통합한다.

적용 원칙:

- 구현 전에 가정과 충돌 검증
- 단순성 우선
- 요청 범위에 한정된 변경
- 테스트/빌드 등 검증 가능한 완료 기준

참고 기준 커밋: `multica-ai/andrej-karpathy-skills@2c606141936f1eeef17fa3043a72095b4765b9c2`

## Superpowers

Superpowers는 프로젝트 내부 `.superpowers` Git submodule로 관리한다.

현재 고정 기준:

- Repository: `obra/superpowers`
- Commit: `b36e0829c6d0140e93cfef2ca599b1b07d4a7797`
- 해당 커밋은 v6.3.0 릴리스 기준

## 프로젝트별 skill 연결

동일한 `.superpowers/skills`를 다음 경로에서 심볼릭 링크로 참조한다.

```text
.claude/skills/superpowers  -> ../../.superpowers/skills
.agents/skills/superpowers  -> ../../.superpowers/skills
.opencode/skills/superpowers -> ../../.superpowers/skills
.omp/skills/superpowers     -> ../../.superpowers/skills
```

따라서 Superpowers 원본을 여러 위치에 복사하지 않는다.

## Clone 후 초기화

```bash
git submodule update --init --recursive
```

심볼릭 링크를 정상적으로 체크아웃할 수 있는 Git 환경이 필요하다.

Windows에서 symlink 권한/정책 때문에 링크가 일반 파일로 체크아웃되는 환경에서는 Developer Mode 또는 Git의 symlink 설정을 확인해야 한다.

## 업데이트

Superpowers를 업데이트할 때는 `.superpowers` submodule 포인터만 새로운 검증된 커밋으로 이동한다. 각 도구별 skill 링크는 변경하지 않는다.

`AGENTS.md`의 외부 지침을 갱신할 때는 upstream 변경을 그대로 덮어쓰기보다 FileGateway 프로젝트 규칙과 충돌 여부를 검토한 후 필요한 원칙만 반영한다.
