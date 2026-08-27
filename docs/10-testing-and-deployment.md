# 테스트 및 배포 설계

## 테스트 계층

### Unit Test

외부 서버 없이 검증한다.

- `filePattern` glob 해석 (`*`, `?`) 및 FTP 서버 wildcard 의미에 비종속
- MVP 파일명 glob matching의 case-insensitive 동작과 원본 casing 보존
- MetadataRule용 relative path의 `/` 구분자 정규화
- path/file Template 해석
- Regex named-group 해석
- timestamp/subtype/attributes 매핑
- Hourly/Daily/Continuous 필터
- 조회 범위로 필요한 디렉터리만 계산하고 무제한 recursive scan하지 않음
- 여러 논리 시간 슬롯이 동일 디렉터리를 계산하는 구조 허용 및 동일 디렉터리 중복 탐색 제거
- 한 디렉터리 안의 여러 시간대 파일을 MetadataRule로 구분해 범위 필터 적용
- Hourly/Daily 로그 시간 범위 규칙
  - `from`/`to` 없음 → 최근 24시간
  - `from`만 있음 → `[from, from + 2일)`
  - `to`만 있음 → `InvalidRequest`
  - `from`/`to` 모두 있음 → 지정 범위
  - `Logs.MaxQueryRange` 초과 → `InvalidRequest`
  - `Logs.MaxQueryRange`가 2일 미만이면 설정 검증 실패
- Continuous에 `from` 또는 `to`가 들어오면 `InvalidRequest`
- Continuous에는 최근 24시간 기본 범위를 적용하지 않음
- Daily timestamp의 Site local `00:00` 처리
- Continuous timestamp가 없는 경우 `null` 처리
- Hourly/Daily 정렬 `timestamp DESC + fileName ASC`
- Hourly/Daily cursor `timestamp + fileName`
- Continuous 정렬 `fileName ASC`
- Continuous cursor `fileName`
- 정렬/cursor/logical identity의 `fileName` case-insensitive 비교
- case-insensitive 기준 동일 파일명 2개 발견 시 `FileDefinitionConflict`
- attribute filter의 case-sensitive 일치
- `cardinality`의 슬롯 단위 검증
- 후보 파일 metadata 파싱 실패 → `FileDefinitionConflict`
- 계산된 목록 디렉터리 없음 → 해당 탐색 결과 0개
- 설비별 Log/Configuration 정의를 제공 파일 종류 응답으로 투영
- 제공 파일 종류 응답이 `logType + generationType`, `configurationType`만 포함하고 내부 경로/rule 정보를 포함하지 않음
- `fileId` 보호/만료/논리 identity/resourceKind
- token payload가 클라이언트에 노출되지 않는 opaque 보호
- Configuration Snapshot `fileId` 재해석 시 완료 marker 재확인
- Snapshot 파일이 남아 있어도 marker가 사라졌으면 `FileNotFound`
- 보호 key rotation 중 이전 key로 발급된 유효 `fileId` 검증
- continuation token의 조회조건 종속성/TTL/stateless cursor
- continuation token 유지 중 `limit` 변경 허용
- direct download multiple-match 판단
- Current Configuration과 History 분리
- Current Configuration case-insensitive `fileName ASC` 정렬
- Current/Snapshot logical identity의 `fileName` case-insensitive 비교
- Configuration History 정렬/cursor의 `fileName` case-insensitive 비교
- `Configurations.HistoryMaxQueryRange` 초과 → `InvalidRequest`
- History marker 파일 존재/부재 판정
- History marker 내용은 읽거나 해석하지 않음
- 정규화 경로의 `rootPath` 경계 검증 및 traversal 차단
- 기준정보 전체 validation 성공 후 cache atomic 교체
- 기준정보 일부 validation 실패 시 전체 refresh 거부
- 기준정보 validation은 구조/문법/invariant만 검사하고 FTP 실재 확인은 수행하지 않음
- lazy refresh single-flight 동작

`IFileAccess` fake/stub으로 Resolver를 독립 테스트한다.

### Integration Test

- MSSQL SP → 내부 Definition 매핑
- cache hit/miss, lazy refresh
- TTL 만료 동시 요청에서 Stored Procedure refresh가 single-flight로 1회 수행되는지 검증
- last-good cache가 있는 refresh 중 다른 요청이 기존 cache로 처리되는지 검증
- 최초 cache 없음 상태의 동시 요청이 동일 refresh 결과를 공유하는지 검증
- 새 기준정보 전체 validation 성공 시 atomic cache 교체
- validation 실패 + last-good cache 존재 시 새 데이터 미적용 및 stale fallback
- 최초 기준정보 validation 실패 + cache 없음 → `ReferenceDataUnavailable`
- 기준정보 refresh/readiness가 FTP 서버 전체를 선제 순회하지 않음
- 설비별 제공 파일 종류 조회가 기준정보 cache만 사용하고 FTP 접근을 발생시키지 않음
- DB 기준정보에 새 `logType`/`configurationType` 추가 후 정상 cache refresh 시 코드 변경 없이 catalog 결과에 반영
- 서로 다른 설비/설비사에 연결된 정의 집합 차이가 catalog 결과 차이로 반영
- FTP 목록/Stat/OpenRead
- FTP timeout/인증/경로 오류
- 목록 대상 디렉터리 부재와 연결/인증/프로토콜 장애 구분
- 디렉터리 부재 시 목록 결과 0개 처리
- 여러 디렉터리 조회 중 하나의 실제 FTP I/O 오류가 발생하면 부분 결과를 반환하지 않고 요청 전체 실패
- FTP 서버 wildcard 기능에 의존하지 않고 목록 후 case-insensitive glob 후보 판정
- case-insensitive 중복 파일명 발견 시 `FileDefinitionConflict`
- root 밖 경로/`..` traversal 정의의 원격 접근 차단
- FileGateway 전체 FTP 동시성 한도와 서버별 한도 적용
- Continuous 파일의 시작 시점 크기 제한
- Continuous 다운로드 중 growth/truncate 처리
- Current Configuration 변경 파일 조회/다운로드
- 완료 marker가 없는 Configuration Snapshot Set 제외
- 완료 marker가 있는 Snapshot Set 포함 및 marker 내용 미사용
- Snapshot `fileId` 발급 후 marker 제거 시 metadata/download가 `FileNotFound`
- IIS/애플리케이션 재시작 후 재시작 전에 발급된 유효 `fileId` 검증

Current Configuration 및 Hourly/Daily 파일의 생산 방식 자체, 원자적 replace 여부, 생산 중 내용 일관성은 FileGateway 테스트 책임에 포함하지 않는다. FileGateway는 이미 저장소에 보이는 파일을 읽는 동작과 외부 변경으로 발생한 I/O 실패 처리를 검증한다.

가능하면 운영과 유사한 IIS FTP 테스트 환경에서 Passive port 동작도 확인한다.

### API Test

- `X-Api-Key` header 인증
- 여러 활성 API Key가 각각 대응 `callerId`로 인증/감사 추적되는지 검증
- 신/구 API Key overlap 활성화 가능 여부 검증
- API Key query string 전달을 인증 수단으로 허용하지 않음
- API Key 누락/오류 모두 `401 InvalidApiKey`
- `GET /api/v1/equipments/{equipmentId}/file-types` 제공 파일 종류 조회
- 제공 파일 종류 조회가 Log의 `logType + generationType`, Configuration의 `configurationType`을 반환하는지 검증
- 존재하지 않는 equipment는 `EquipmentNotFound`, 유효 equipment의 정의 없음은 빈 배열 반환
- 제공 파일 종류 응답에 host/path/rule 등 내부 기준정보가 노출되지 않는지 검증
- 제공 파일 종류 조회가 실제 FTP 파일 존재를 의미하거나 FTP 접근을 수행하지 않는지 검증
- 로그 목록/페이지네이션
- Log 목록 응답이 `{ items, continuationToken }` envelope인지 검증
- 빈 Log 결과가 `items=[]`, `continuationToken=null`인지 검증
- 계산된 디렉터리 부재가 502가 아니라 정상 빈 목록으로 반환되는지 검증
- Hourly/Daily와 Continuous의 정렬/cursor 규칙이 각각 적용되는지 검증
- Continuous `from`/`to` 입력 거부
- case-insensitive 중복 파일명은 `500 FileDefinitionConflict`
- Configuration Current/History API 분리
- Current 응답이 단순 배열이며 case-insensitive `fileName ASC`인지 검증
- History `from`/`to` 필수 검증
- History 최대 조회 기간 검증
- History 목록 응답이 `{ items, continuationToken }` envelope인지 검증
- 페이지 중 조회조건 변경 거부 및 `limit` 변경 허용
- 페이지 사이 원격 파일 집합 변경 시 완전 snapshot을 보장하지 않는 동작
- 공통 `GET /files?fileId=...`가 `fileId`, `fileName`, `size` 최소 metadata만 반환하고 실제 원격 stat을 수행하는지 검증
- Snapshot `fileId` 접근 시 완료 marker 재검증
- `/files?fileId=...` HEAD endpoint가 MVP API에 존재하지 않는지 검증
- fileId 다운로드
- 조건 기반 직접 다운로드
- Problem Details 기반 공통 오류 body와 안정적인 `code`, `traceId`
- 오류 응답에 FTP host/path, stack trace 등 내부정보 비노출
- streaming/cancel 및 `ClientCancelled` 분류
- 다운로드 `Content-Length`, `Content-Type`, `Content-Disposition`
- `Content-Disposition` filename의 header-safe 처리
- 물리 host/path 비노출

## 오픈소스 테스트 도구

자동 통합테스트 환경 구성에는 **Testcontainers.MsSql**을 사용한다.

- MSSQL 등 외부 의존성을 테스트 실행 단위로 격리하는 데 사용한다.
- FTP 테스트 서버는 테스트 전용 `FubarDev.FtpServer`를 사용한다.
- FTP/FTPS 테스트 컨테이너를 사용할 경우에도 `IFileAccess` 계약과 FluentFTP Adapter 동작 검증에 한정한다.
- 컨테이너 환경이 실제 Windows Server + IIS FTP/FTPS의 SSL 설정, Passive port, 방화벽/NAT 동작을 완전히 대체한다고 가정하지 않는다.
- 최종 MVP 완료 조건에는 운영과 유사한 Windows Server/IIS + 실제 MSSQL/FTP 연동 검증이 계속 포함된다.
- Testcontainers MSSQL 이미지는 `latest`를 사용하지 않고 실행 시점의 구체 CU 태그로 고정한다(예: `mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04`). `Testcontainers.MsSql`, `FubarDev.FtpServer`, `FluentFTP` 패키지 버전은 각 `csproj`에 고정된 값으로 기록한다.

Unit/API 테스트 프레임워크는 .NET 기본 테스트 생태계의 단순한 구성을 우선하며, 추가 mocking/assertion framework는 도입하지 않는다.

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
- API Key 목록/FTP credential/DB credential/token 보호 key: Secret/환경변수 등 별도 공급
- token 보호 key는 IIS 프로세스 재시작으로 소실되지 않는 방식으로 공급/보관
- timeout/cache TTL/FTP 전체 동시성/서버별 동시성/continuationToken TTL: 운영 설정 가능
- `Logs.MaxQueryRange`: 운영 설정, 최소 2일 이상이어야 함
- `Configurations.HistoryMaxQueryRange`: 로그 조회 기간과 독립적인 운영 설정

## 배포 전 필수 확인

- HTTPS 인증서/바인딩
- IIS ASP.NET Core Hosting Bundle/권한
- 여러 `X-Api-Key` 인증/호출자 구분 및 query string key 비허용
- API Key 신/구 overlap 회전
- MSSQL 연결
- 설비별 제공 파일 종류 API가 DB 기준정보와 일치하고 FTP 접근 없이 동작
- 기준정보 구조 validation/atomic cache 교체/stale fallback/single-flight 동작
- 기준정보 refresh가 FTP 실재 검사를 수행하지 않는지 확인
- 각 파일 서버 21번 제어 연결
- IIS FTP SSL 설정(FTP vs FTPS)
- Passive 데이터 포트 범위/방화벽
- 실제 파일 목록/다운로드
- 여러 시간 슬롯이 동일 물리 디렉터리를 사용하는 로그 탐색
- 디렉터리 부재/파일 서버 장애/부분 FTP 실패 구분
- FTP 전체/서버별 동시성 제한
- Configuration History 완료 marker 존재 조건과 Snapshot `fileId` 재검증 동작
- token 보호 key 재시작 내구성 및 rotation 시 기존 fileId TTL 유지
- rootPath 경계/traversal 차단
- 로그/Secret에 민감정보 비노출

## MVP 완료 기준

Task 1~20의 자동화 게이트(`dotnet build && dotnet test`)는 구현 완료 조건일 뿐 MVP 완료가 아니다. MVP 완료는 Task 21의 수동 배포 검증 체크리스트(10 문서 "배포 전 필수 확인" + "MVP 완료 기준")까지 통과해야 한다.

`01-requirements.md`의 MVP 기능을 충족하고 아래를 검증해야 완료로 본다.

- Windows Server + IIS 기동
- MSSQL 기준정보 조회/검증/캐시
- 설비별 제공 파일 종류 조회
- 실제 FTP/FTPS 대상 목록/metadata/download
- 대표 로그 규칙
- Current Configuration 및 Configuration Snapshot History 규칙
- API Key/HTTPS
- 감사로그/Health Check
- 주요 오류 시나리오
- 테스트/빌드 성공
