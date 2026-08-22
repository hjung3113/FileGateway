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

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 `EquipmentLogDefinition`을 식별한다.

MVP에서 하나의 로그 정의는 **하나의 `discoveryRule`만 가진다**. 현재 동일 `logType` 조회에서 서로 다른 디렉터리 구조나 파일명 규칙을 동시에 검색해야 하는 사례가 없으므로 `discoveryRules[]` 같은 다중 rule 구조는 만들지 않는다. 같은 생성 슬롯에서 여러 파일이 생기는 경우는 `cardinality=Multiple`로 표현한다.

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

- `pathTemplate`: 조회조건/논리 생성 슬롯으로 탐색할 논리 디렉터리 경로를 계산
- `filePattern`: 계산된 디렉터리 안에서 후보 **파일명**을 선택하는 glob matcher
- 파일의 `timestamp`/`subtype`/`attributes` 추출은 `MetadataRule` 책임

`filePattern`은 MVP에서 정규식이 아니라 glob 문법만 사용한다. 예: `*.zip`, `Event_*.log`, `PM?.cfg`. 복잡한 metadata 추출은 `MetadataRule.Regex`가 담당하므로 discovery matcher와 parsing regex의 역할을 섞지 않는다.

Glob 의미는 FTP 서버의 wildcard 구현에 의존하지 않는다. FileGateway가 디렉터리 목록을 받은 뒤 동일한 matcher 의미로 후보 파일명을 판정한다. MVP Windows/IIS FTP 환경의 파일명 의미에 맞춰 glob의 파일명 비교는 **case-insensitive**로 수행하고, 실제 파일명의 원래 casing은 응답에 그대로 보존한다.

MVP에서는 root부터의 무제한 recursive scan을 허용하지 않는다. `pathTemplate`이 조회 범위의 Hourly/Daily 슬롯 또는 Continuous 현재 위치로 필요한 디렉터리를 직접 계산하고 해당 디렉터리만 목록 조회한다. 여러 슬롯이 같은 디렉터리를 계산하면 중복 목록 조회하지 않는다.

계산된 논리 디렉터리가 실제 원격 저장소에 존재하지 않으면 해당 슬롯의 정상 결과 0개로 취급한다. 파일 서버 연결/인증/프로토콜 장애와 구분한다.

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

MetadataRule의 입력은 물리 FTP root를 제외한 **정규화된 논리 relative path + fileName**이다. 경로 구분자는 플랫폼/FTP 표현과 무관하게 `/`로 통일한다.

예:

```text
2026/08/22/18/Event_A.zip
```

일반적이고 결정적인 레이아웃은 `Template`을 우선하고, Template으로 표현하기 어려운 복잡한 경우만 `Regex` named group을 사용한다. metadata가 디렉터리명에 포함된 경우에도 파일명에만 한정하지 않는다.

`filePattern`에 후보로 일치한 파일이 필수 metadata를 해석하지 못하면 조용히 제외하지 않고 `FileDefinitionConflict`로 처리한다. 정의가 예상한 파일을 해석하지 못한 상태를 정상 결과로 숨기지 않는다.

`timestamp`는 파일명/경로 규칙에서 추출한 **로그의 논리 시각**이다. FTP modified time이나 파일시스템 수정 시각과 동일한 개념으로 사용하지 않는다.

Timezone 정보가 없는 논리 시각은 현재 Site의 운영 시간대 `Asia/Seoul`로 해석한다. API 경계에서는 UTC offset을 포함한 ISO-8601로 표현한다.

Daily 로그의 `timestamp`는 해당 날짜의 Site local `00:00`으로 표현한다.

Continuous 로그에 파일명/경로로부터 명확한 논리 시각을 추출할 수 없다면 `timestamp`는 `null`이다. 현재 시각이나 FTP modified time을 대신 넣지 않는다.

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

`subtype`은 하나의 `logType` 내부에서 API 사용자가 자주 조회하는 대표 하위 분류 하나다. 나머지 가변 메타데이터는 `attributes`에 두며 같은 의미의 값을 양쪽에 중복 저장하지 않는다.

`subtype`과 `attributes` 필터는 정확한 문자열 일치(case-sensitive)를 사용한다. 대소문자 비구분이 필요한 값은 파싱/기준정보 단계에서 canonical value로 정규화한다.

`fileId`는 특정 논리 파일 하나를 가리키는 임시 opaque 참조다. 일반 조회조건 자체를 나타내지 않는다.

물리 host/path/credential은 포함하지 않는다.

## 파일명 비교 규칙

MVP에서 `fileName` 비교는 case-insensitive다. 이 규칙은 다음에 동일하게 적용한다.

- `filePattern` glob matching
- Log logical identity의 `fileName` 구성요소 비교
- 동일 timestamp 내 `fileName ASC` 정렬
- continuation cursor의 `fileName` 비교

`Event.LOG`와 `event.log`는 같은 논리 파일명으로 취급한다. 응답의 `fileName`은 실제 원격 파일이 가진 casing을 그대로 반환한다. 향후 case-sensitive 저장소를 지원할 때 이 계약은 재검토한다.

동일 탐색 범위에 `Event.LOG`와 `event.log`처럼 case-insensitive 기준 동일한 서로 다른 원격 파일이 함께 발견되면 임의 dedupe하거나 하나를 선택하지 않고 `FileDefinitionConflict`로 처리한다.

## 토큰 의미

Logs는 Log `fileId`와 Log pagination의 **도메인 의미**를 소유한다. 서명/검증/opaque encoding/TTL 같은 token codec은 공통 계층을 사용한다.

### Log fileId

- `resourceKind=Log`
- logical identity: `equipmentId + logType + timestamp + fileName`
- `fileName` 구성요소는 case-insensitive 비교
- 현재 기준정보로 물리 위치를 재해석

### Log continuationToken

서버가 FTP 목록 전체를 저장하는 session 방식은 사용하지 않는다. 토큰은 stateless cursor로 다음 의미를 보존한다.

- 원래 결과 집합을 결정한 조회조건
- 시간 기반 로그 마지막 반환 위치: `timestamp + fileName`
- Continuous 로그 마지막 반환 위치: `fileName`
- token TTL

cursor의 `fileName` 비교도 case-insensitive다. `limit`은 페이지 크기이므로 원래 조회조건에 포함하지 않는다. 페이지 사이 원격 파일 집합 변경에 대한 완전한 snapshot은 보장하지 않는다.

## 로그 생성 정책 (`generationType`)

### Hourly

시간 또는 시간 범위로 조회한다. 같은 시간대 여러 파일을 허용한다.

### Daily

일자/일자 범위로 조회한다. 논리 `timestamp`는 해당 날짜의 Site local `00:00`이다.

### Continuous

- 시간 범위 개념을 사용하지 않고 현재 존재 파일을 조회한다.
- `from` 또는 `to`가 요청에 포함되면 `InvalidRequest`로 처리한다.
- Hourly/Daily의 최근 24시간 기본값을 적용하지 않는다.
- 명확한 논리 시각이 없으면 `timestamp=null`이다.
- 목록 정렬은 case-insensitive `fileName ASC`를 사용하고 pagination cursor는 `fileName`이다.
- 다운로드 시작 직전 파일 크기를 확정하고 그 크기까지만 전송한다.
- 다운로드 중 파일이 커져도 시작 시점 이후 추가된 내용은 전송하지 않는다.
- 다운로드 중 파일이 줄어 시작 크기까지 읽지 못하면 정상 완료가 아니라 streaming I/O 실패다.
- truncate/rotation 시 새 파일로 이어 붙이거나 자동 재시도하지 않는다.

`generationType`은 조회/탐색 의미를 나타내며, Hourly/Daily 파일이 생산 중 언제 완성되는지 또는 파일 교체가 원자적인지를 FileGateway가 판정하는 계약은 아니다. FileGateway는 저장소에 보이는 파일을 읽어 제공하며 생산 방식과 쓰기 중 내용 일관성은 생산 시스템 책임으로 둔다.

## 필터 및 시간 범위

- equipmentId
- logType
- from/to
- subtype
- 동적 attributes

`equipmentId`와 `logType`은 로그 조회 시 모두 필수다.

Hourly/Daily에서 `from`/`to`는 `timestamp` 기준 반개구간 `[from, to)`로 해석한다. `from`은 포함하고 `to`는 제외한다.

Hourly/Daily 시간 범위 입력은 다음과 같이 해석한다.

- `from`, `to` 모두 없음: 최근 24시간
- `from`만 있음: `[from, from + 2일)`
- `to`만 있음: 지원하지 않으며 `InvalidRequest`
- `from`, `to` 모두 있음: 지정한 `[from, to)`

시간 기반 로그 조회에는 설정 가능한 `Logs.MaxQueryRange`를 적용한다. 요청 범위가 이를 초과하면 `InvalidRequest`로 처리한다. `from` 단독 요청이 항상 2일 범위를 의미하므로 `Logs.MaxQueryRange` 설정은 **최소 2일 이상**이어야 하며 애플리케이션 시작 시 설정값을 검증한다.

Continuous는 `from`/`to`를 허용하지 않는다.

## 정렬

시간 기반 로그의 목록 기본 정렬은 다음 순서다.

1. `timestamp DESC`
2. 동일 `timestamp`에서는 case-insensitive `fileName ASC`

Continuous 로그 목록은 case-insensitive `fileName ASC`로 정렬한다.

`equipmentId + logType`은 하나의 `generationType`만 가지므로 시간 기반 로그와 `timestamp=null`인 Continuous 로그를 같은 목록 정의 안에서 혼합하지 않는다.
