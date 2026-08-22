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

### 로그 정의

- logType
- generationType: Hourly / Daily / Continuous
- pathTemplate
- filePattern
- cardinality
- metadata mode/pattern/mapping

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 로그 정의를 식별한다. 동일 조합의 중복 정의는 기준정보 오류다.

MVP에서 하나의 로그 정의는 하나의 discovery rule만 가진다. 현재 동일 `logType` 조회에서 서로 다른 디렉터리/파일명 규칙을 동시에 검색해야 하는 사례가 없으므로 다중 discovery rule 모델은 두지 않는다. 같은 생성 슬롯의 여러 파일은 `cardinality=Multiple`로 표현한다.

`pathTemplate`은 탐색할 논리 디렉터리 경로를 계산하고, `filePattern`은 해당 디렉터리에서 후보 파일을 선택한다. `timestamp`/`subtype`/`attributes` 추출은 metadata rule이 담당한다.

MetadataRule은 물리 FTP root를 제외한 논리 relative path와 fileName 전체를 대상으로 해석할 수 있다. 후보 파일이 `filePattern`에 일치했지만 필수 metadata를 해석하지 못하면 누락시키지 않고 `FileDefinitionConflict`로 취급한다.

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
- `historyRule`: 날짜별 History 디렉터리/파일 패턴, Snapshot Set의 `snapshotTimestamp`, 완료 조건/marker를 해석하는 규칙

하나의 `equipmentId + configurationType` 아래 PM1/PM2/PM3/PM4처럼 여러 Current Configuration File이 존재할 수 있다. 이 파일들을 별도 `configurationType`, `subtype`, `attributes`로 세분화하지 않는다.

Current Configuration File의 logical identity는 `equipmentId + configurationType + fileName`이다. `fileName`이 바뀌면 다른 논리 파일로 취급한다.

History는 별도 시스템이 자정에 날짜 폴더를 만들고 Current 파일 집합을 그대로 복사한 결과다. 같은 날짜 폴더의 Snapshot File들은 동일한 `snapshotTimestamp`를 공유하며 현재 운영 계획에서는 Site local `00:00`으로 해석한다. FTP modified time은 snapshot 시각으로 사용하지 않는다.

History 생산자는 Snapshot Set 복사 완료를 판정할 수 있는 완료 조건/marker를 제공한다. `historyRule`은 이 완료 조건을 해석할 수 있어야 하며 FileGateway는 완료가 확인되지 않은 날짜 폴더를 History 결과에 포함하지 않는다. 완료 marker 자체는 Configuration File 후보로 반환하지 않는다.

Configuration Snapshot File의 logical identity는 `equipmentId + configurationType + snapshotTimestamp + fileName`이다.

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

MVP Configuration 정의에는 `subtype`/동적 `attributes` 규칙을 추가하지 않는다.

FTP 비밀번호 등 credential은 SP에서 반환하지 않는다.

## fileId 재해석과 기준정보 변경

`fileId`는 물리 host/path를 저장하지 않고 논리 identity를 보존한다. 접근 시 현재 기준정보를 사용해 물리 위치를 다시 해석한다.

- 서버/경로가 바뀌어도 같은 논리 파일이 새 위치에 있으면 정상 접근
- 로그 정의가 삭제돼 재해석할 수 없으면 `LogDefinitionNotFound`
- Configuration 정의가 삭제돼 재해석할 수 없으면 `ConfigurationDefinitionNotFound`
- 기준정보는 정상이나 실제 대상 파일이 없으면 `FileNotFound`

Current Configuration File의 `fileId`는 특정 바이트 버전을 고정하지 않는다. 같은 `equipmentId + configurationType + fileName` 파일의 다운로드 시점 현재 내용을 가리킨다.

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

DB/SP 갱신 시도 결과:

- 갱신 성공 → 새 기준정보로 캐시 교체
- 갱신 실패 + 이전 정상 캐시 존재 → 마지막 정상 캐시를 stale 상태로 계속 사용
- 프로세스 시작 후 정상 기준정보를 한 번도 얻지 못했고 캐시도 없음 → `ReferenceDataUnavailable`

stale 캐시 사용 여부와 마지막 정상 갱신 시각은 운영 로그/메트릭에서 관측 가능해야 한다.

로컬 영속 fallback/분산 cache는 MVP에서 제외한다.

## 필수 검증

SP 결과 수신 시:

- 설비/서버 매핑 존재 여부
- 로그/Configuration 정의 존재 여부
- `equipmentId + logType` 중복 정의 여부
- 중복/충돌 매핑
- 잘못된 root/path template
- Current rule이 유효한 현재 Configuration File 집합을 해석할 수 있는지
- History rule이 유효한 날짜별 Snapshot File 집합, 논리 시각, 완료 조건을 해석할 수 있는지
- 지원하지 않는 generation/metadata mode
- 유효하지 않은 regex/template/mapping

실제 로그 탐색 시 `cardinality=Single`인데 하나의 논리 생성 슬롯에서 여러 파일이 발견되거나 후보 파일의 필수 metadata 해석에 실패하면 `FileDefinitionConflict`로 분류한다.

기준정보 오류와 실제 파일 서버 장애는 별도 원인으로 유지한다.
