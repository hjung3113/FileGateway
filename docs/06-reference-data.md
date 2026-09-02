# DB 및 기준정보 연계

## DB

- Microsoft SQL Server
- FileGateway는 Stored Procedure로 기준정보를 조회한다.
- DB 테이블 구조는 애플리케이션/API에 노출하지 않는다.

단일 SP `FileGateway_GetReferenceData`가 다음 순서로 4개 result set을 반환한다.

1. `Equipments`: `EquipmentId` (`ServerId` 아님)
2. `Servers`: `ServerId`, `Host`, `FileRootPath`
3. `LogDefinitions`: `EquipmentId`, `LogType`, `ServerId`, `GenerationType`, `DirectoryTemplate`, `FileNamePattern`, `SlotCardinality`, `MetadataParseMode`, `RelativePathMetadataPattern`, `MetadataGroupMappings`
4. `ConfigurationDefinitions`: `EquipmentId`, `ConfigurationType`, `ServerId`, `CurrentDirectoryTemplate`, `CurrentFileNamePattern`, `CurrentFileNameMatchMode`, `HistoryDirectoryTemplate`, `HistoryFileNamePattern`, `HistoryFileNameMatchMode`, `HistoryCompletionMarkerPathTemplate`, `HistoryTimestampParseMode`, `HistoryFileNameTimestampPattern`, `HistoryTimestampMappings`

`MetadataGroupMappings`와 `HistoryTimestampMappings`은 JSON 배열 `[{"group":"...","target":"...","format":"..."}]`이며 `format`은 선택이다. SP/스키마 스크립트는 `db/`에 테스트·개발용 계약 구현으로 제공하고 운영 DB 내부 구조는 이 계약만 지키면 자유롭다.

`ConfigurationDefinitions` result set은 위 컬럼을 **위 순서와 무관하게** 항상 반환한다. 행이 0개여도 필수 컬럼 이름과 13개 컬럼 shape를 검증하며, 구 SP의 8컬럼 shape는 `ReferenceDataUnavailable`로 이어지는 fail-closed 오류다. 신규 Configuration 컬럼의 NULL/빈 값은 `Glob` 및 metadata rule 없음으로 해석한다.

## Schema/SP 및 애플리케이션 배포 순서

Issue #21의 신규 Configuration 기준정보는 다음 3단계 순서를 지킨다.

1. `db/mvp-schema.sql`의 5개 컬럼과 신규 Stored Procedure를 함께 배포한다. 모든 기존 row는 빈 mode/metadata 값으로 유지한다.
2. 신규 13컬럼 result set을 읽는 application을 **전 인스턴스**에 배포해 구 app 인스턴스가 혼재하지 않도록 한다.
3. 신규 `Literal | Glob | Regex` mode, `regex:` path, metadata rule 기준정보를 활성화한다. 2단계가 끝나기 전에는 신규 값을 활성화하지 않는다.

신규 application과 구 SP의 조합은 최초 기준정보 로딩에서 result set shape 오류로 즉시 실패해야 하며 silent mismatch를 허용하지 않는다. Rollback 시에는 신규 application을 구 버전으로 되돌리기 전에 신규 mode/metadata 값과 `regex:` path를 먼저 legacy 값으로 비활성화한다.

## SP가 제공해야 하는 논리 정보

### 서버/설비 매핑

- `Equipments` result set: `EquipmentId`
- `Servers` result set: `ServerId`, `Host`, `FileRootPath`

`equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자이며 하나의 FileGateway 배포 범위 안에서 유일하다.

`rootPath`는 해당 서버에서 FileGateway가 접근할 수 있는 물리 경로의 보안 경계다. 모든 discovery/current/history 규칙의 최종 정규화 경로는 반드시 이 root 아래에 있어야 한다.

`host`가 `localhost`(대소문자 무시, 앞뒤 공백 제거 후 정확히 일치)인 서버는 FTP 대신 동일 머신 파일시스템에서 직접 읽는다. 이 경우 `rootPath`는 로컬 절대 경로여야 하며(상대 경로는 즉시 오류), 라우팅/경로 검증 규칙은 `03-server-access-core.md`를 따른다. `127.0.0.1`, `::1`, 머신명 등 다른 값은 모두 FTP 서버로 취급된다.

### 로그 정의

- `EquipmentId`
- `LogType`
- `ServerId`
- `GenerationType`: `Hourly | Daily | Continuous`
- `DirectoryTemplate`
- `FileNamePattern`
- `SlotCardinality`
- `MetadataParseMode`
- `RelativePathMetadataPattern`
- `MetadataGroupMappings`

하나의 FileGateway 배포 범위에서 `equipmentId + logType`은 정확히 하나의 로그 정의를 식별한다. 동일 조합의 중복 정의는 기준정보 오류다.

MVP에서 하나의 로그 정의는 하나의 discovery rule만 가진다. 현재 동일 `logType` 조회에서 서로 다른 디렉터리/파일명 규칙을 동시에 검색해야 하는 사례가 없으므로 다중 discovery rule 모델은 두지 않는다. 같은 생성 슬롯의 여러 파일은 `cardinality=Multiple`로 표현한다.

`pathTemplate`은 조회조건과 논리 생성 슬롯으로 탐색할 논리 디렉터리를 계산한다. `filePattern`은 해당 디렉터리에서 후보 **파일명**을 선택하는 glob matcher다. 예: `*.zip`, `Event_*.log`, `PM?.cfg`. `timestamp`/`subtype`/`attributes` 추출은 metadata rule이 담당한다.

`filePattern`은 정규식으로 사용하지 않는다. FTP 서버 자체 wildcard 구현에도 의존하지 않고 FileGateway가 받은 디렉터리 목록에 동일한 glob 의미를 적용한다. MVP Windows/IIS FTP 환경에서는 파일명 glob 비교를 case-insensitive로 수행하고 실제 casing은 응답에 보존한다.

MVP에서는 root부터 하위 전체를 훑는 무제한 recursive scan을 허용하지 않는다. `pathTemplate`이 Hourly/Daily 조회 범위 또는 Continuous 현재 슬롯에서 필요한 디렉터리를 직접 계산해야 한다. 여러 슬롯이 같은 디렉터리를 계산하면 중복 목록 조회하지 않는다.

MetadataRule은 물리 FTP root를 제외한 **정규화된 논리 relative path + fileName**을 입력으로 사용한다. 경로 구분자는 플랫폼/FTP 표현과 무관하게 `/`로 통일한다. 단순한 결정적 레이아웃은 `Template`, 복잡한 예외는 `Regex` named group을 사용한다.

후보 파일이 `filePattern`에 일치했지만 필수 metadata를 해석하지 못하면 해당 파일만 결과 후보에서 제외한다. Hourly/Daily는 요청 시간 범위 `[from,to)`로 후보를 먼저 제한한 뒤 범위 안의 파일에 대해서만 `cardinality=Single` 및 logical identity 충돌을 검증한다.

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
- `historyRule`: 날짜별 History 디렉터리/파일 패턴, Snapshot File의 `snapshotTimestamp` 파생 규칙, 완료 marker 파일명/위치를 해석하는 규칙

`currentRule.pathTemplate`과 `historyRule.pathTemplate`은 `/`로 구분하는 상대 경로다. 빈 세그먼트는 제거한 뒤 파싱한다. 각 세그먼트는 `regex:PATTERN`(자식 디렉터리 이름 매칭, 비어 있지 않고 `/`를 포함하지 않으며 `^...$` anchor 필수) 또는 리터럴/기존 날짜 token(`{yyyy}` `{MM}` `{dd}` `{HH}`) template으로 해석한다. Template token은 논리 슬롯의 Site local(`Asia/Seoul`) 구성요소로 치환하며 token 없는 고정 경로도 허용한다. 비-regex 세그먼트에는 `..`, rooted 경로, `:`를 금지한다. `HistoryCompletionMarkerPathTemplate`은 확정된 template 경로만 허용하며 `regex:` 세그먼트는 사용할 수 없다.

`CurrentFileNameMatchMode`와 `HistoryFileNameMatchMode`는 `Literal | Glob | Regex`이며 NULL/빈 값은 기존 의미인 `Glob`이다. `Literal`과 `Glob`은 case-insensitive로 비교하고 `Regex`는 파일명 전체를 `IgnoreCase | CultureInvariant`로 매칭한다. 정규식은 anchor와 컴파일 가능성을 검증한다.

`HistoryTimestampParseMode`, `HistoryFileNameTimestampPattern`, `HistoryTimestampMappings`는 선택적인 ConfigurationMetadataRule을 표현한다. 입력은 물리 root를 제외한 경로가 아니라 **fileName**이다. `Template`은 fileName의 첫 `.` 앞 stem에 매칭하고 `{yyyy}` `{MM}` `{dd}` `{HH}` `{mm}` token에서 timestamp를 파생하므로 `.zip`, `.gz`, `.txt.gz`처럼 확장자가 달라도 같은 stem을 처리한다. Template의 mappings는 비어 있어야 한다. `Regex`는 fileName 전체를 매칭하며 timestamp 전체를 담는 단일 named group mapping 정확히 1개만 허용하고, target은 `timestamp`, format은 필수다. Metadata 매칭은 `IgnoreCase` 정책을 사용한다. 후보가 rule에 매칭되지 않거나 timestamp를 추출하지 못하면 누락하지 않고 `FileDefinitionConflict`로 처리한다.

`HistoryTimestampMappings`는 Logs의 `MetadataGroupMappings`와 동일한 JSON 배열 `[{"group":"...","target":"...","format":"..."}]` 형태를 사용한다. Configuration에서는 Regex 모드의 단일 timestamp mapping만 허용하며 Template 모드에서는 빈 배열이어야 한다.

하나의 `equipmentId + configurationType` 아래 PM1/PM2/PM3/PM4처럼 여러 Current Configuration File이 존재할 수 있다. 이 파일들을 별도 `configurationType`, `subtype`, `attributes`로 세분화하지 않는다.

Current Configuration File의 logical identity는 `equipmentId + configurationType + fileName`이다. MVP에서 `fileName` 구성요소는 case-insensitive로 비교하며 casing만 다른 이름은 같은 논리 파일로 취급한다.

History는 별도 시스템이 날짜 폴더를 만들고 Current 파일 집합을 복사한 결과다. **물리 batch**는 날짜 폴더와 그 복사 완료 marker가 이루는 단위다. 물리 batch 안의 파일들은 metadata rule 유무에 따라 `snapshotTimestamp`를 공유하며, 한 물리 batch에 여러 **Snapshot Set**(그룹핑 키는 `snapshotTimestamp` 단독)이 있을 수 있다. Snapshot File identity는 `(snapshotTimestamp, fileName)`이며 `fileName`은 case-insensitive로 비교한다. Metadata rule이 없으면 `snapshotTimestamp`는 해당 날짜의 Site local `00:00`이고, rule이 있으면 fileName에서 추출한 시각이다. FTP modified time은 snapshot 시각으로 사용하지 않는다.

History 생산자는 물리 batch(날짜 폴더 복사) 완료 시 marker 파일을 생성한다. marker 파일명/위치는 `historyRule`에 있으며 FileGateway는 **marker 존재 여부만 확인**한다. marker 내용은 읽거나 해석하지 않는다. marker가 없는 물리 batch는 History 결과에 포함하지 않으며 marker 자체도 Configuration File 후보로 반환하지 않는다. 한 물리 batch 안의 여러 Snapshot Set은 동일 marker로 함께 게이팅된다.

Configuration Snapshot File의 logical identity는 `equipmentId + configurationType + snapshotTimestamp + fileName`이다. `fileName` 구성요소는 case-insensitive로 비교하며, cursor도 `snapshotTimestamp + fileName` 의미를 그대로 유지한다.

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

MVP Configuration 정의에는 `subtype`/동적 `attributes` 규칙을 추가하지 않는다.

FTP 비밀번호 등 credential은 SP에서 반환하지 않는다.

### `FgConfigurationDefinition` 조회 결과 상세 스펙

SP `FileGateway_GetReferenceData`의 4번째 result set `ConfigurationDefinitions`는 아래 13개 컬럼을 반환한다. 컬럼 순서는 계약이 아니며 이름으로 식별한다. 이 절은 각 컬럼 값이 **정확히 어떻게 작성되고 해석되는지**의 상세 계약이다. SP는 NULL을 빈 문자열로 변환(`ISNULL(..., '')`)해 반환하며, 신규 Configuration 컬럼의 NULL/빈 값은 모두 "기존(8컬럼 시대) 정의와 동일한 의미"로 해석된다.

공통 규칙:

- 모든 값은 앞뒤 공백이 제거되지 않은 채 전달된다. 경로는 파싱 시 세그먼트 단위로 `Trim`된다.
- `EquipmentId + ConfigurationType`이 하나의 정의를 식별한다(PK). 대소문자 정책은 아래 컬럼 표 참조.
- 경로 구분자는 `/` 뿐이다. `\`는 치환하지 않고 비-regex 세그먼트에서는 안전 위반으로 거부한다(regex 세그먼트의 `\d`, `\.` 등 .NET escape 보존).

| 컬럼 | 상세 스펙 |
|---|---|
| `EquipmentId` | 논리 설비 식별자(nvarchar(64), NOT NULL). 표시명이 아닌 안정 식별자이며 배포 범위에서 유일. 다른 컬럼과 마찬가지로 Equipment 매핑 result set에 존재해야 하고, `ServerId`도 `Servers`에 존재해야 한다. 값 자체는 대소문자를 구분해 매칭한다(기준정보 내 Equipments 집합과의 정확한 일치). |
| `ConfigurationType` | 업무 Configuration 종류(nvarchar(128), NOT NULL). `EquipmentId + ConfigurationType`이 정의의 PK다. API query 값과의 비교는 case-insensitive다. |
| `CurrentDirectoryTemplate` | Current 파일 집합 디렉터리. **`RootPath` 기준 상대경로**(선행 `/`·`\` 금지, `/` 세그먼트 구분, 드라이브/절대경로 표현 금지). 빈 세그먼트(`a//b`)와 앞뒤 공백은 제거한 뒤 파싱. 각 세그먼트는 (1) Template 세그먼트 = literal + 날짜 token `{yyyy}` `{MM}` `{dd}` `{HH}`(token 없는 고정 경로 허용) 또는 (2) `regex:PATTERN` 세그먼트. 비-regex 세그먼트는 `..`, `:`, `\`, rooted 표현 금지. `regex:`는 예약어 접두사다. Current는 날짜 token 의무 없음 — token이 있으면 resolve 시작 시 `TimeProvider`의 Site local 현재 시각으로 정확히 한 번 캡처해 모든 token을 같은 slot으로 확장한다. |
| `CurrentFileNamePattern` | Current 후보 **파일명** 패턴(nvarchar(256)). 경로(`/`) 포함 금지 — 파일명만 매칭한다. 기본 해석은 glob(`*`, `?`, case-insensitive). `CurrentFileNameMatchMode`로 해석이 전환된다. |
| `CurrentFileNameMatchMode` | `Literal \| Glob \| Regex`(nvarchar(16), 기본 `''`). **빈 값/NULL = `Glob`**(기존 정의 호환). `Literal` = case-insensitive 전체 동등(빈 값 금지, `/` 금지). `Regex` = 패턴이 `^...$` anchor 필수, 컴파일 가능해야 하며 파일명 전체 매칭(`\A(?:...)\z` wrap으로 부분 일치 불가), `IgnoreCase \| CultureInvariant` 비교, runtime timeout 250ms 초과 시 `FileDefinitionConflict`. |
| `HistoryDirectoryTemplate` | History 날짜별 디렉터리. 경로 규칙(상대경로, 빈 세그먼트 제거, Template/Regex 세그먼트, 비-regex 세그먼트 제약)은 `CurrentDirectoryTemplate`과 동일. 단 **비-regex 세그먼트에 `{yyyy}` `{MM}` `{dd}`가 필수이고 `{HH}`는 금지**다(regex 세그먼트의 `{2}` 같은 수량자는 token 검사 대상이 아니다). Template token은 조회 슬롯 날짜의 Site local(`Asia/Seoul`) 구성요소로 확장한다. `regex:^PM[0-9]$` 같은 세그먼트가 있으면 서버가 해당 prefix의 자식 디렉터리를 열거해 매칭(fan-out)하고, 매칭 자식이 없으면 그 branch는 정상 결과 0건이다. |
| `HistoryFileNamePattern` | Snapshot 후보 파일명 패턴(nvarchar(256)). 파일명만 매칭, `/` 금지. 기본 해석 glob(ci). `HistoryFileNameMatchMode`로 전환. |
| `HistoryFileNameMatchMode` | `Literal \| Glob \| Regex`. 빈 값/NULL = `Glob`. 규칙은 `CurrentFileNameMatchMode`와 동일(ci Literal, anchored 전체 일치 Regex + 250ms timeout → `FileDefinitionConflict`). |
| `HistoryCompletionMarkerPathTemplate` | 물리 batch 완료 marker의 확정 경로(nvarchar(512)). `RootPath` 기준 상대경로이며 **Template 세그먼트만 허용**(`regex:` 세그먼트 금지) — 존재 여부만 확인하는 확정 1개 경로다. 날짜 규칙은 HistoryDirectoryTemplate와 동일하게 비-regex 세그먼트에 `{yyyy}{MM}{dd}` 필수, `{HH}` 금지. marker 내용은 읽지 않는다. |
| `HistoryTimestampParseMode` | `'' \| Template \| Regex`(nvarchar(16), 기본 `''`). **빈 값 = metadata rule 없음** — 기존 동작대로 `snapshotTimestamp`가 해당 날짜 폴더의 Site local 자정(`00:00`)이 된다. `Template` = fileName의 첫 `.` 앞 stem에 매칭(확장자 독립). `Regex` = fileName 전체 매칭(anchor 필수). 둘 다 `IgnoreCase` 정책. |
| `HistoryFileNameTimestampPattern` | mode별 문법(nvarchar(1024), 기본 `''`). `Template`: token `{yyyy}` `{MM}` `{dd}` 필수 + `{HH}` `{mm}` 선택, 그 외 `{...}` token 금지. 시/분 token은 범위 검사(`HH` 0–23, `mm` 0–59)를 통과해야 하고 없으면 `00`으로 해석한다. `Regex`: `^...$` anchor 필수, 컴파일 가능, timestamp 전체를 담는 단일 named group이 있어야 한다. |
| `HistoryTimestampMappings` | Logs의 `MetadataGroupMappings`와 동일한 JSON 배열 `[{"group":"...","target":"...","format":"..."}]`(nvarchar(max), 기본 `''` = 빈 매핑). Configuration에서는 `Regex` 모드일 때 **정확히 1개 mapping**만 허용하며 `target`은 `"timestamp"`만, `format`은 필수다. `format`은 `DateTime.TryParseExact`(InvariantCulture) 형식이고 문자 letter는 `y M d H m`만 허용되며 `y`, `M`, `d`는 필수다(offset/ampm/fraction 지정자 금지). `Template` 모드에서는 mappings가 반드시 비어 있어야 한다. 예: `[{"group":"ts","target":"timestamp","format":"yyyyMMddHHmm"}]` |

#### snapshotTimestamp 파생 규칙

- metadata rule이 없으면(`HistoryTimestampParseMode` 빈 값) 같은 물리 batch(날짜 폴더)의 모든 Snapshot File이 해당 날짜의 Site local `00:00`을 공유한다.
- rule이 있으면 `HistoryFileNamePattern`을 통과한 후보의 fileName에서 timestamp를 추출한 값이 `snapshotTimestamp`다. Template은 stem 기반, Regex는 named group 값 전체를 format으로 해석하며, 해석된 값은 offset 지정 없이 Site local(`Asia/Seoul`)로 해석한다.
- 후보가 rule에 매칭되지 않거나 timestamp 해석에 실패하면 누락시키지 않고 `FileDefinitionConflict`다. regex runtime timeout도 같은 오류로 변환된다.
- **추출 timestamp의 Site local 날짜가 물리 날짜 폴더 슬롯과 일치하지 않으면 `FileDefinitionConflict`**다(예: `2026-08-29` 폴더 안 파일에서 `2026-08-28` timestamp 추출).

#### 배포 순서 요약

신규 컬럼/mode/regex 기준정보는 위 "Schema/SP 및 애플리케이션 배포 순서"의 3단계를 그대로 따른다: (1) schema 5컬럼 + 신규 SP 동시 배포(기존 row는 빈 값 유지 → 기존 의미), (2) 13컬럼 result set을 읽는 app 전 인스턴스 배포, (3) 신규 mode/`regex:`/metadata 값 활성화. 신규 app + 구 SP 조합은 최초 로딩에서 shape 오류로 즉시 실패한다(fail-closed).

#### 예시 정의 rows

등대 시나리오 — 물리 구조: `RootPath` 아래 `config/current/`, `config/history/{yyyy}{MM}{dd}/PM1/` 같은 자식 폴더, snapshot 파일명은 10자리 `yyyyMMddHH` 시각을 담는 `^\d{10}(\.txt)?\.gz$` 형태(예: `2026082910.txt.gz`)인 경우:

| EquipmentId | ConfigurationType | ServerId | CurrentDirectoryTemplate | CurrentFileNamePattern | CurrentFileNameMatchMode | HistoryDirectoryTemplate | HistoryFileNamePattern | HistoryFileNameMatchMode | HistoryCompletionMarkerPathTemplate | HistoryTimestampParseMode | HistoryFileNameTimestampPattern | HistoryTimestampMappings |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `EQ-LH-001` | `PM` | `srv-lh` | `config/current` | `PM*.cfg` | `` | `config/history/{yyyy}{MM}{dd}` | `*` | `` | `config/history/{yyyy}{MM}{dd}/DONE.marker` | `` | `` | `` |
| `EQ-LH-002` | `PM` | `srv-lh` | `config/current` | `^PM[0-9]{1}\.cfg$` | `Regex` | `config/history/{yyyy}{MM}{dd}/regex:^PM[0-9]$` | `^\d{10}(\.txt)?\.gz$` | `Regex` | `config/history/{yyyy}{MM}{dd}/DONE.marker` | `Regex` | `^(?<ts>\d{10})(\.txt)?\.gz$` | `[{"group":"ts","target":"timestamp","format":"yyyyMMddHH"}]` |
| `EQ-LH-003` | `Recipe` | `srv-lh` | `config/current` | `RECIPE` | `Literal` | `config/history/{yyyy}{MM}{dd}` | `RECIPE_*` | `` | `config/history/{yyyy}{MM}{dd}/DONE.marker` | `Template` | `RECIPE_{yyyy}{MM}{dd}` | `` |

해석 예:

- row 1: 전부 기본값 — `2026-08-29` 폴더의 파일들은 marker가 있으면 `snapshotTimestamp = 2026-08-29T00:00:00+09:00`로 조회된다.
- row 2: History 경로가 날짜 폴더 + `PM0`~`PM9` 자식 디렉터리로 fan-out되고, 파일명 `2026082910.txt.gz`에서 named group `ts` 값 `2026082910`이 format `yyyyMMddHH`로 해석돼 `snapshotTimestamp = 2026-08-29T10:00:00+09:00`가 된다(자정 아님). 추출 날짜가 폴더 날짜와 다르면 `FileDefinitionConflict`.
- row 3: Current는 `RECIPE`와 case-insensitive 전체 동등인 파일 1종. History는 `RECIPE_20260829.dat`, `RECIPE_20260829.txt.gz`처럼 확장자가 달라도 stem(`RECIPE_20260829`)이 Template에 매칭되고, `{HH}` `{mm}` 선택 token 부재로 자정 `00:00` snapshot이 된다.

## 설비별 제공 파일 종류 조회

외부 `GET /api/v1/equipments/{equipmentId}/file-types`는 별도의 물리 파일 catalog를 만들지 않고 **전역 계약을 통과하고 정의 단위 검증을 거친 기준정보 snapshot에서 해당 설비의 유효 정의를 투영**해 반환한다.

- Log: 해당 `equipmentId`의 `EquipmentLogDefinition`들에서 `logType + generationType` 추출
- Configuration: 해당 `equipmentId`의 `EquipmentConfigurationDefinition`들에서 `configurationType` 추출
- FTP 디렉터리/파일 존재 여부를 확인하지 않음
- serverId/host/rootPath/discoveryRule/metadataRule/currentRule/historyRule 같은 내부 정보는 API에 노출하지 않음
- 유효한 설비에 정의가 하나도 없으면 빈 목록으로 반환 가능
- 정의 단위 검증에 실패한 Log/Configuration은 목록에서 제외하며, 직접 조회하면 각각 `LogDefinitionNotFound`/`ConfigurationDefinitionNotFound`
- 기존 계약으로 표현 가능한 새 Log/Configuration 종류가 기준정보에 추가되면 정상 cache refresh 후 자동으로 조회 결과에 포함

설비사/설비 종류별로 제공 가능한 파일이 다른 것은 **`equipmentId`에 최종적으로 연결된 정의 집합의 차이**로 표현한다. DB 내부에서 설비사 공통 정의를 정규화하거나 재사용하는 방식은 DB/SP 구현 세부사항이며, FileGateway 코드에 설비사별 분기를 만들지 않는다.

현재 기준정보 snapshot에 필요한 전체 정의가 이미 포함된다면 별도 전용 테이블은 필요하지 않다. Stored Procedure 계약이 분리되어 있다면 설비별 제공 정의를 읽을 수 있도록 SP 조회 계약만 보완하고 DB 물리 구조는 애플리케이션 계약으로 고정하지 않는다.

## 파일명 비교 규칙

MVP Windows/IIS FTP 환경에서 파일명 관련 비교는 case-insensitive다.

- `filePattern` 및 Configuration 후보 파일명 matching
- Configuration path의 directory segment matching
- Configuration file `Regex` matching
- Configuration metadata matching
- Log/Current/Snapshot logical identity의 `fileName`
- `fileName ASC` 정렬
- continuation cursor의 `fileName`

실제 원격 파일 casing은 API 응답에 그대로 보존한다. 정규식은 .NET에서 `OrdinalIgnoreCase` 옵션이 없으므로 `IgnoreCase | CultureInvariant`를 표준 근사로 사용한다. PM1, Port3, Config 같은 ASCII 이름에서는 `OrdinalIgnoreCase`와 동등하다. `subtype`/`attributes` 비교는 이 규칙과 무관하게 기존대로 case-sensitive다. 향후 case-sensitive 저장소를 도입할 때 fileName 계약과 함께 재검토한다.

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

Snapshot `fileId` 재해석의 검색 predicate는 요청한 `snapshotTimestamp`와의 **정확한 일치**(`SnapshotTimestamp == ts`) 및 case-insensitive `fileName` 일치다. 이름만으로 선택하거나 `[ts, ts+1일)` 안의 다른 timestamp를 허용하지 않는다. Metadata rule로 추출한 timestamp의 Site local 날짜가 물리 날짜 슬롯과 다르면 목록 단계에서 `FileDefinitionConflict`로 거부한다. 이 불변식으로 목록에서 발급한 `fileId`가 같은 물리 슬롯을 다시 방문해 round-trip된다.

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

기준정보 갱신은 **필수 result set과 Equipment/Server 전역 식별자를 먼저 검증한 뒤, 각 Log/Configuration 정의를 독립적으로 검증하고 유효 정의만 담은 새 snapshot을 한 번에 atomic 교체**한다. 무효 정의를 이전 snapshot의 정의로 보완하는 혼합 상태는 만들지 않는다.

lazy refresh는 프로세스당 하나만 실행하는 **single-flight** 방식으로 동기화한다.

- last-known-good cache가 있으면 refresh가 진행 중인 동안 다른 요청은 기존 cache로 계속 처리한다.
- 최초 로딩이라 usable cache가 없으면 동시 요청들은 동일한 최초 refresh 결과를 공유한다.
- TTL 만료 시 요청마다 별도 Stored Procedure refresh를 발생시키지 않는다.

갱신 시도 결과:

- DB/SP 조회 실패, 필수 result set/shape 누락, Equipment/Server 전역 식별자 검증 실패 → 새 snapshot을 만들지 않음
- 위 전역 검증 성공 → invalid Log/Configuration 정의를 해당 key 단위로 격리하고, 나머지 정상 정의를 담은 새 snapshot으로 atomic 교체
- 전역 검증 실패 + 이전 정상 cache 존재 → 새 데이터를 적용하지 않고 마지막 정상 cache 전체를 stale 상태로 계속 사용
- 최초 로딩에서 조회 또는 전역 검증 실패하여 usable cache가 없음 → `ReferenceDataUnavailable`
- 개별 정의가 invalid인 경우에도 전역 검증을 통과하면 refresh는 성공하며, 해당 정의는 새 snapshot에서 제외된다

동일 `equipmentId + logType` 또는 `equipmentId + configurationType`이 여러 행에 나타나 authoritative row를 정할 수 없으면 충돌한 모든 행을 invalid 처리한다. 하나를 임의의 승자로 선택하지 않는다.

stale cache 사용 여부, 마지막 정상 갱신 시각, refresh/validation 실패 원인은 운영 로그/메트릭에서 관측 가능해야 한다.
refresh 실패 로그는 전역 식별자 validation, 필수 SP result set/shape, 그 밖의 source read 실패를 구분하며 각 범주의 실제 원인을 함께 기록한다.

로컬 영속 fallback/분산 cache는 MVP에서 제외한다.

## 필수 검증

SP 결과 전체에 대해 cache 교체 전에 **result set 계약, 전역 식별자, 각 정의의 구조·문법·invariant만** 검증한다. 이 단계에서 FTP 서버에 접속해 실제 디렉터리, 파일, marker 존재 여부를 확인하지 않는다. 원격 저장소의 실재 상태는 실제 조회/metadata/download 요청 시 확인한다.

필수 result set 누락/shape 오류와 Equipment/Server 테이블의 전역 식별자 무결성 오류는 전체 refresh 실패다. 반면 특정 Log/Configuration 행의 enum/JSON/validator 오류, unknown reference 또는 정의 key 중복은 해당 정의만 invalid 처리한다.

검증 항목:

- 설비/서버 매핑 존재 여부
- 로그/Configuration 정의 존재 여부
- `equipmentId + logType` 중복 정의 여부(충돌한 모든 행 invalid)
- `equipmentId + configurationType` 중복 정의 여부(충돌한 모든 행 invalid)
- 개별 Log/Configuration의 enum, JSON, validator, `equipmentId`/`serverId` reference 오류
- 중복/충돌 매핑
- root/path template의 구조와 정규화 가능 여부
- Configuration pathTemplate의 세그먼트 분류, 빈 세그먼트 제거 후 파싱, `regex:` pattern의 anchor/컴파일 가능성, marker template의 `regex:` 금지
- 정규화 후 `rootPath` 밖으로 탈출 가능한 정의 여부
- `filePattern`이 지원되는 glob 문법인지
- Configuration file match mode(`Literal | Glob | Regex`)와 Regex pattern의 anchor/컴파일 가능성
- 무제한 recursive scan을 요구하는 정의가 아닌지
- MetadataRule 입력 정규화와 지원 mode가 유효한지
- Configuration MetadataRule의 fileName/stem 입력, token 또는 단일 timestamp named group, `target=timestamp`, `format`, IgnoreCase 규칙
- Current rule의 구조/패턴이 유효한지
- History rule의 날짜별 경로, 논리 시각, marker 파일명/위치 정의가 유효한지
- 지원하지 않는 generation/metadata mode
- 유효하지 않은 regex/template/mapping
- 각 result set의 필수 컬럼 이름과 중복 여부 및 `ConfigurationDefinitions`의 13개 컬럼 shape(행이 0개인 경우 포함)

실제 원격 탐색에서는 다음을 별도로 판정한다.

- 계산된 디렉터리가 존재하지 않음 → 해당 슬롯의 정상 결과 0개
- `cardinality=Single`인데 하나의 논리 생성 슬롯에서 여러 파일 발견 → `FileDefinitionConflict`
- case-insensitive 기준 동일 파일명이 둘 이상 발견 → `FileDefinitionConflict`
- `filePattern` 후보의 필수 metadata 해석 실패 → 해당 파일만 결과 후보에서 제외
- Regex runtime timeout → `FileDefinitionConflict`
- 물리 날짜 슬롯과 metadata 추출 timestamp의 Site local 날짜 불일치 → `FileDefinitionConflict`
- 파일 서버 연결/인증/프로토콜 장애 → 파일 서버 오류

기준정보 오류와 실제 파일 서버 장애는 별도 원인으로 유지한다.
