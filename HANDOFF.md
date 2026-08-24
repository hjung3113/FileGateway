# HANDOFF

새 에이전트 세션이 FileGateway 작업을 이어받기 위한 상태 문서. 설계 문서가 아니므로 `docs/INDEX.md` 등록 대상이 아니다. 구현 진행 시 이 문서의 체크포인트만 갱신하고, MVP 완료 시 삭제한다.

- 설계 확정: `docs/00-glossary.md`~`10-testing-and-deployment.md` + 통합 스냅샷 `docs/superpowers/specs/2026-08-22-filegateway-design.md`
- 구현계획 확정·병합: `docs/superpowers/plans/2026-08-23-filegateway-mvp.md` (PR #1 squash-merge, main `025f53a`). Task 0~21 / 스텝 116개
- **Task 0~20 전체 자동화 구현 완료, main에 전부 merge됨.** 최종 게이트(main HEAD, 컨트롤러 직접 검증): 빌드 0 경고, 단위 166/166 + 통합 82/82.
  - Foundation(PR #2) + 기준정보(PR #3) + Logs(PR #4 `af01bad`) + Configurations(PR #5 `6283349`) + Api(PR #6 `c58ca27`) + 검증/배포 준비(PR #7 `7c1e741`) + PR #6 사후 follow-up(PR #8 `bff8551`) 전부 merge 완료.
  - PR별 상세 이력은 SDD ledger 참조. 요약: 매 phase마다 (a) task-scoped subagent 리뷰(spec+quality), (b) phase 단위 PR 오픈 후 Opus5(diff+PR body만) whole-branch 리뷰, (c) 발견 사항은 실코드로 컨트롤러가 직접 검증 후 fix wave → scoped 재검토, (d) 사용자가 GitHub에 직접 남긴 inline 코멘트도 동일하게 검증→fix→재검토 절차를 거쳐 merge.
  - **주목할 반복 패턴**: `Audit.FileId`/`Audit.FileSize` 등 감사 필드 누락이 Task 16(로그 다운로드) → Task 17(구성 다운로드) → Task 18(공통 파일 fileId 재해석) → PR #6 사후 코멘트(`FileEndpoints`)까지 4번 연속 재발. 매번 task review 또는 PR 레벨 리뷰가 잡아냈지만, 향후 유사 엔드포인트를 추가할 때는 "다운로드 성공 시 Audit.FileId/FileName/FileSize(+도메인별 EquipmentId/LogType 또는 ConfigurationType) 전부 설정" 여부를 신규 코드 체크리스트에 명시적으로 포함할 것.
- SDD ledger: `.superpowers/sdd/2026-08-23-filegateway-mvp/progress.md` — 이 파일에 Task별/PR별 전체 이력이 있음. 요약이 부족하면 이 파일을 먼저 확인한다.

## 다음 작업: Task 21 (수동 배포 검증) — MVP 완료 조건

Task 0~20(자동화 구현)은 모두 완료됐다. **남은 것은 Task 21 하나뿐**이며, 이것은 계획상 명시적으로 수동 게이트다(확정 결정 18: 자동화 게이트가 이를 대체하지 않는다). 자동화 에이전트/subagent가 대신 수행할 수 없다 — 실제 Windows/IIS 환경에 배포하고 검증하는 작업이다.

`docs/superpowers/plans/2026-08-23-filegateway-mvp.md`의 Task 21 섹션에서 정확한 체크리스트를 확인할 것. 완료 후 이 HANDOFF.md는 삭제 대상이다(MVP 완료 시 삭제 원칙, 파일 상단 안내 참조).

## 배경 참고 (완료된 항목, 필요시 참조)

**보강 3건 해소 내역 (Foundation 단계):**

1. Token purpose 분리 — `ITokenCodec.Unprotect(token, expectedPurpose)` 계약. protector purpose `filegateway.tokens.v1:{purpose}` 파생, cross-kind 토큰 `Invalid`.
2. `RemotePath.IsUnderRoot` — canonicalize 방식으로 `..` 우회 차단.
3. 동기 `Dispose()` 경로 — `OwnedFtpStream`/`ExactLengthStream` 공통 idempotent cleanup.

**Task 7 룰링 (D4)**: 계획의 `_inFlight` re-arm 경합 결함을 구현이 수정 — single-flight 의미는 보존.

**다음 세션 참고**: timing 기반 cache 테스트(TTL 짧은 값 사용) 변동성 주의 — 여러 phase에서 반복 발견된 패턴.

- **Subagent-Driven** — Task별 fresh subagent + Task 간 리뷰. `superpowers:subagent-driven-development` 스킬 필수. 이번 MVP 전체 구현이 이 방식으로 진행됨(omp: judgment→glm-5.3, mechanical fix→gpt-5.6-luna max).

완료 기준: 각 Task는 계획의 체크박스 스텝 전부 통과 + 커밋. **Task 20 통과 = 구현 완료(달성).** **MVP 완료 = Task 21 수동 배포 검증까지**(다음 세션의 유일한 남은 작업).

## 조건별 필독 문서

| 조건 | 문서 |
|---|---|
| 항상 (자동 로드) | `AGENTS.md` — 프로젝트 규칙의 기준. 특히 YAGNI/변경범위 최소화/검증 후 완료 |
| 설계 확인·변경 전 | `docs/INDEX.md` → 안내받은 역할별 문서 |
| Task 21 수행 전 | 계획의 Task 21 섹션 + `docs/10-testing-and-deployment.md` "배포 전 필수 확인" + `README.md` 실행/배포 섹션 |
| 용어 생성·해석 전 | `docs/00-glossary.md` 정식 용어 |

## 진행 체크포인트

| Phase | Tasks | 상태 |
|---|---|---|
| 문서 동기화 | 0 | ✅ 완료 (`ea6fd4b`) |
| Foundation (Core/FTP/token) | 1–5 | ✅ 완료 (PR #2 merge) |
| 기준정보 (SP/cache) | 6–7 | ✅ 완료 (PR #3 merge) |
| Logs | 8–11 | ✅ 완료 (PR #4 `af01bad` merge) |
| Configurations | 12–13 | ✅ 완료 (PR #5 `6283349` merge) |
| Api | 14–18 | ✅ 완료 (PR #6 `c58ca27` merge + 사후 follow-up PR #8 `bff8551` merge) |
| 검증/배포 준비 | 19–20 | ✅ 완료 (PR #7 `7c1e741` merge) |
| **MVP 수동 게이트** | **21** | **다음 세션 대상 — 유일하게 남은 작업** |

## 환경 (2026-08-23 확인)

- .NET 10 SDK **설치 완료** — 10.0.301 (`~/.dotnet`, `/opt/homebrew/bin/dotnet` 심볼릭 링크)
- Docker 29.2.1 구동 중 — Testcontainers(MSSQL) 통합테스트 사용 가능
- 도입 패키지는 계획 Task 1/5/7 고정: FluentFTP, Microsoft.Data.SqlClient, Testcontainers.MsSql, FubarDev.FtpServer(테스트 전용) 외 금지
