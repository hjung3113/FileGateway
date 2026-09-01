# Configuration Provider

## 역할

`FileGateway.Configurations`는 설비가 실제 사용하는 Configuration File의 조회 의미와 탐색 규칙을 담당하고 실제 파일 I/O는 `IFileAccess`에 위임한다.

Configuration은 로그가 아니므로 `FileGateway.Logs`의 `logType`/`generationType` 모델에 포함하지 않는다.

## 책임 경계

FileGateway가 담당한다.

- Current Configuration 조회/다운로드
- Configuration Snapshot History 조회/다운로드
- `equipmentId + configurationType` 기반 논리 해석
- 기준정보를 이용한 물리 서버/경로 탐색
- Configuration logical identity와 pagination 의미 관리
- 논리 `fileId` 발급/해석
- History 생산자가 제공한 완료 marker 존재 여부를 확인해 완료된 물리 batch의 Snapshot File만 조회 대상으로 삼기

FileGateway가 담당하지 않는다.

- 설비에서 설정값 수집
- Current Configuration 변경
- Configuration Snapshot 생성
- snapshot 복사/보관/삭제 정책
- Current 파일의 생산 중 원자성/내용 일관성 보장

히스토리 파일은 별도 시스템이 파일 서버에 저장한 결과를 FileGateway가 읽기 전용으로 제공한다.

## 도메인 관계

특정 `equipmentId + configurationType`은 하나의 Current 파일이 아니라 **현재 Configuration File 집합**을 식별한다.

예를 들어 `configurationType=PM` 조회 결과로 PM1, PM2, PM3, PM4와 같이 여러 현재 파일이 존재할 수 있다. 이 개별 파일들을 `subtype`이나 별도 `configurationType`으로 세분화하지 않는다.

```text
Equipment + Configuration Type
├─ Current Configuration Set
│  ├─ Current File A
│  ├─ Current File B
│  └─ Current File ...
└─ Configuration Snapshot History
   ├─ Snapshot Set @ T1
   │  ├─ Snapshot File A
   │  ├─ Snapshot File B
   │  └─ ...
   ├─ Snapshot Set @ T2
   └─ ...
```

Current와 History는 서로 다른 `configurationType`이 아니라 같은 Configuration의 현재 파일 집합과 과거 snapshot 관계다.

## 기준정보 모델

개념적으로 하나의 Configuration 정의는 다음 정보를 가진다.

```text
EquipmentConfigurationDefinition
- equipmentId
- configurationType
- serverId
- currentRule
- historyRule
```

- `currentRule`: 현재 Configuration File 집합의 위치/후보 패턴을 해석하는 규칙
- `historyRule`: 날짜별 히스토리 디렉터리/파일 패턴, Snapshot File의 `snapshotTimestamp` 파생 규칙, 물리 batch 완료 marker 이름/위치를 해석하는 규칙

`currentRule.pathTemplate`과 `historyRule.pathTemplate`은 `/`로 구분하는 상대 경로다. 빈 세그먼트는 제거한 뒤 파싱한다. 각 세그먼트는 다음 두 종류다.

- Template 세그먼트: 리터럴과 `{yyyy}` `{MM}` `{dd}` `{HH}` token을 사용하며 논리 슬롯의 Site local(`Asia/Seoul`) 구성요소로 치환한다. token 없는 고정 경로도 허용한다.
- Regex 세그먼트: `regex:PATTERN` 접두사 뒤의 pattern으로 자식 디렉터리 이름을 매칭한다. pattern은 비어 있지 않고 `/`를 포함하지 않으며 `^...$` anchor가 필요하다.

선두의 연속된 Template 세그먼트는 슬롯으로 확장한 **확정 prefix**가 되고, 뒤따르는 Regex 세그먼트마다 해당 prefix의 자식 디렉터리를 열거해 모든 매칭 경로로 fan-out한다. 이후 Template 세그먼트는 각 branch에 결합하며, 여러 leaf의 파일 결과는 하나의 결과 집합으로 합친다. Regex 세그먼트가 없으면 기존처럼 확정된 단일 경로만 조회한다. 비-regex 세그먼트에는 `..`, rooted 경로, `:`를 금지한다. 빈 세그먼트 제거는 기존 동작을 보존한다. `MarkerPathTemplate`은 확정된 Template 경로만 허용하고 Regex 세그먼트를 허용하지 않는다.

기준정보의 `CurrentFileNameMatchMode`와 `HistoryFileNameMatchMode`는 `Literal | Glob | Regex`다. NULL/빈 값은 기존 의미인 `Glob`이며, `Literal`은 case-insensitive 전체 동등, `Glob`은 기존 glob, `Regex`는 파일명 전체 매칭이다. Regex pattern은 anchor와 컴파일 가능성을 검증한다.

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

## 파일명 비교 규칙

MVP Windows/IIS FTP 환경에서는 Configuration `fileName` 비교를 case-insensitive로 수행하고 실제 파일명의 원래 casing은 응답에 그대로 보존한다.

이 규칙은 다음에 동일하게 적용한다.

- Current/Snapshot 후보 파일명 matching
- Configuration path의 directory segment matching
- Configuration file `Regex` matching
- Configuration metadata matching
- Current logical identity의 `fileName`
- Snapshot logical identity의 `fileName`
- Current `fileName ASC` 정렬
- History 동일 timestamp 내 `fileName ASC` 정렬과 cursor 비교

대소문자만 다른 파일명은 같은 논리 파일명으로 취급한다. 정규식은 .NET에서 `OrdinalIgnoreCase` 옵션이 없으므로 `IgnoreCase | CultureInvariant`를 표준 근사로 사용한다. PM1, Port3, Config 같은 ASCII 이름에서는 `OrdinalIgnoreCase`와 동등하다. 향후 case-sensitive 저장소를 지원할 때 fileName 계약과 함께 재검토한다.

동일 탐색 결과에 `PM1.cfg`와 `pm1.cfg`처럼 case-insensitive 기준 동일한 서로 다른 원격 파일이 함께 발견되면 임의 dedupe하지 않고 `FileDefinitionConflict`로 처리한다.

## Current Configuration

- 시간 필터와 무관하게 현재 파일 집합을 조회한다.
- `CurrentFileNameMatchMode`는 `Literal | Glob | Regex`이며 NULL/빈 값은 `Glob`이다. Regex는 파일명 전체를 매칭한다.
- 동일 `equipmentId + configurationType` 아래 현재 파일이 여러 개 존재할 수 있다.
- 개별 파일을 `subtype`/`attributes`로 세분화하지 않는다.
- Current 조회 응답은 개별 Current Configuration File들의 배열이다.
- 결과가 없으면 `200 OK`와 빈 배열을 반환한다.
- Current 목록은 보통 작은 현재 파일 집합이므로 `limit`/`continuationToken`을 사용하지 않고 전체를 한 번에 반환한다.
- Current item의 핵심 필드는 `fileId`, `fileName`, `equipmentId`, `configurationType`, `size`다.
- Current 목록 정렬은 case-insensitive `fileName ASC`로 고정해 FTP 서버의 원시 목록 순서에 의존하지 않는다.
- Current Configuration File의 논리 identity는 `equipmentId + configurationType + fileName`이며 `fileName` 구성요소는 case-insensitive다.
- 파일명이 대소문자만 바뀐 것은 같은 논리 Current Configuration File로 취급한다. 그 외 이름 변경은 다른 논리 파일이다.
- 목록/정보 조회 후 각 파일 내용이 변경될 수 있다.
- Current File의 `fileId`는 특정 바이트 버전을 고정하지 않고 동일 논리 identity의 다운로드 시점 현재 내용을 가리킨다.
- FileGateway는 Current 파일이 생산 중 어떤 방식으로 쓰이거나 교체되는지 판정·보정하지 않는다.
- 과거 특정 버전이 필요하면 History의 개별 Snapshot File `fileId`를 사용한다.

Current path에 날짜 token이 있으면 `CurrentResolver`는 `ResolveAsync` 시작 시 주입된 `TimeProvider`에서 Site local 현재 시각을 정확히 한 번 캡처해 모든 token을 같은 slot으로 확장한다. 목록 조회와 fileId 재해석은 각각의 resolve에서 독립적으로 캡처한다. Current에는 metadata rule을 두지 않는다. Current identity와 API 응답에는 timestamp가 없고, file match mode가 확장자와 무관한 파일명 선택을 담당하므로 History처럼 timestamp를 추출할 필요가 없다.

Current용으로 계산된 디렉터리가 실제로 존재하지 않으면 정상적인 현재 파일 0개로 취급한다. 파일 서버 연결/인증/프로토콜 장애와 구분한다.

### Current 직접 다운로드

```http
GET /api/v1/configurations/current/download?equipmentId=...&configurationType=...
```

Current 조회와 같은 Resolver 규칙을 사용한다.

- 0개 일치: `FileNotFound`
- 1개 일치: 해당 Current Configuration File 다운로드
- 2개 이상 일치: 임의 선택하지 않고 `MultipleFilesMatched`(409)

여러 Current 파일이 있는 Configuration Type은 Current 목록에서 원하는 파일의 `fileId`를 받은 뒤 공통 `/api/v1/files/download?fileId=...`를 사용한다. 직접 다운로드 endpoint에 `fileName`/`subtype` 같은 추가 선택 축을 만들지 않는다.

## Configuration Snapshot History

- 별도 시스템이 날짜 폴더를 만들고 해당 시점의 Current Configuration Set을 그대로 복사한다.
- snapshot 생성 후에도 Current 원본 파일은 그대로 유지된다.
- **물리 batch**는 날짜 폴더와 그 복사 완료 marker가 이루는 단위다.
- metadata rule이 없는 경우 물리 batch의 Snapshot File들은 동일한 `snapshotTimestamp`를 공유하며 해당 날짜의 Site local `00:00`으로 해석한다. metadata rule이 있으면 fileName에서 추출한 timestamp가 각 Snapshot File의 `snapshotTimestamp`가 된다.
- 한 물리 batch에 여러 `Configuration Snapshot Set`이 있을 수 있다. Snapshot Set은 `snapshotTimestamp`만을 그룹핑 키로 하며, PM1, PM2, PM3, PM4처럼 여러 파일이 하나의 Set에 속할 수 있다.
- History 생산자는 Snapshot Set별이 아니라 물리 batch(날짜 폴더 복사) 완료 시 marker 파일을 생성한다.
- marker 파일명/위치는 `historyRule` 기준정보로 설정한다.
- FileGateway는 marker 파일의 **존재 여부만 확인**하고 내용은 읽거나 해석하지 않는다.
- marker가 존재하는 물리 batch의 Snapshot File만 조회 대상으로 삼고 복사 중인 batch는 노출하지 않는다. 한 물리 batch 안의 여러 Snapshot Set은 동일 marker로 함께 게이팅된다.
- 완료 marker 자체는 Configuration File 결과에 포함하지 않는다.
- 생성이 완료된 Snapshot File은 불변이다. 기존 파일을 수정하지 않고 다음 snapshot에서 새 파일로 반영한다.
- FTP modified time을 snapshot 시각으로 사용하지 않는다.
- timezone 없는 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.
- 시간 범위는 `[from, to)` 규칙을 사용한다.
- History 조회에서는 `from`과 `to`를 모두 필수로 요구한다. 전체 히스토리 또는 임의 기본 기간을 암묵적으로 조회하지 않는다.
- `[from, to)` 경계는 `snapshotTimestamp`에 정확히 적용한다. metadata rule이 있으면 추출된 timestamp 기준으로 필터링하고, 없으면 기존처럼 날짜 폴더의 Site local 자정 기준으로 적용한다. Rule이 없는 경우 `from`이 자정이 아니면 그날 자정에 생성된 Snapshot Set은 제외한다.
- History 조회에는 설정 가능한 `Configurations.HistoryMaxQueryRange`를 적용하고 초과 요청은 `InvalidRequest`로 처리한다.
- History API는 Snapshot Set을 별도 중첩 객체로 만들지 않고 개별 Snapshot File을 반환하며, 같은 `snapshotTimestamp`의 파일들로 Set을 구분할 수 있다.
- Snapshot File용 `fileId`는 `equipmentId + configurationType + snapshotTimestamp + fileName`의 논리 identity로 특정 파일 하나를 가리키며 `fileName`은 case-insensitive 비교한다.
- Metadata rule이 있는 정의에서 추출 timestamp의 Site local 날짜가 물리 날짜 슬롯과 다르면 파일을 누락하지 않고 `FileDefinitionConflict`로 거부한다.
- `subtype`/`attributes`는 MVP Configuration 모델에 두지 않는다.

History 조회에서 계산된 날짜 디렉터리가 실제로 존재하지 않으면 해당 날짜의 정상 결과 0개로 취급한다. marker가 없거나 Snapshot File이 없는 상태도 파일 서버 장애와 구분한다.

## 오류와 빈 결과

| 상황 | 결과 |
|---|---|
| 계산된 상위/leaf 디렉터리 부재(`Exists=false`) | 해당 branch 또는 날짜의 정상 결과 0건 |
| Regex 세그먼트에 매칭 자식 없음 | 정상 결과 0건 |
| 파일 매칭 0건 | `200 OK`, `items=[]` |
| 여러 leaf의 파일 결과 결합 | 모든 결과를 포함하며, Current 집합 내 또는 동일 `snapshotTimestamp` 내 case-insensitive 동일 fileName 충돌은 `FileDefinitionConflict` |
| 후보의 metadata 매칭/추출 실패 | `FileDefinitionConflict` |
| Regex runtime timeout | `FileDefinitionConflict` |
| 물리 날짜 슬롯과 metadata 추출 timestamp의 Site local 날짜 불일치 | `FileDefinitionConflict` |
| 연결/인증/프로토콜 장애 | `FileServerUnavailable`/`FileServerProtocolError` 등 파일 서버 오류 |
| 개별 무효 Configuration 정의의 load/refresh | 해당 정의만 새 snapshot에서 제외, 나머지 정상 정의는 atomic 교체 |
| DB/SP·result set·Equipment/Server 전역 검증 실패 | refresh 전체 거부(fail closed), LKG stale 유지 또는 최초면 `ReferenceDataUnavailable` |
| Snapshot fileId 재해석 대상 부재(정확한 ts + ci 이름 일치 0건) | `FileNotFound` |

정상적인 no-match/부재와 기준정보 품질 오류 및 파일 서버 장애를 같은 빈 결과로 뭉개지 않는다.

## 토큰 의미

Configurations는 Current/Snapshot `fileId`와 Configuration History pagination의 **도메인 의미**를 소유한다. 서명/검증/opaque encoding/TTL 같은 token codec은 공통 계층을 사용한다.

### Current fileId

- `resourceKind=ConfigurationCurrent`
- logical identity: `equipmentId + configurationType + fileName`
- `fileName` 비교는 case-insensitive
- 특정 바이트 버전이 아니라 같은 identity의 다운로드 시점 현재 내용을 가리킴

### Snapshot fileId

- `resourceKind=ConfigurationSnapshot`
- logical identity: `equipmentId + configurationType + snapshotTimestamp + fileName`
- `fileName` 비교는 case-insensitive
- 생성 완료된 불변 Snapshot File을 가리킴
- 재해석 시 해당 물리 batch의 완료 marker 존재 여부를 다시 확인
- 재해석 predicate는 `snapshotTimestamp` 정확 일치와 case-insensitive `fileName` 일치다. 이름만으로 선택하거나 다른 timestamp를 허용하지 않는다.
- 물리 슬롯 날짜와 metadata 추출 timestamp의 Site local 날짜가 다르면 `FileDefinitionConflict`
- marker가 사라졌으면 실제 Snapshot File이 남아 있어도 `FileNotFound`

### History continuationToken

서버가 History 결과 전체를 보관하는 session 방식은 사용하지 않는다. 토큰은 stateless cursor로 다음 의미를 보존한다.

- 원래 결과 집합을 결정한 `equipmentId + configurationType + from/to`
- 마지막 반환 위치: `snapshotTimestamp + fileName`
- token TTL

cursor의 `fileName` 비교는 case-insensitive다. `limit`은 페이지 크기이므로 결과 집합 조건에 포함하지 않는다. 페이지 사이 원격 History 파일 집합 변경에 대한 완전한 snapshot은 보장하지 않는다.

## 조회 원칙

- Current와 History는 API에서 명시적으로 구분한다.
- Current를 History 결과에 암묵적으로 포함하지 않는다.
- Current는 `equipmentId + configurationType`으로 현재 파일 집합을 결정하고 case-insensitive `fileName ASC`로 정렬한다.
- History는 `equipmentId + configurationType + [from, to)`로 **완료 marker가 존재하는** Snapshot File 집합을 조회한다.
- 계산된 원격 디렉터리가 없으면 해당 범위의 정상 결과 0개로 취급한다.
- History 결과 item은 `fileId`, `fileName`, `equipmentId`, `configurationType`, `snapshotTimestamp`, `size`를 가진다.
- History 목록은 pagination envelope의 `items`로 결과를 반환하며 결과가 없으면 `items=[]`, `continuationToken=null`이다.
- History 목록은 `limit + continuationToken` 페이지네이션을 사용한다.
- `limit`의 기본값/최댓값은 운영 설정으로 두며 최대값을 넘는 요청은 `InvalidRequest`다.
- `Configurations.HistoryMaxQueryRange`를 초과하는 조회는 `InvalidRequest`다.
- History 기본 정렬은 `snapshotTimestamp DESC`, 동일 시각에서는 case-insensitive `fileName ASC`다.
- 페이지네이션은 원격 파일 집합의 완전한 snapshot을 보장하지 않는다. 조회 중 파일이 추가/삭제되면 후속 페이지 결과가 달라질 수 있다.
- Configuration History 전용 조건 기반 직접 다운로드 endpoint는 두지 않는다. History 목록에서 `fileId`를 받은 뒤 공통 `/files/download?fileId=...`를 사용한다.
