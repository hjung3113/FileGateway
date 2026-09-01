# 배포 검증 체크리스트 (Task 21 — MVP 완료 게이트)

실제 Windows Server + IIS + MSSQL + 파일 서버(FTP/FTPS) 환경에서 수행하는 **수동** 검증 체크리스트입니다. `dotnet build && dotnet test` 자동화 게이트(Task 0~20)는 이 문서를 대체하지 않습니다(계획 확정 결정 18).

- 근거 문서: [`10-testing-and-deployment.md`](10-testing-and-deployment.md) "배포 전 필수 확인" / "MVP 완료 기준", `docs/superpowers/plans/2026-08-23-filegateway-mvp.md` Task 21
- 사용법: 항목별로 `통과` / `차단` 표시하고, 차단이면 원인·조치를 기록합니다. 전 항목 통과해야 "MVP 완료"로 선언합니다. 환경 제약으로 일부를 연기해야 하면 "구현 완료, 배포 검증 보류" 상태로 명시하고 미실행 항목과 이유를 남깁니다.
- 검증 결과는 배포 PR 본문 또는 릴리스 노트에 이 체크리스트를 복사해 기록합니다(새 설계 문서를 만들지 않습니다).

## Step 1. 배포 전 필수 확인

| # | 항목 | 결과 | 원인/비고 |
|---|---|---|---|
| 1 | HTTPS 인증서/바인딩 구성 | ☐ 통과 ☐ 차단 | |
| 2 | IIS ASP.NET Core Hosting Bundle 설치 + 권한 | ☐ 통과 ☐ 차단 | |
| 3 | 여러 `X-Api-Key` 인증/호출자(`callerId`) 구분, query string key 거부(401 실제 확인) | ☐ 통과 ☐ 차단 | |
| 4 | API Key 신/구 overlap 회전(두 key 동시 활성 → 구 key 폐기) | ☐ 통과 ☐ 차단 | |
| 5 | MSSQL 연결(SP `FileGateway_GetReferenceData` 실행, 기준정보 로딩) | ☐ 통과 ☐ 차단 | |
| 6 | 설비별 제공 파일 종류 API가 DB 기준정보와 일치, FTP 접근 없이 동작 | ☐ 통과 ☐ 차단 | |
| 7 | 기준정보 전역 구조 validation/atomic cache 교체/stale fallback/single-flight 및 개별 invalid 정의 격리(잘못된 정의 1건 주입 → 정상 정의 유지·invalid 정의 제외 확인) | ☐ 통과 ☐ 차단 | |
| 8 | 기준정보 refresh가 FTP 실재 검사를 하지 않는지 확인(FTP 중단 상태에서 catalog 정상 응답) | ☐ 통과 ☐ 차단 | |
| 9 | 각 파일 서버 21번 제어 연결(FTP 커맨드 포트) | ☐ 통과 ☐ 차단 | |
| 10 | IIS FTP SSL 설정(FTP vs FTPS) 확인 및 `FileGateway:Ftp:Security` 값 확정 | ☐ 통과 ☐ 차단 | |
| 11 | Passive 데이터 포트 범위/방화벽 개방 | ☐ 통과 ☐ 차단 | |
| 12 | 실제 파일 목록/다운로드(대표 로그 + Current Configuration + History Snapshot) | ☐ 통과 ☐ 차단 | |
| 13 | 여러 시간 슬롯이 동일 물리 디렉터리를 사용하는 로그 탐색 | ☐ 통과 ☐ 차단 | |
| 14 | 디렉터리 부재/파일 서버 장애/부분 FTP 실패 구분(각각 200 빈 목록 / 502 / 전체 실패) | ☐ 통과 ☐ 차단 | |
| 15 | FTP 전체/서버별 동시성 제한(`MaxConcurrentGlobal`/`MaxConcurrentPerServer`, 동시 다운로드 부하 시도) | ☐ 통과 ☐ 차단 | |
| 16 | Configuration History marker 존재 조건, Snapshot `fileId` 재검증(marker 삭제 → 404) | ☐ 통과 ☐ 차단 | |
| 17 | DataProtection 키 재시작 내구성(IIS 재시작 후 기존 `fileId` 유효) + rotation 시 기존 `fileId` TTL 유지 | ☐ 통과 ☐ 차단 | |
| 18 | rootPath 경계/traversal 차단(경계 위반 정의 주입 → 해당 정의 격리, 정상 정의 유지) | ☐ 통과 ☐ 차단 | |
| 19 | 로그/Secret에 민감정보 비노출(API Key/FTP credential/물리 경로 — 감사로그·응용로그 샘플 검토) | ☐ 통과 ☐ 차단 | |

## Step 2. MVP 완료 기준 최종 확인

| # | 항목 | 결과 |
|---|---|---|
| 1 | Windows Server + IIS 기동 | ☐ 통과 ☐ 차단 |
| 2 | MSSQL 기준정보 조회/검증/캐시 | ☐ 통과 ☐ 차단 |
| 3 | 설비별 제공 파일 종류 조회 | ☐ 통과 ☐ 차단 |
| 4 | 실제 FTP/FTPS 대상 목록/metadata/download | ☐ 통과 ☐ 차단 |
| 5 | 대표 로그 규칙(Hourly/Daily/Continuous) | ☐ 통과 ☐ 차단 |
| 6 | Current Configuration 및 Configuration Snapshot History 규칙 | ☐ 통과 ☐ 차단 |
| 7 | API Key/HTTPS | ☐ 통과 ☐ 차단 |
| 8 | 감사로그/Health Check(`/health/live`, `/health/ready`) | ☐ 통과 ☐ 차단 |
| 9 | 주요 오류 시나리오(오류 코드 표 전체) | ☐ 통과 ☐ 차단 |
| 10 | 테스트/빌드 성공(`dotnet build`, `dotnet test`) | ☐ 통과 ☐ 차단 |

## Step 3. 차단 항목 처리

실패/차단 항목은 MVP 완료 선언 전에 수정 후 재검증합니다. 환경 제약으로 일부를 연기해야 하면 아래에 기록하고 "MVP 완료"가 아닌 "구현 완료, 배포 검증 보류" 상태로 둡니다.

```text
미실행 항목:
사유:
재검증 예정일:
```

## 검증 완료 시

- 모든 항목 통과 시 이 파일을 배포 PR 본문/릴리스 노트에 복사해 기록합니다.
- MVP 완료 후 `HANDOFF.md`는 삭제 대상입니다(`HANDOFF.md` 파일 상단 안내 참조).
