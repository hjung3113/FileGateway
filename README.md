# FileGateway

분산 파일 서버에 저장된 설비 로그 및 향후 추가 파일을 클라이언트에 제공하는 파일 게이트웨이 프로젝트입니다.

클라이언트는 실제 서버 분산 구조와 물리 파일 경로를 알 필요 없이 **설비명과 논리 조회 조건**만으로 파일을 조회하고 다운로드합니다.

> 설비에서 로그를 직접 수집·가공하는 기능은 별도 시스템의 책임이며 FileGateway 범위에 포함하지 않습니다.

## 문서

설계나 요구사항을 확인할 때는 먼저 [문서 인덱스](docs/INDEX.md)를 확인합니다. 인덱스에 작업 상황별로 읽어야 할 문서를 정리합니다.

## 핵심 구조

```text
Client (.NET / Python / WPF / Web Backend / Other Server)
                         |
                      HTTPS
                         |
                  FileGateway.Api
                         |
                  FileGateway.Logs
                         |
          +--------------+--------------+
          |                             |
      MSSQL SP                     IFileAccess
                                         |
                                  FTP/FTPS Adapter
                                         |
                              Distributed File Servers
```

MVP 서버는 ASP.NET Core/.NET을 Windows Server의 IIS에서 호스팅합니다. Linux 배포는 후속 확장 사항입니다.

## Agent 개발 환경

프로젝트 공통 지침은 `AGENTS.md`를 기준으로 합니다.

- `CLAUDE.md` → `AGENTS.md` 심볼릭 링크
- Superpowers runtime skills: `.superpowers/skills`에 일반 Git 파일로 포함
- Claude: `.claude/skills/superpowers`
- Codex: `.agents/skills/superpowers`
- OpenCode: `.opencode/skills/superpowers`
- OMP: `.omp/skills/superpowers`

Superpowers 기준 버전과 프로젝트 로컬 조정 사항은 [.superpowers/UPSTREAM.md](.superpowers/UPSTREAM.md)를 참조합니다.
