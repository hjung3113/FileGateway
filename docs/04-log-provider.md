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

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 `EquipmentLogDefinition`을 식별한다. 같은 logType에 여러 물리 파일 패턴이 필요하면 별도 로그 정의를 중복 생성하지 않고 해당 정의 내부의 탐색 규칙으로 표현한다.

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

`cardinality`는 기준정보 검증/의도 표현용이며 Resolver 반환형을 바꾸지 않는다.

`cardinality=Single`인데 실제 탐색 결과가 2개 이상이면 정상적인 다중 결과가 아니라 기준정보/파일 상태가 정의와 충돌한 시스템 invariant 위반이다. 사용자 조회조건이 정상적인 여러 파일에 일치한 경우의 `MultipleFilesMatched`와 구분한다.

### MetadataRule

- mode: `Template | Regex`
- pattern
- mappings: 추출 값 → `timestamp`, `subtype`, `attribute.<key>`

`timestamp`는 파일명/경로 규칙에서 추출한 **로그의 논리 시각**이다. FTP modified time이나 파일시스템 수정 시각과 동일한 개념으로 사용하지 않는다.

Timezone 정보가 없는 논리 시각은 현재 Site의 운영 시간대 `Asia/Seoul`로 해석한다. API 경계에서는 UTC offset을 포함한 ISO-8601로 표현한다.

Daily 로그의 `timestamp`는 해당 날짜의 Site local `00:00`으로 표현한다.

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

시간 필터와 무관하게 현재 존재 파일을 포함한다. 다운로드 시작 시점 크기까지만 전송한다.

## 필터

- equipmentId
- logType
- from/to
- subtype
- 동적 attributes

`equipmentId`와 `logType`은 로그 조회 시 모두 필수다.

`from`/`to`는 `timestamp` 기준 반개구간 `[from, to)`로 해석한다. `from`은 포함하고 `to`는 제외한다.

시간 조건이 없으면 최근 24시간을 사용한다.

## 정렬

시간 기반 로그의 목록 기본 정렬은 다음 순서다.

1. `timestamp DESC`
2. 동일 `timestamp`에서는 `fileName ASC`

최신 논리 시각의 파일부터 반환하고 `fileName`을 동일 시각 내 안정적인 tie-breaker로 사용한다.
