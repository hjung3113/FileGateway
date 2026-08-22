# FileGateway

분산된 설비 서버의 로그 및 향후 추가 파일을 클라이언트에 제공하기 위한 파일 게이트웨이 프로젝트입니다.

클라이언트는 실제 공정/설비별 서버 분산 구조와 물리 파일 경로를 알 필요 없이 **설비명과 조회 조건**만으로 파일 목록을 조회하고 다운로드합니다.

## 문서

- [요구사항](docs/01-requirements.md)
- [전체 아키텍처](docs/02-architecture.md)
- [Server Access Core](docs/03-server-access-core.md)
- [Log Provider](docs/04-log-provider.md)
- [API / 클라이언트 인터페이스](docs/05-api-interface.md)
- [DB 및 기준정보 연계](docs/06-reference-data.md)
- [확장성 및 주요 리스크](docs/07-extension-and-risks.md)
- [Agent Tooling](docs/08-agent-tooling.md)

## 핵심 구조

```text
Client (.NET / Python FastAPI)
          |
       HTTP(S)
          |
   FileGateway API
          |
   Feature Provider
   (Log Provider 등)
          |
 Server Access Core
      /        \
 MSSQL SP    File Servers
```

## Agent 개발 환경

프로젝트 공통 지침은 `AGENTS.md`를 기준으로 합니다.

- `CLAUDE.md` → `AGENTS.md` 심볼릭 링크
- Superpowers: `.superpowers` Git submodule
- Claude: `.claude/skills/superpowers`
- Codex: `.agents/skills/superpowers`
- OpenCode: `.opencode/skills/superpowers`
- OMP: `.omp/skills/superpowers`

Superpowers skills 경로들은 모두 같은 `.superpowers/skills`를 가리킵니다.

새로 clone한 경우:

```bash
git submodule update --init --recursive
```

`AGENTS.md`에는 `multica-ai/andrej-karpathy-skills`의 핵심 원칙을 FileGateway 프로젝트 규칙에 맞게 통합했습니다.
