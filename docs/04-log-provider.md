# Log Provider / Resolver

## 역할

`FileGateway.Logs`는 **로그**의 업무 의미와 탐색 규칙을 담당하고 실제 파일 I/O는 `IFileAccess`에 위임한다.

`Configuration File`은 로그가 아니므로 이 Provider의 로그 종류로 취급하지 않는다. Configuration은 `FileGateway.Configurations`에서 별도로 다룬다.

## 핵심 처리 흐름

1. `equipmentId`/`logType`/조회조건 수신
2. MSSQL 기준정보 조회(캐시 우선)
3. `LogResolver`가 탐색 경로/후보 패턴 계산
4. `IFileAccess`로 원격 목록/메타데이터 조회
5. Template/Regex 규칙으로 `timestamp`/`subtype`/`attributes` 추출
6. 날짜/시간/subtype/attribute 필터 적용
7. `FileDescriptor[]` 구성
8. `fileId` 발급 후 API 반환

Resolver의 기본 반환은 항상 **파일 집합**이다. 시간당 한 파일인 EventLog도 단일 파일 특수 API로 모델링하지 않는다.

## 도메인 모델

### EquipmentLogDefinition

- equipmentId
- logType
- serverId
- generationType: `Hourly | Daily | Continuous`
- discoveryRule
- metadataRule

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 `EquipmentLogDefinition`을 식별한다. 같은 logType에 여러 물리 파일 패턴이 실제로 필요한지는 별도 설계 결정으로 남기고, MVP에서는 요구가 확인되기 전까지 `discoveryRule` 하나를 전제로 한다.

`logType`은 업무적으로 어떤 종류의 로그인지를 나타내며, `generationType`은 파일 생성 주기/생명주기를 나타낸다. 두 개념을 혼용하지 않는다.

### ServerDefinition

- serverId
- host
- rootPath

MVP에서는 공통 credential을 별도 Secret으로 사용한다.

### DiscoveryRule

- pathTemplate
- filePattern
- cardinality: `Single | Multiple`

역할은 다음처럼 분리한다.

- `pathTemplate`: 탐색할 논리 디렉터리 경로 계산
- `filePattern`: 해당 디렉터리 안에서 후보 파일 선택
- 파일의 `timestamp`/`subtype`/`attributes` 추출은 `MetadataRule` 책임

`cardinality`는 전체 조회 결과 개수가 아니라 **논리 생성 슬롯당 파일 개수**를 나타내는 invariant다.

- Hourly: 각 시간 슬롯마다 적용
- Daily: 각 날짜 슬롯마다 적용
- Continuous: 현재 슬롯에 적용
- `Single`: 슬롯당 최대 1개
- `Multiple`: 같은 슬롯에 여러 파일 허용

`cardinality=Single`인데 하나의 슬롯에서 실제 탐색 결과가 2개 이상이면 정상적인 다중 결과가 아니라 기준정보/파일 상태가 정의와 충돌한 시스템 invariant 위반이다. 사용자 조회조건이 정상적인 여러 파일에 일치한 경우의 `MultipleFilesMatched`와 구분한다.

### MetadataRule

- mode: `Template | Regex`
- pattern
- mappings: 추출 값 → `timestamp`, `subtype`, `attribute.<key>`

MetadataRule은 물리 FTP root를 제외한 **논리 relative path + fileName 전체**를 대상으로 해석할 수 있다. metadata가 디렉터리명에 포함된 경우에도 파일명에만 한정하지 않는다.

`filePattern`에 후보로 일치한 파일이 필수 metadata를 해석하지 못하면 조용히 제외하지 않고 `FileDefinitionConflict`로 처리한다. 정의가 예상한 파일을 해석하지 못한 상태를 정상 결과로 숨기지 않는다.

`timestamp`는 파일명/경로 규칙에서 추출한 **로그의 논리 시각**이다. FTP modified time이나 파일시스템 수정 시각과 동일한 개념으로 사용하지 않는다.

Timezone 정보가 없는 논리 시각은 현재 Site의 운영 시간대 `Asia/Seoul`로 해석한다. API 경계에서는 UTC offset을 포함한 ISO-8601로 표현한다.

Daily 로그의 `timestamp`는 해당 날짜의 Site local `00:00`으로 표현한다.

Continuous 로그에 파일명/경로로부터 명확한 논리 시각을 추출할 수 없다면 `timestamp`는 `null`이다. 현재 시각이나 FTP modified time을 대신 넣지 않는다.

일반 패턴은 Template을 우선하고 복잡한 예외만 Regex named group을 사용한다.

### FileDescriptor

- fileId
- equipmentId
- logType
- subtype(optional)
- timestamp(optional)
- fileName
- size
- isContinuous
- attributes: `Dictionary<string,string>`

`subtype`은 하나의 `logType` 내부에서 자주 조회하는 대표 하위 분류 하나다. 나머지 가변 메타데이터는 `attributes`에 두며 같은 의미의 값을 양쪽에 중복 저장하지 않는다.

`subtype`과 `attributes` 필터는 정확한 문자열 일치(case-sensitive)를 사용한다. 대소문자 비구분이 필요한 값은 파싱/기준정보 단계에서 canonical value로 정규화한다.

`fileId`는 특정 논리 파일 하나를 가리키는 임시 opaque 참조다. 일반 조회조건 자체를 나타내지 않는다.

물리 host/path/credential은 포함하지 않는다.

## 로그 생성 정책 (`generationType`)

### Hourly

시간 또는 시간 범위로 조회한다. 같은 시간대 여러 파일을 허용한다.

### Daily

일자/일자 범위로 조회한다. 논리 `timestamp`는 해당 날짜의 Site local `00:00`이다.

### Continuous

- 시간 필터와 무관하게 현재 존재 파일을 포함한다.
- 명확한 논리 시각이 없으면 `timestamp=null`이다.
- 다운로드 시작 직전 파일 크기를 확정하고 그 크기까지만 전송한다.
- 다운로드 중 파일이 커져도 시작 시점 이후 추가된 내용은 전송하지 않는다.
- 다운로드 중 파일이 줄어 시작 크기까지 읽지 못하면 정상 완료가 아니라 streaming I/O 실패다.
- truncate/rotation 시 새 파일로 이어 붙이거나 자동 재시도하지 않는다.

## 필터

- equipmentId
- logType
- from/to
- subtype
- 동적 attributes

`equipmentId`와 `logType`은 로그 조회 시 모두 필수다.

`from`/`to`는 `timestamp` 기준 반개구간 `[from, to)`로 해석한다. `from`은 포함하고 `to`는 제외한다.

시간 조건이 없으면 최근 24시간을 사용한다. Continuous 로그는 시간 범위와 별도로 현재 파일을 포함한다.

## 정렬

시간 기반 로그의 목록 기본 정렬은 다음 순서다.

1. `timestamp DESC`
2. 동일 `timestamp`에서는 `fileName ASC`

최신 논리 시각의 파일부터 반환하고 `fileName`을 동일 시각 내 안정적인 tie-breaker로 사용한다. `equipmentId + logType`은 하나의 `generationType`만 가지므로 시간 기반 로그와 `timestamp=null`인 Continuous 로그를 같은 목록 정의 안에서 혼합하지 않는다.
