# 테스트 및 배포 설계

## 테스트 계층

### Unit Test

외부 서버 없이 검증한다.

- path/file Template 해석
- Regex named-group 해석
- timestamp/subtype/attributes 매핑
- Hourly/Daily/Continuous 필터
- 로그 시간 범위 규칙
  - `from`/`to` 없음 → 최근 24시간
  - `from`만 있음 → `[from, from + 2일)`
  - `to`만 있음 → `InvalidRequest`
  - `from`/`to` 모두 있음 → 지정 범위
  - 최대 조회 기간 초과 → `InvalidRequest`
- Daily timestamp의 Site local `00:00` 처리
- Continuous timestamp가 없는 경우 `null` 처리
- attribute filter의 case-sensitive 일치
- `cardinality`의 슬롯 단위 검증
- 후보 파일 metadata 파싱 실패 → `FileDefinitionConflict`
- `fileId` 서명/만료/논리 identity
- continuation token의 조회조건 종속성
- continuation token 유지 중 `limit` 변경 허용
- direct download multiple-match 판단
- Current Configuration과 History 분리
- Configuration History 정렬

`IFileAccess` fake/stub으로 Resolver를 독립 테스트한다.

### Integration Test

- MSSQL SP → 내부 Definition 매핑
- cache hit/miss, lazy refresh, stale cache fallback
- 시작 후 기준정보 미확보 상태의 `ReferenceDataUnavailable`
- FTP 목록/Stat/OpenRead
- FTP timeout/인증/경로 오류
- Continuous 파일의 시작 시점 크기 제한
- Continuous 다운로드 중 growth/truncate 처리
- Current Configuration 변경 파일 조회/다운로드
- 불변 Configuration Snapshot History 조회

가능하면 운영과 유사한 IIS FTP 테스트 환경에서 Passive port 동작도 확인한다.

### API Test

- API Key 인증
- 로그 목록/페이지네이션
- Configuration Current/History API 분리
- History `from`/`to` 필수 검증
- 페이지 중 조회조건 변경 거부 및 `limit` 변경 허용
- 페이지 사이 원격 파일 집합 변경 시 완전 snapshot을 보장하지 않는 동작
- 공통 `/files/{fileId}`가 `fileId`, `fileName`, `size` 최소 metadata만 반환하는지 검증
- 파일 정보/HEAD의 실제 원격 stat 수행
- fileId 다운로드
- 조건 기반 직접 다운로드
- 대표 오류 코드
- streaming/cancel 및 `ClientCancelled` 분류
- 다운로드 `Content-Length`, `Content-Type`, `Content-Disposition`
- 물리 host/path 비노출

## 배포 구조

```text
Windows Server
└─ IIS
   └─ ASP.NET Core FileGateway
      ├─ MSSQL
      └─ Distributed FTP/FTPS File Servers
```

MVP는 Windows Server/IIS에서 실제 운영 검증한다. Linux 배포는 이번 완료 조건이 아니다.

## 설정 분리

- 공통 비민감 설정: `appsettings.json`
- 환경별 비민감 설정: `appsettings.<Environment>.json`
- API Key/FTP credential/DB credential: Secret/환경변수 등 별도 공급
- timeout/cache TTL/concurrency limit/로그 최대 조회 기간: 운영 설정 가능

## 배포 전 필수 확인

- HTTPS 인증서/바인딩
- IIS ASP.NET Core Hosting Bundle/권한
- MSSQL 연결
- 각 파일 서버 21번 제어 연결
- IIS FTP SSL 설정(FTP vs FTPS)
- Passive 데이터 포트 범위/방화벽
- 실제 파일 목록/다운로드
- 로그/Secret에 민감정보 비노출

## MVP 완료 기준

`01-requirements.md`의 MVP 기능을 충족하고 아래를 검증해야 완료로 본다.

- Windows Server + IIS 기동
- MSSQL 기준정보 조회/캐시
- 실제 FTP/FTPS 대상 목록/metadata/download
- 대표 로그 규칙
- Current Configuration 및 Configuration Snapshot History 규칙
- API Key/HTTPS
- 감사로그/Health Check
- 주요 오류 시나리오
- 테스트/빌드 성공
