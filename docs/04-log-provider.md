# Log Provider / Resolver

## 역할

`FileGateway.Logs`는 로그의 업무 의미와 탐색 규칙을 담당하고 실제 파일 I/O는 `IFileAccess`에 위임한다.

## 핵심 처리 흐름

1. 설비명/로그종류/조회조건 수신
2. MSSQL 기준정보 조회(캐시 우선)
3. `LogResolver`가 탐색 경로/후보 패턴 계산
4. `IFileAccess`로 원격 목록/메타데이터 조회
5. Template/Regex 규칙으로 timestamp/subtype/attributes 추출
6. 날짜/시간/subtype/attribute 필터 적용
7. `FileDescriptor[]` 구성
8. `fileId` 발급 후 API 반환

Resolver의 기본 반환은 항상 **파일 집합**이다. 시간당 한 파일인 EventLog도 단일 파일 특수 API로 모델링하지 않는다.

## 도메인 모델

### EquipmentLogDefinition

- equipment
- logType
- serverId
- generationType: `Hourly | Daily | Continuous`
- discoveryRule
- metadataRule

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

일반 패턴은 Template을 우선하고 복잡한 예외만 Regex named group을 사용한다.

### FileDescriptor

- fileId
- equipment
- logType
- subtype(optional)
- timestamp(optional)
- fileName
- size
- isContinuous
- attributes: `Dictionary<string,string>`

물리 host/path/credential은 포함하지 않는다.

## 로그 정책

### Hourly

시간 또는 시간 범위로 조회한다. 같은 시간대 여러 파일을 허용한다.

### Daily

일자/일자 범위로 조회한다.

### Continuous

시간 필터와 무관하게 현재 존재 파일을 포함한다. 다운로드 시작 시점 크기까지만 전송한다.

### Configuration

파일 종류와 속성이 다양하므로 subtype + attributes를 사용하고, 파일명/경로 파싱과 DB 정의를 조합한다.

## 필터

- equipment
- logType
- from/to
- subtype
- 동적 attributes

시간 조건이 없으면 최근 24시간을 사용한다.
