# 테스트 및 배포 설계

## 테스트 계층

### Unit Test

외부 서버 없이 검증한다.

- path/file Template 해석
- Regex named-group 해석
- timestamp/subtype/attributes 매핑
- Hourly/Daily/Continuous 필터
- 최근 24시간 기본 범위
- attribute filter
- `fileId` 서명/만료
- continuation token
- direct download multiple-match 판단

`IFileAccess` fake/stub으로 Resolver를 독립 테스트한다.

### Integration Test

- MSSQL SP → 내부 Definition 매핑
- cache hit/miss 및 DB 장애 fallback
- FTP 목록/Stat/OpenRead
- FTP timeout/인증/경로 오류
- 계속 갱신되는 파일의 시작 시점 크기 제한

가능하면 운영과 유사한 IIS FTP 테스트 환경에서 Passive port 동작도 확인한다.

### API Test

- API Key 인증
- 목록/페이지네이션
- 파일 정보/HEAD
- fileId 다운로드
- 조건 기반 직접 다운로드
- 대표 오류 코드
- streaming/cancel
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
- timeout/cache TTL/concurrency limit: 운영 설정 가능

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
- 대표 EventLog 및 ConfigurationLog 규칙
- API Key/HTTPS
- 감사로그/Health Check
- 주요 오류 시나리오
- 테스트/빌드 성공
