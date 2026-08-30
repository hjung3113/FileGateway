# FileGateway 용어집

FileGateway 설계와 구현에서 사용하는 **정식 용어(canonical language)**를 정의한다. 같은 개념을 다른 이름으로 부르지 않으며, 역할별 문서에서 용어가 충돌하면 이 문서의 정의에 맞춰 정리한다.

## Language

**Equipment ID (`equipmentId`)**:
클라이언트와 기준정보가 공통으로 사용하는 안정적인 논리 설비 식별자다. 사람이 보는 표시명과 구분하며, 하나의 FileGateway 배포 범위 안에서 유일하고 표시명이 바뀌어도 동일 설비를 식별하는 값은 유지되는 것을 전제로 한다.
_Avoid_: `equipment`, 설비명(식별자 의미로 사용할 때)

**Log Type (`logType`)**:
업무적으로 어떤 종류의 로그인지를 나타내는 분류다. 파일 생성 주기나 생명주기를 나타내는 `generationType`과는 별개다.
_Avoid_: Hourly/Daily/Continuous를 로그 종류라고 부르는 표현

**Generation Type (`generationType`)**:
로그 파일의 생성 주기 또는 생명주기를 나타내는 분류다. 현재 값은 `Hourly | Daily | Continuous`다. Hourly/Daily는 시간 범위를 사용하고 Continuous는 현재 파일 집합을 조회하며 `from`/`to`를 허용하지 않는다.
_Avoid_: `logType`, 로그 종류

**Configuration File**:
설비가 실제 동작에 사용하는 파라미터 값들이 저장된 설정 파일이다. 로그가 아니며 현재 설정파일과 파일 서버에 보관되는 히스토리 설정파일 모두 FileGateway의 MVP 제공 대상이다.
_Avoid_: Configuration 로그, Configuration 계열 로그

**Configuration Type (`configurationType`)**:
설비 설정파일을 업무 의미에 따라 구분하는 안정적인 논리 분류다. 하나의 `equipmentId + configurationType` 아래에 현재 파일이 여러 개 존재할 수 있다. PM1/PM2/PM3/PM4처럼 같은 Configuration Type에 속하는 개별 파일을 각각 별도 `configurationType`이나 `subtype`으로 세분화하지 않는다. 실제 파일명과도 분리한다.
_Avoid_: 개별 파일마다 configurationType/subtype을 생성, 파일명을 configuration type으로 직접 사용

**Current Configuration Set**:
특정 `equipmentId + configurationType`에 대해 현재 존재하는 Configuration File들의 집합이다. 하나의 파일로 한정하지 않으며 파일 수는 Configuration Type에 따라 달라질 수 있다. FileGateway는 이 집합의 버전을 고정하거나 히스토리를 생성하지 않는다.
_Avoid_: `equipmentId + configurationType`이 항상 파일 하나를 식별한다고 가정

**Current Configuration File**:
`Current Configuration Set` 안의 개별 현재 파일이다. 논리 identity는 `equipmentId + configurationType + fileName`이며 파일 내용은 시간에 따라 바뀔 수 있다. 같은 identity의 `fileId`는 특정 바이트 버전을 고정하지 않고 다운로드 시점의 현재 내용을 가리킨다. `fileName`은 MVP에서 case-insensitive 비교하므로 casing만 바뀐 이름은 같은 논리 파일이며, 그 외 이름 변경은 다른 논리 파일이다.
_Avoid_: 현재 파일을 불변 snapshot으로 간주, PM1/PM2를 별도 subtype으로 모델링

**Configuration Snapshot Set**:
별도 시스템이 특정 시점에 `Current Configuration Set`의 파일들을 그대로 복사해 보관한 히스토리 파일 집합이다. 물리 batch(날짜 폴더와 복사 완료 marker가 이루는 단위) 안에서 같은 `snapshotTimestamp`를 공유하는 파일들의 집합이며, metadata rule이 있으면 하나의 물리 batch에 여러 Snapshot Set이 있을 수 있다. History 생산자가 만든 물리 batch 완료 marker가 존재해야 FileGateway 조회 대상이 된다.
_Avoid_: Snapshot을 항상 단일 파일 하나라고 가정

**Configuration Snapshot File**:
`Configuration Snapshot Set` 안의 개별 히스토리 파일이다. 생성이 완료된 뒤에는 불변이며 수정이 필요하면 다음 snapshot에서 새 파일로 반영한다. FileGateway는 생성·보관 책임 없이 이미 저장된 파일을 조회·다운로드만 한다.
_Avoid_: FileGateway가 생성하는 configuration backup, 생성 완료 후 내용이 바뀌는 snapshot 파일

**Configuration Snapshot Timestamp (`snapshotTimestamp`)**:
Configuration Snapshot File이 속한 Snapshot Set의 논리 시각이다. 정의에 metadata rule이 없으면 물리 날짜 폴더에 대응하는 Site local 자정이고, rule이 있으면 파일명 규칙에서 추출한 시각이다. 추출된 timestamp의 Site local 날짜는 물리 날짜 슬롯과 일치해야 하며, FTP modified time과 동일시하지 않는다.
_Avoid_: FTP modified time

**History Completion Marker**:
Configuration History 생산자가 물리 batch(날짜 폴더 복사) 완료 후 생성하는 marker 파일이다. marker는 개별 Snapshot Set이 아니라 물리 batch의 완성을 표시하므로 한 batch에 여러 Snapshot Set이 포함될 수 있다. marker 이름/위치는 기준정보의 `historyRule`이 정의하며 FileGateway는 **존재 여부만 확인**하고 marker 내용은 읽거나 해석하지 않는다.
_Avoid_: marker 내용을 업무 metadata로 해석, FileGateway가 marker를 생성

**Logical Timestamp (`timestamp`)**:
파일명/경로의 메타데이터 규칙에서 추출한, 해당 로그 파일이 논리적으로 나타내는 시각이다. FTP 파일의 수정 시각이나 서버 파일시스템 시각을 의미하지 않는다. 현재 Site의 운영 시간대인 `Asia/Seoul`로 해석하고 API에서는 UTC offset이 포함된 ISO-8601 값으로 표현한다.
_Avoid_: modified time, FTP modification time을 `timestamp`와 동일시하는 표현

**Time Range (`from`, `to`)**:
Hourly/Daily 및 Configuration History의 시간 기반 조회에 적용하는 반개구간 `[from, to)`이다. `from`은 포함하고 `to`는 제외한다. Continuous 로그에는 적용하지 않는다.
_Avoid_: `to` 포함 여부가 문맥에 따라 달라지는 표현, Continuous에 시간 범위를 적용

**File Name Comparison**:
MVP Windows/IIS FTP 환경에서 `fileName` 관련 비교에 사용하는 case-insensitive 규칙이다. 구현은 `StringComparer.OrdinalIgnoreCase`로 단일화하며 `filePattern` matching, logical identity, 정렬, pagination cursor에 동일하게 적용한다. 실제 파일명의 casing은 응답에 그대로 보존하며 `subtype`/`attributes` 비교는 이 규칙과 무관하게 case-sensitive다.
_Avoid_: 위치마다 다른 파일명 대소문자 규칙

**Logical File Identity**:
물리 서버나 경로가 바뀌어도 같은 논리 파일을 다시 식별하기 위한 값의 조합이다. 로그는 `equipmentId + logType + timestamp + fileName`, Configuration Snapshot File은 `equipmentId + configurationType + snapshotTimestamp + fileName`, Current Configuration File은 `equipmentId + configurationType + fileName`을 사용한다. 모든 identity의 `fileName` 구성요소는 MVP에서 case-insensitive 비교한다.
_Avoid_: FTP host/path를 파일 identity로 취급

**Resource Kind (`resourceKind`)**:
`fileId`가 어떤 feature의 logical identity를 담는지 내부적으로 구분하는 서명된 값이다. 현재 값은 `Log | ConfigurationCurrent | ConfigurationSnapshot`이다. 클라이언트는 이 값을 해석하거나 의존하지 않는다.
_Avoid_: resourceKind를 외부 API 분기 파라미터로 사용

**File ID (`fileId`)**:
`Logical File Identity`를 가리키는 유효기간이 있는 opaque 참조다. 일반 조회조건 자체를 나타내는 토큰이 아니며 물리 서버/경로를 직접 식별하지 않는다. 내부에는 logical identity와 `resourceKind`가 서명된 형태로 보존된다. Current Configuration File의 경우 같은 논리 identity의 현재 내용을 가리키며 특정 바이트 버전을 고정하지 않는다.
_Avoid_: query token, physical path identifier

**Continuation Token (`continuationToken`)**:
목록 조회의 다음 페이지를 가리키는 유효기간이 있는 opaque stateless cursor다. 서버에 이전 결과 전체를 저장하지 않고 원래 결과 집합을 결정한 조회조건과 마지막 반환 위치를 보존한다. Hourly/Daily Log는 `timestamp + fileName`, Continuous Log는 `fileName`, Configuration History는 `snapshotTimestamp + fileName`을 cursor로 사용한다. cursor의 `fileName` 비교는 case-insensitive다. `limit`은 페이지 크기이므로 결과 집합 조건에 포함하지 않는다.
_Avoid_: offset/page 번호, 서버 세션에 전체 결과를 저장하는 pagination token

**Token Codec**:
`fileId`와 `continuationToken`의 서명/검증, opaque encoding/decoding, TTL 처리를 담당하는 공통 기계적 계약이다. ASP.NET Core DataProtection으로 payload JSON을 purpose protector로 보호한 뒤 Base64Url로 인코딩한다. payload에는 `exp`(`IssuedAt+Ttl`)를 포함하고 decode 시 만료/변조/형식 오류를 `Expired`/`Invalid`로 구분한다. token 종류마다 독립된 purpose 문자열을 사용하며 다음 값을 고정한다.

- Logs fileId: `fg.fileid.log`
- Logs cursor: `fg.page.log`
- ConfigurationCurrent fileId: `fg.fileid.cfgcurrent`
- ConfigurationSnapshot fileId: `fg.fileid.cfgsnapshot`
- History cursor: `fg.page.cfghistory`

key ring은 DataProtection가 관리하며 자동 rotation을 사용한다. 기본 수명은 90일로 `fileId` TTL 24시간 이상이어야 하고, IIS 배포에서는 재시작 내구성을 위해 key ring을 파일 시스템에 persist한다. Log/Configuration identity나 pagination 의미는 해석하지 않는다.
_Avoid_: 공통 token 계층에 Log/Configuration 업무 규칙을 넣는 것

**Reference Data Snapshot**:
Stored Procedure에서 읽어 전체 검증을 통과한 하나의 일관된 기준정보 집합이다. refresh 시 새 snapshot 전체 검증 성공 후 cache를 atomic 교체하며, 검증 실패 시 일부만 적용하지 않는다.
_Avoid_: 검증되지 않은 일부 정의만 기존 cache와 혼합

**Subtype (`subtype`)**:
하나의 `logType` 내부에서 API 사용자가 자주 조회하는 대표 하위 분류 하나다. 같은 의미의 값을 `attributes`에 중복 저장하지 않는다. MVP Configuration 모델에는 사용하지 않는다.
_Avoid_: 임의의 모든 메타데이터를 subtype으로 승격, PM1/PM2 같은 Configuration 개별 파일을 subtype으로 모델링

**Attributes (`attributes`)**:
`subtype` 외에 로그 파일에서 추출하거나 기준정보로 부여하는 가변 key-value 메타데이터다. MVP Configuration 모델에는 적용하지 않는다.
_Avoid_: subtype과 같은 의미의 값을 중복 저장, Configuration에 요구사항 없이 attributes를 추가하는 것
