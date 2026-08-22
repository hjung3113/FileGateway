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
- Superpowers runtime skills: `.superpowers/skills`에 일반 Git 파일로 포함
- Claude: `.claude/skills/superpowers`
- Codex: `.agents/skills/superpowers`
- OpenCode: `.opencode/skills/superpowers`
- OMP: `.omp/skills/superpowers`

각 Superpowers skill 경로는 동일한 `.superpowers/skills`를 가리킵니다. 별도 submodule 초기화 없이 일반 `git clone`만으로 프로젝트 파일이 준비됩니다.

Superpowers 기준 버전과 프로젝트 로컬 조정 사항은 [.superpowers/UPSTREAM.md](.superpowers/UPSTREAM.md)를 참조합니다.

`AGENTS.md`에는 `multica-ai/andrej-karpathy-skills`의 핵심 원칙을 FileGateway 프로젝트 규칙에 맞게 통합했습니다.

> Windows에서는 Git symlink 체크아웃을 위해 Developer Mode 또는 Git의 symlink 설정이 필요할 수 있습니다.
