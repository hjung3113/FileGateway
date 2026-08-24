# FileGateway 문서 인덱스

설계/구현 작업을 시작할 때 이 문서를 먼저 읽는다. 전체 문서를 순서대로 읽기보다 **현재 작업에 해당하는 문서**를 선택한다.

용어를 해석하거나 새 이름을 만들기 전에는 [00 용어집](00-glossary.md)의 정식 용어를 우선 확인한다.

## 상황별 문서 선택

| 이런 작업을 할 때 | 먼저 볼 문서 | 함께 볼 문서 |
|---|---|---|
| 프로젝트 용어/도메인 명칭 확인 또는 변경 | [00 용어집](00-glossary.md) | 해당 역할별 설계 문서 |
| 프로젝트 목적, MVP 범위, 제외 범위 확인 | [01 요구사항](01-requirements.md) | [07 확장/리스크](07-extension-and-risks.md) |
| 프로젝트/계층 구조 변경 | [02 전체 아키텍처](02-architecture.md) | [03 Server Access Core](03-server-access-core.md), [04.1 Log Provider](04a-log-provider.md), [04.2 Configuration Provider](04b-configuration-provider.md) |
| 오픈소스/외부 라이브러리 도입·교체 | [02 전체 아키텍처](02-architecture.md) | FTP/FTPS는 [03 Server Access Core](03-server-access-core.md), 테스트 도구는 [10 테스트/배포](10-testing-and-deployment.md) |
| FTP/FTPS 파일 접근, 목록/Stat/Stream 구현 | [03 Server Access Core](03-server-access-core.md) | [09 보안/운영](09-security-and-operations.md) |
| 로그 탐색, 시간/속성 필터, discovery 규칙 구현 | [04.1 Log Provider](04a-log-provider.md) | [06 DB/기준정보](06-reference-data.md), [05 API 인터페이스](05-api-interface.md) |
| Current Configuration/History 탐색 구현 | [04.2 Configuration Provider](04b-configuration-provider.md) | [06 DB/기준정보](06-reference-data.md), [05 API 인터페이스](05-api-interface.md) |
| 설비별 제공 가능한 `logType`/`configurationType` 조회 구현 | [05 API 인터페이스](05-api-interface.md) | [06 DB/기준정보](06-reference-data.md), [04.1 Log Provider](04a-log-provider.md), [04.2 Configuration Provider](04b-configuration-provider.md) |
| HTTP endpoint, fileId, pagination, 다운로드 계약 변경 | [05 API 인터페이스](05-api-interface.md) | [09 보안/운영](09-security-and-operations.md) |
| MSSQL SP 결과나 탐색/파싱/제공 파일 기준정보 변경 | [06 DB/기준정보](06-reference-data.md) | [04.1 Log Provider](04a-log-provider.md), [04.2 Configuration Provider](04b-configuration-provider.md) |
| 향후 Linux/Site/credential/다른 프로토콜 확장 판단 | [07 확장/리스크](07-extension-and-risks.md) | [02 전체 아키텍처](02-architecture.md) |
| Agent/Superpowers/심볼릭 링크 관리 | [08 Agent Tooling](08-agent-tooling.md) | `AGENTS.md`, `.superpowers/UPSTREAM.md` |
| 인증, Secret, token, 감사로그, Health, 장애/timeout 정책 | [09 보안/운영](09-security-and-operations.md) | [05 API 인터페이스](05-api-interface.md) |
| 테스트 전략, IIS 배포, MVP 완료 검증 | [10 테스트/배포](10-testing-and-deployment.md) | [01 요구사항](01-requirements.md) |
| 실 배포 환경 수동 검증 진행(Task 21) | [배포 검증 체크리스트](DEPLOYMENT-CHECKLIST.md) | [10 테스트/배포](10-testing-and-deployment.md) |
| 확정된 전체 설계의 단일 스냅샷이 필요할 때 | [Superpowers 설계 Spec](superpowers/specs/2026-08-22-filegateway-design.md) | 역할별 문서를 최신 기준으로 사용 |

## 문서 구조

0. `00-glossary.md` — 프로젝트 정식 용어와 피해야 할 혼용 표현
1. `01-requirements.md` — 무엇을 만들고 무엇을 만들지 않는가
2. `02-architecture.md` — 계층, 의존성 경계, 오픈소스/외부 라이브러리 원칙
3. `03-server-access-core.md` — 프로토콜 비종속 파일 접근 계약과 FTP/FTPS 구현 경계
4. **Feature Providers** — 파일 종류별 업무 규칙
   - 4.1 `04a-log-provider.md` — 로그 도메인, Resolver, discovery/metadata/filter 규칙
   - 4.2 `04b-configuration-provider.md` — Configuration Current/History 도메인과 Provider 책임
5. `05-api-interface.md` — 외부 HTTP API 계약, fileId/download, 설비별 제공 파일 종류 조회
6. `06-reference-data.md` — MSSQL SP, 설비별 제공 파일 정의, 기준정보 검증/캐시
7. `07-extension-and-risks.md` — MVP 밖 확장과 남은 리스크
8. `08-agent-tooling.md` — 개발 Agent/Skill 운영
9. `09-security-and-operations.md` — 인증/Secret/token/감사/장애/Health 운영 정책
10. `10-testing-and-deployment.md` — 테스트 계층, Testcontainers 활용, IIS 배포, 완료 검증

## 문서 우선순위

- 정식 용어는 `00-glossary.md`를 기준으로 한다.
- 역할별 문서가 현재 구현 기준이다.
- `docs/superpowers/specs/...`는 승인된 전체 설계의 통합 스냅샷이다.
- 문서 간 충돌이 발견되면 구현 전에 충돌을 해소하고 관련 문서를 함께 수정한다.
