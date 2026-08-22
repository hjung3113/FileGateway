# FileGateway

설비 로그 및 향후 확장 파일을 클라이언트에 제공하기 위한 파일 게이트웨이 프로젝트입니다.

클라이언트는 실제 공정/설비별 서버 분산 구조와 물리 파일 경로를 알 필요 없이 **설비명과 조회 조건**만으로 파일 목록을 조회하고 다운로드합니다.

## 문서

- [요구사항](docs/01-requirements.md)
- [전체 아키텍처](docs/02-architecture.md)
- [Server Access Core](docs/03-server-access-core.md)
- [Log Provider](docs/04-log-provider.md)
- [API / 클라이언트 인터페이스](docs/05-api-interface.md)
- [DB 및 기준정보 연계](docs/06-reference-data.md)
- [확장성 및 주요 리스크](docs/07-extension-and-risks.md)

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

## Superpowers

Claude Code 프로젝트 범위에서 `superpowers@claude-plugins-official`을 사용하도록 `.claude/settings.json`에 설정되어 있습니다. 프로젝트를 신뢰하고 Claude Code를 시작하면 프로젝트 설정으로 플러그인을 사용할 수 있습니다.
