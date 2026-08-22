# DB 및 기준정보 연계

## DB

- Microsoft SQL Server
- FileGateway는 Stored Procedure로 기준정보를 조회한다.
- DB 테이블 구조는 애플리케이션/API에 노출하지 않는다.

## SP가 제공해야 하는 논리 정보

### 서버/설비 매핑

- equipmentId
- serverId
- host 또는 서버 연결에 필요한 비민감 식별정보
- rootPath

`equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자이며 하나의 FileGateway 배포 범위 안에서 유일하다.

`rootPath`는 해당 서버에서 FileGateway가 접근할 수 있는 물리 경로의 보안 경계다. 모든 discovery/current/history 규칙의 최종 정규화 경로는 반드시 이 root 아래에 있어야 한다.

### 로그 정의

- logType
- generationType: Hourly / Daily / Continuous
- pathTemplate
- filePattern
- cardinality
- metadata mode/pattern/mapping

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 로그 정의를 식별한다. 동일 조합의 중복 정의는 기준정보 오류다.

MVP에서 하나의 로그 정의는 하나의 discovery rule만 가진다. 현재 동일 `logType` 조회에서 서로 다른 디렉터리/파일명 규칙을 동시에 검색해야 하는 사례가 없으므로 다중 discovery rule 모델은 두지 않는다. 같은 생성 슬롯의 여러 파일은 `cardinality=Multiple`로 표현한다.

`pathTemplate`은 조회조건과 논리 생성 슬롯으로 탐색할 논리 디렉터리를 계산한다. `filePattern`은 해당 디렉터리에서 후보 **파일명**을 선택하는 glob matcher다. 예: `*.zip`, `Event_*.log`, `PM?.cfg`. `timestamp`/`subtype`/`attributes` 추출은 metadata rule이 담당한다.

`filePattern`은 정규식으로 사용하지 않는다. FTP 서버 자체 wildcard 구현에도 의존하지 않고 FileGateway가 받은 디렉터리 목록에 동일한 glob 의미를 적용한다. MVP Windows/IIS FTP 환경에서는 파일명 glob 비교를 case-insensitive로 수행하고 실제 casing은 응답에 보존한다.

MVP에서는 root부터 하위 전체를 훑는 무제한 recursive scan을 허용하지 않는다. `pathTemplate`이 Hourly/Daily 조회 범위 또는 Continuous 현재 슬롯에서 필요한 디렉터리를 직접 계산해야 한다. 여러 슬롯이 같은 디렉터리를 계산하면 중복 목록 조회하지 않는다.

MetadataRule은 물리 FTP root를 제외한 **정규화된 논리 relative path + fileName**을 입력으로 사용한다. 경로 구분자는 플랫폼/FTP 표현과 무관하게 `/`로 통일한다. 단순한 결정적 레이아웃은 `Template`, 복잡한 예외는 `Regex` named group을 사용한다.

후보 파일이 `filePattern`에 일치했지만 필수 metadata를 해석하지 못하면 누락시키지 않고 `FileDefinitionConflict`로 취급한다.

`logType`은 업무적인 로그 종류이고 `generationType`은 파일 생성 주기/생명주기다. 두 값을 같은 분류로 취급하지 않는다.

로그 `timestamp`는 파일명/경로 규칙에서 추출한 논리 시각이다. FTP modified time과 구분하며 timezone 정보가 없으면 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다. Daily 로그의 논리 `timestamp`는 해당 날짜의 Site local `00:00`이다. Continuous 로그에 명확한 논리 시각이 없으면 `timestamp`는 `null`이다.

`subtype` 및 동적 attribute 값은 외부 조회에서 정확한 문자열 일치(case-sensitive)를 사용한다. 대소문자 비구분이 필요한 업무 값은 기준정보/파싱 단계에서 canonical value로 정규화한다.

`cardinality`는 전체 조회 결과 수가 아니라 논리 생성 슬롯당 파일 개수 invariant다.

- Hourly: 각 시간 슬롯
- Daily: 각 날짜 슬롯
- Continuous: 현재 슬롯
- `Single`: 슬롯당 최대 1개
- `Multiple`: 같은 슬롯에 여러 파일 허용

`cardinality=Single`인데 하나의 슬롯에서 실제 결과가 2개 이상이면 정상적인 다중 결과가 아니라 기준정보/파일 상태 불일치로 취급한다.

### Configuration 정의

Configuration은 로그 정의와 별도로 관리한다.

개념적으로 하나의 Configuration 정의는 다음 논리 정보를 가진다.

```text
EquipmentConfigurationDefinition
- equipmentId
- configurationType
- serverId
- currentRule
- historyRule
```

- `currentRule`: Current Configuration File 집합의 위치와 후보 파일 패턴을 해석하는 규칙
- `historyRule`: 날짜별 History 디렉터리/파일 패턴, Snapshot Set의 `snapshotTimestamp`, 완료 marker 파일명/위치를 해석하는 규칙

하나의 `equipmentId + configurationType` 아래 PM1/PM2/PM3/PM4처럼 여러 Current Configuration File이 존재할 수 있다. 이 파일들을 별도 `configurationType`, `subtype`, `attributes`로 세분화하지 않는다.

Current Configuration File의 logical identity는 `equipmentId + configurationType + fileName`이다. MVP에서 `fileName` 구성요소는 case-insensitive로 비교하며 casing만 다른 이름은 같은 논리 파일로 취급한다.

History는 별도 시스템이 자정에 날짜 폴더를 만들고 Current 파일 집합을 그대로 복사한 결과다. 같은 날짜 폴더의 Snapshot File들은 동일한 `snapshotTimestamp`를 공유하며 현재 운영 계획에서는 Site local `00:00`으로 해석한다. FTP modified time은 snapshot 시각으로 사용하지 않는다.

History 생산자는 Snapshot Set 복사 완료 시 marker 파일을 생성한다. marker 파일명/위치는 `historyRule`에 있으며 FileGateway는 **marker 존재 여부만 확인**한다. marker 내용은 읽거나 해석하지 않는다. marker가 없는 날짜 폴더는 History 결과에 포함하지 않으며 marker 자체도 Configuration File 후보로 반환하지 않는다.

Configuration Snapshot File의 logical identity는 `equipmentId + configurationType + snapshotTimestamp + fileName`이다. `fileName` 구성요소는 case-insensitive로 비교한다.

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

MVP Configuration 정의에는 `subtype`/동적 `attributes` 규칙을 추가하지 않는다.

FTP 비밀번호 등 credential은 SP에서 반환하지 않는다.

## 파일명 비교 규칙

MVP Windows/IIS FTP 환경에서 파일명 관련 비교는 case-insensitive다.

- `filePattern` 및 Configuration 후보 파일명 matching
- Log/Current/Snapshot logical identity의 `fileName`
- `fileName ASC` 정렬
- continuation cursor의 `fileName`

실제 원격 파일 casing은 API 응답에 그대로 보존한다. `subtype`/`attributes` 비교는 이 규칙과 무관하게 기존대로 case-sensitive다. 향후 case-sensitive 저장소를 도입할 때 파일명 비교 계약을 재검토한다.

동일한 탐색 범위에서 case-insensitive 기준으로 같은 파일명인 서로 다른 원격 항목이 둘 이상 발견되면 임의 dedupe하지 않고 `FileDefinitionConflict`로 처리한다. 예: `PM1.cfg`와 `pm1.cfg`가 동시에 존재하는 경우다.

## 경로 안전성 invariant

기준정보에서 계산되는 모든 파일/디렉터리 경로는 다음 조건을 만족해야 한다.

- 최종 정규화 경로는 해당 `ServerDefinition.rootPath` 아래에 있어야 한다.
- `..`, 절대 경로, rooted path 등으로 `rootPath` 밖으로 탈출할 수 없어야 한다.
- Log `pathTemplate`, Configuration `currentRule`/`historyRule`, History 완료 marker 경로 모두 같은 경계를 적용한다.
- MetadataRule 입력용 relative path는 root 제외 후 `/` 구분자로 정규화한다.
- 클라이언트 요청값을 raw 물리 경로 세그먼트로 사용하지 않는다.
- 경계 위반 정의는 실제 원격 접근에 사용하지 않고 기준정보 오류로 취급한다.

## fileId 재해석과 기준정보 변경

`fileId`는 물리 host/path를 저장하지 않고 논리 identity를 보존한다. 접근 시 현재 기준정보를 사용해 물리 위치를 다시 해석한다.

- 서버/경로가 바뀌어도 같은 논리 파일이 새 위치에 있으면 정상 접근
- 로그 정의가 삭제돼 재해석할 수 없으면 `LogDefinitionNotFound`
- Configuration 정의가 삭제돼 재해석할 수 없으면 `ConfigurationDefinitionNotFound`
- 기준정보는 정상이나 실제 대상 파일이 없으면 `FileNotFound`

Current Configuration File의 `fileId`는 특정 바이트 버전을 고정하지 않는다. 같은 `equipmentId + configurationType + fileName` 논리 파일의 다운로드 시점 현재 내용을 가리킨다.

Configuration Snapshot `fileId`를 재해석할 때는 해당 Snapshot Set의 완료 marker 존재 여부도 다시 확인한다. marker가 사라졌다면 실제 Snapshot File이 남아 있어도 완료 상태로 제공하지 않고 `FileNotFound`로 처리한다.

기준정보 변경과 실제 파일 삭제를 같은 원인으로 취급하지 않는다.

## MVP 전제

- 분산 서버의 기본 FTP root 구조 동일
- 동일 credential 사용
- 로그 및 Configuration 종류별 실제 탐색/파일명 규칙은 서로 다를 수 있음
- DB/SP는 확장 가능하며 탐색/파싱 규칙을 기준정보로 관리
- FileGateway는 Configuration History를 생성/보관하지 않고 별도 시스템이 저장한 파일을 읽기 전용으로 제공
- Current/Hourly/Daily 파일의 생산 방식과 쓰기 중 내용 일관성은 기준정보로 해결하지 않으며 생산 시스템 책임으로 둠

## 호출자 확장 포인트

MVP API Key는 전체 설비/제공 파일에 접근 가능하다. 향후 권한 분리가 필요해지면 CallerId와 논리 리소스 식별자를 기준으로 정책 필터를 추가할 수 있다.

raw API Key를 Stored Procedure에 전달하지 않는다.

## 캐시

- 프로세스 memory cache를 사용한다.
- TTL은 설정 가능하며 초기 권장값은 10~30분 범위다.
- TTL은 캐시 데이터의 강제 폐기 시점이 아니라 **기준정보 갱신을 다시 시도해야 하는 시점**으로 사용한다.
- TTL 경과 후 실제 요청이 들어오면 lazy refresh로 Stored Procedure 갱신을 시도한다.
- MVP에서는 별도 background refresh worker를 두지 않는다.

기준정보 갱신은 **새 기준정보 전체를 검증한 뒤 한 번에 atomic 교체**한다. 일부 정의만 새 값으로 적용하는 혼합 상태는 만들지 않는다.

lazy refresh는 프로세스당 하나만 실행하는 **single-flight** 방식으로 동기화한다.

- last-known-good cache가 있으면 refresh가 진행 중인 동안 다른 요청은 기존 cache로 계속 처리한다.
- 최초 로딩이라 usable cache가 없으면 동시 요청들은 동일한 최초 refresh 결과를 공유한다.
- TTL 만료 시 요청마다 별도 Stored Procedure refresh를 발생시키지 않는다.

갱신 시도 결과:

- 새 기준정보 전체 검증 성공 → cache 전체를 새 기준정보로 atomic 교체
- 조회 실패 또는 검증 실패 + 이전 정상 cache 존재 → 새 데이터를 적용하지 않고 마지막 정상 cache 전체를 stale 상태로 계속 사용
- 최초 로딩에서 조회/검증 실패하여 정상 cache가 없음 → `ReferenceDataUnavailable`

하나의 잘못된 정의가 있으면 해당 refresh 전체를 거부한다. MVP에서는 부분 갱신 가용성보다 기준정보 집합의 일관성을 우선한다.

stale cache 사용 여부, 마지막 정상 갱신 시각, refresh/validation 실패 원인은 운영 로그/메트릭에서 관측 가능해야 한다.

로컬 영속 fallback/분산 cache는 MVP에서 제외한다.

## 필수 검증

SP 결과 전체에 대해 cache 교체 전에 **정의의 구조·문법·invariant만** 검증한다. 이 단계에서 FTP 서버에 접속해 실제 디렉터리, 파일, marker 존재 여부를 확인하지 않는다. 원격 저장소의 실재 상태는 실제 조회/metadata/download 요청 시 확인한다.

검증 항목:

- 설비/서버 매핑 존재 여부
- 로그/Configuration 정의 존재 여부
- `equipmentId + logType` 중복 정의 여부
- 중복/충돌 매핑
- root/path template의 구조와 정규화 가능 여부
- 정규화 후 `rootPath` 밖으로 탈출 가능한 정의 여부
- `filePattern`이 지원되는 glob 문법인지
- 무제한 recursive scan을 요구하는 정의가 아닌지
- MetadataRule 입력 정규화와 지원 mode가 유효한지
- Current rule의 구조/패턴이 유효한지
- History rule의 날짜별 경로, 논리 시각, marker 파일명/위치 정의가 유효한지
- 지원하지 않는 generation/metadata mode
- 유효하지 않은 regex/template/mapping

실제 원격 탐색에서는 다음을 별도로 판정한다.

- 계산된 디렉터리가 존재하지 않음 → 해당 슬롯의 정상 결과 0개
- `cardinality=Single`인데 하나의 논리 생성 슬롯에서 여러 파일 발견 → `FileDefinitionConflict`
- case-insensitive 기준 동일 파일명이 둘 이상 발견 → `FileDefinitionConflict`
- 후보 파일의 필수 metadata 해석 실패 → `FileDefinitionConflict`
- 파일 서버 연결/인증/프로토콜 장애 → 파일 서버 오류

기준정보 오류와 실제 파일 서버 장애는 별도 원인으로 유지한다.
