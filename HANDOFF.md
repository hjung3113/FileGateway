# HANDOFF

새 에이전트 세션이 FileGateway 작업을 이어받기 위한 상태 문서. 설계 문서가 아니므로 `docs/INDEX.md` 등록 대상이 아니다. 구현 진행 시 이 문서의 체크포인트만 갱신하고, MVP 완료 시 삭제한다.

## 현재 상태 (2026-08-23)

- 설계 확정: `docs/00-glossary.md`~`10-testing-and-deployment.md` + 통합 스냅샷 `docs/superpowers/specs/2026-08-22-filegateway-design.md`
- 구현계획 확정·병합: `docs/superpowers/plans/2026-08-23-filegateway-mvp.md` (PR #1 squash-merge, main `025f53a`). Task 0~21 / 스텝 116개, PR 리뷰 13건(사용자 7 + Codex inline 6) 반영 완료
- 코드: Foundation(Task 0~5) PR #2 merge 완료(`db5de59`). **보강 3건 + 기준정보(Task 6~7) + PR #3 리뷰 6건 수정 + P3 정리 완료** — **PR #3 오픈/merge 대기**(브랜치 `implement/reference-data`, HEAD `5ff5b97`, merge state clean). 게이트 green — 빌드 0 경고, 단위 74/74 + 통합 16/16. `db/` 스크립트는 Task 7 생성
- 브랜치: 다음 세션은 PR #3 merge 후 새 브랜치 분기. SDD ledger: `.superpowers/sdd/2026-08-23-filegateway-mvp/progress.md`

## 다음 작업: 계획 실행

`docs/superpowers/plans/2026-08-23-filegateway-mvp.md`를 Task 0 → 21 순서로 실행. **Task 0~7 + 보강/PR #3 수정 완료**. 다음 세션 순서: **PR #3 merge → Task 8 (Logs — pathTemplate 슬롯 확장)부터 새 브랜치로 계속**.

**보강 3건 해소 내역 (구현 브랜치 커밋):**

1. Token purpose 분리 — `ITokenCodec.Unprotect(token, expectedPurpose)` 계약 변경. protector purpose `filegateway.tokens.v1:{purpose}` 파생, cross-kind 토큰 `Invalid`. 계획 595행 갱신. **Task 11~13 구현 시 각 feature purpose 상수(`fg.fileid.log` 등)를 Unprotect에 전달할 것**
2. `RemotePath.IsUnderRoot` — canonicalize 방식으로 `..` 우회 차단 (`ad78390` 다음 커밋 `dfb8883`)
3. 동기 `Dispose()` 경로 — `OwnedFtpStream`/`ExactLengthStream` 공통 idempotent cleanup (`d3d3a4c`)

**Task 7 룰링 (D4)**: 계획의 `_inFlight` re-arm 코드에 경합 결함(동기 완료 load 시 `_inFlight` 잔존)이 있어 구현이 수정함 — single-flight 의미는 보존. Task 14에서 `FileGatewayException` 확장 시 optional-message ctor 필요할 수 있음.

**PR #3 리뷰 6건 수정 (`ea4998b`/`d20735a`/`9d9b3e8` + P3 `5ff5b97`)**: SP result set 완결성 검사, 최초 load 취소 관찰(`WaitAsync(ct)`, 공유 load는 계속), config path token 화이트리스트, regex anchor 필수, Hourly format 완전성, 전 mapping group 검증. 재리뷰 6/6 ADDRESSED. 사용자 룰링: validator 강화 4건은 계획 Task 6 스케치를 초과하나 리뷰가 우선(ledger 기록).

**다음 세션 참고**: `LogDefinitionValidator`에도 unsafe path + token loop 동일 구조가 있음(P3 수정은 config에만 적용 — 필요 시 동일 guard). timing 기반 cache 테스트(50ms TTL) 변동성 주의.

- **Subagent-Driven** — Task별 fresh subagent + Task 간 리뷰. `superpowers:subagent-driven-development` 스킬 필수
- **Inline** — 이 세션에서 체크포인트별 일괄 실행. `superpowers:executing-plans` 스킬 필수

완료 기준: 각 Task는 계획의 체크박스 스텝 전부 통과 + 커밋. Task 20 통과 = **구현 완료**. **MVP 완료 = Task 21 수동 배포 검증까지**(자동화 게이트가 대체하지 않음).

## 조건별 필독 문서

| 조건 | 문서 |
|---|---|
| 항상 (자동 로드) | `AGENTS.md` — 프로젝트 규칙의 기준. 특히 YAGNI/변경범위 최소화/검증 후 완료 |
| 설계 확인·변경 전 | `docs/INDEX.md` → 안내받은 역할별 문서 |
| 구현 작업 전 | 계획의 **확정 결정 사항 18항목 + Global Constraints** — 구현 중 변경 금지 계약 |
| 용어 생성·해석 전 | `docs/00-glossary.md` 정식 용어 |

## 실행 규칙 (요점)

1. **Task 0(설계문서 동기화) 없이 구현 Task를 시작하지 않는다** — 계획이 확정한 계약을 역할별 문서에 반영하는 선행 조건이다.
2. 계획의 결정 사항 변경이 필요하면 계획 + 해당 역할별 문서를 함께 수정하고 별도 커밋/PR로 남긴다. 구현 도중 임의 해석 금지.
3. 진행 상태는 계획 파일의 `- [ ]` 체크박스에 표시하고, 아래 체크포인트 표도 함께 갱신한다.
4. 테스트 실패/설계와의 충돌 발견 시: 충돌이면 구현을 멈추고 문서를 먼저 정리한다(AGENTS.md 원칙).

## 진행 체크포인트

| Phase | Tasks | 상태 |
|---|---|---|
| 문서 동기화 | 0 | ✅ 완료 (`ea6fd4b`) |
| Foundation (Core/FTP/token) | 1–5 | ✅ 완료 (리뷰 통과, PR) |
| 기준정보 (SP/cache) | 6–7 | ✅ 완료 (보강 3건 포함, 리뷰 통과, `implement/reference-data` PR) |
| Logs | 8–11 | 미시작 |
| Configurations | 12–13 | 미시작 |
| Api | 14–18 | 미시작 |
| 검증/배포 준비 | 19–20 | 미시작 |
| MVP 수동 게이트 | 21 | 미시작 |

## 환경 (2026-08-23 확인)

- .NET 10 SDK **설치 완료** — 10.0.301 (`~/.dotnet`, `/opt/homebrew/bin/dotnet` 심볼릭 링크)
- Docker 29.2.1 구동 중 — Testcontainers(MSSQL) 통합테스트 사용 가능
- 도입 패키지는 계획 Task 1/5/7 고정: FluentFTP, Microsoft.Data.SqlClient, Testcontainers.MsSql, FubarDev.FtpServer(테스트 전용) 외 금지
