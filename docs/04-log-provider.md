# Log Provider / Resolver

## 역할

`FileGateway.Logs`는 **로그**의 업무 의미와 탐색 규칙을 담당하고 실제 파일 I/O는 `IFileAccess`에 위임한다.

`Configuration File`은 로그가 아니므로 이 Provider의 로그 종류로 취급하지 않는다. Configuration 제공 경계는 별도 설계 결정으로 확정한다.

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

### MetadataRule

- mode: `Template | Regex`
- pattern
- mappings: 추출 값 → `timestamp`, `subtype`, `attribute.<key>`

`timestamp`는 파일명/경로 규칙에서 추출한 **로그의 논리 시각**이다. FTP modified time이나 파일시스템 수정 시각과 동일한 개념으로 사용하지 않는다.

Timezone 정보가 없는 논리 시각은 현재 Site의 운영 시간대 `Asia/Seoul`로 해석한다. API 경계에서는 UTC offset을 포함한 ISO-8601로 표현한다.

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

`fileId`는 특정 논리 파일 하나를 가리키는 임시 opaque 참조다. 일반 조회조건 자체를 나타내지 않는다.

물리 host/path/credential은 포함하지 않는다.

## 로그 생성 정책 (`generationType`)

### Hourly

시간 또는 시간 범위로 조회한다. 같은 시간대 여러 파일을 허용한다.

### Daily

일자/일자 범위로 조회한다.

### Continuous

시간 필터와 무관하게 현재 존재 파일을 포함한다. 다운로드 시작 시점 크기까지만 전송한다.

## 필터

- equipmentId
- logType
- from/to
- subtype
- 동적 attributes

`from`/`to`는 `timestamp` 기준 반개구간 `[from, to)`로 해석한다. `from`은 포함하고 `to`는 제외한다.

시간 조건이 없으면 최근 24시간을 사용한다.
