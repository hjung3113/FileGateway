# HANDOFF

새 에이전트 세션이 FileGateway 작업을 이어받기 위한 상태 문서. 설계 문서가 아니므로 `docs/INDEX.md` 등록 대상이 아니다. 구현 진행 시 이 문서의 체크포인트만 갱신하고, MVP 완료 시 삭제한다.

- 설계 확정: `docs/00-glossary.md`~`10-testing-and-deployment.md` + 통합 스냅샷 `docs/superpowers/specs/2026-08-22-filegateway-design.md`
- 구현계획 확정·병합: `docs/superpowers/plans/2026-08-23-filegateway-mvp.md` (PR #1 squash-merge, main `025f53a`). Task 0~21 / 스텝 116개
- 코드: Foundation(PR #2) + 기준정보(PR #3) + **Logs(PR #4, squash `af01bad`) merge 완료**. 게이트 green — 빌드 0 경고, 단위 148/148 + 통합 16/16. 사용자 inline 리뷰 P2 5건(range drift 커서 고정, subtype 진입 정규화, offset 명시적 분류, cross-directory identity 검사, 중복 range 코멘트) `9a03b2e`로 해소 후 merge. glm-5.3 high PR 리뷰(본문+diff) APPROVED 전제
- 브랜치: **Configurations(Task 12~13) merge 완료 (PR #5 squash `6283349`)**. 게이트 green — 빌드 0 경고, 단위 166/166 + 통합 16/16. 사용자 inline P2 3건(marker glob 제외, limit 최댓값, Current fileId stat — Logs에도 limitMaximum 대칭 적용) `848c4fc`로 해소 후 merge. glm-5.3 high 리뷰 APPROVED(P1/P2 0건, P3 7건 중 2건 fix·5건 deferred).
- 브랜치: **Api(Task 14~18) merge 완료 (PR #6 squash `c58ca27`)**. 게이트 green — 빌드 0 경고, 단위 166/166 + 통합 63/63. Task별 omp subagent 디스패치(judgment→glm-5.3 low/구현·high/리뷰, mechanical fix→gpt-5.6-luna max) + task review + fix loop 전부 통과. Task 16/17에서 동일 패턴(plan-mandated Audit.FileId 누락)이 반복 발견·수정됨 — Task 18은 사전 룰링으로 회피(ITokenCodec purpose 3종 순차 시도), 그런데 **Task 18 자신의 Audit.FileSize/EquipmentId/LogType 누락**이 세 번째 반복으로 PR 레벨 리뷰(Opus5, diff+PR body만)에서 Critical로 발견됨 — fix wave(`0cc0003`)로 해소, glm-5.3 high 재검토 5/5 ADDRESSED. Opus5 리뷰 Important 4건 중 1건(subtype/attr path traversal)은 컨트롤러 검증 결과 **false positive**(ApplyFilters가 listing 이후 순수 필터링, path 조합 미관여) — 반박 근거 ledger 기록. 나머지 3건(appsettings.json Logging 누락, IFileAccess 수명 오기재, Content-Disposition backslash 미이스케이프)도 fix wave에 포함해 해소.
- 브랜치: **검증/배포 준비(Task 19~20) 구현 완료** — `implement/verification` (HEAD `eeb22d6`). 게이트 green — 빌드 0 경고, 단위 166/166 + 통합 68/68. Task 19: 실 MSSQL 컨테이너+실 in-proc FTP+실 DataProtection, 서비스 오버라이드 전무한 E2E 시나리오 2건(리뷰 confirmed "provably failure-preserving"). Task 20: DataProtection key 내구성, appsettings.json 전체 구조 확정, web.config 신설, README 갱신. **Task 0~20 전체 자동화 구현 완료.** PR #7 오픈, Opus5(diff+PR body만, 529 재시도 3회 끝 성공) 리뷰 Approve with comments → fix wave(`6d995ba`) → 재검토 clean. **이후 사용자가 GitHub에 직접 inline 리뷰 코멘트 2건 추가**(appsettings `FileIdTtl` TimeSpan 파싱 버그 — `"24:00:00"`이 24시간이 아니라 24일로 파싱됨 확인됨·DataProtection key at-rest 미보호) → 둘 다 확인 후 fix(`eeb22d6`), 사용자 코멘트에 답글. **merge는 사용자 보류 중**(PR #7 오픈 상태).
- 별도: **PR #6(이미 merge됨) 사후 코멘트 3건** — 사용자가 merge 후 재검증하여 P1 1건(`FileAccessException`이 `ErrorMappingMiddleware`에서 매핑 안 되고 항상 500으로 뭉개짐, Task 8부터 존재하던 미해소 gap) + P2 2건(`/health/ready`가 stale 서빙 중에도 무조건 `Healthy`, 다운로드 audit가 resolve-time 크기 기록해 open-time과 race 가능) 발견 → 별도 워크트리(`main`에서 분기, branch `hjung3113/pr6-followup`)에서 fix, task review Approved → **PR #8 오픈**(merge 대기).
- SDD ledger: `.superpowers/sdd/2026-08-23-filegateway-mvp/progress.md`

## 다음 작업

계획(Task 0~20) 자동화 구현은 전부 완료. 열려 있는 PR 2개가 다음 세션의 최우선 순서:
- **PR #7** (`implement/verification` → main): Task 19-20 + 사용자 코멘트 2건 수정 완료, merge 보류 중 — 사용자 승인 대기
- **PR #8** (`hjung3113/pr6-followup` → main): PR #6 사후 발견 3건 수정, task review Approved, merge 대기

두 PR merge 후: **Task 21 (수동 배포 검증)** — 사용자 직접 수행, MVP 완료 조건, 자동화 게이트로 대체 불가.

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
| Logs | 8–11 | ✅ 완료 (PR #4 `af01bad` merge — luna max/glm-5.3 혼합 디스패치, task 리뷰+glm-5.3 high PR 리뷰+사용자 inline 5건 해소) |
| Configurations | 12–13 | ✅ 완료 (PR #5 `6283349` merge — task 리뷰 2건 + glm-5.3 high APPROVED + 사용자 inline P2 3건 해소) |
| Api | 14–18 | ✅ 완료 (PR #6 `c58ca27` merge — task 리뷰 5건 + Opus5 PR 리뷰 Request changes→fix wave→재검토 clean, 단위 166 + 통합 63). 사후 발견 3건은 **PR #8**(merge 대기)로 해소 |
| 검증/배포 준비 | 19–20 | ✅ 구현 완료 (`eeb22d6`) — task 리뷰 2건 Approved + Opus5 PR 리뷰(재시도 3회)→fix wave→재검토 clean + 사용자 GitHub 코멘트 2건 해소. **PR #7 merge 보류 중**, 단위 166 + 통합 68 |
| MVP 수동 게이트 | 21 | 미시작 (PR #7·#8 merge 후 다음 대상) |

## 환경 (2026-08-23 확인)

- .NET 10 SDK **설치 완료** — 10.0.301 (`~/.dotnet`, `/opt/homebrew/bin/dotnet` 심볼릭 링크)
- Docker 29.2.1 구동 중 — Testcontainers(MSSQL) 통합테스트 사용 가능
- 도입 패키지는 계획 Task 1/5/7 고정: FluentFTP, Microsoft.Data.SqlClient, Testcontainers.MsSql, FubarDev.FtpServer(테스트 전용) 외 금지
