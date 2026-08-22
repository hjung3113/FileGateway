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
로그 파일의 생성 주기 또는 생명주기를 나타내는 분류다. 현재 값은 `Hourly | Daily | Continuous`다.
_Avoid_: `logType`, 로그 종류

**Configuration File**:
설비가 실제 동작에 사용하는 파라미터 값들이 저장된 설정 파일이다. 로그가 아니며 현재 설정파일과 파일 서버에 보관되는 히스토리 설정파일 모두 FileGateway의 MVP 제공 대상이다.
_Avoid_: Configuration 로그, Configuration 계열 로그

**Configuration Type (`configurationType`)**:
설비 설정파일을 업무 의미에 따라 구분하는 안정적인 논리 분류다. 실제 파일명과 분리하며 파일명 규칙 변경이 외부 계약 변경으로 이어지지 않게 한다.
_Avoid_: 파일명을 configuration type으로 직접 사용

**Current Configuration**:
특정 `equipmentId + configurationType`에 대해 설비가 현재 사용하는 설정파일을 가리키는 논리 슬롯이다. 내용은 시간에 따라 바뀔 수 있으며 FileGateway가 버전을 고정하거나 히스토리를 생성하지 않는다.
_Avoid_: 현재 파일을 불변 snapshot으로 간주하는 표현

**Configuration Snapshot**:
별도 시스템이 파일 서버에 저장한 과거 Configuration File의 시점별 히스토리 파일이다. FileGateway는 생성·보관 책임 없이 이미 저장된 snapshot을 조회·다운로드만 한다.
_Avoid_: FileGateway가 생성하는 configuration backup

**Configuration Snapshot Timestamp**:
Configuration Snapshot이 생성된 논리 시각이다. 파일명/경로 규칙에서 추출하며 FTP modified time과 동일시하지 않는다.
_Avoid_: FTP modified time

**Logical Timestamp (`timestamp`)**:
파일명/경로의 메타데이터 규칙에서 추출한, 해당 로그 파일이 논리적으로 나타내는 시각이다. FTP 파일의 수정 시각이나 서버 파일시스템 시각을 의미하지 않는다. 현재 Site의 운영 시간대인 `Asia/Seoul`로 해석하고 API에서는 UTC offset이 포함된 ISO-8601 값으로 표현한다.
_Avoid_: modified time, FTP modification time을 `timestamp`와 동일시하는 표현

**Time Range (`from`, `to`)**:
시간 기반 조회에 적용하는 반개구간 `[from, to)`이다. `from`은 포함하고 `to`는 제외한다.
_Avoid_: `to` 포함 여부가 문맥에 따라 달라지는 표현

**File ID (`fileId`)**:
하나의 논리 파일 참조를 가리키는 유효기간이 있는 opaque 참조다. 로그와 Configuration Snapshot에서는 특정 파일을 가리키고, Current Configuration에서는 특정 `equipmentId + configurationType`의 현재 파일 슬롯을 가리킨다. 일반 조회조건 자체를 나타내는 토큰이 아니며 물리 서버/경로를 직접 식별하지 않는다.
_Avoid_: query token, physical path identifier

**Subtype (`subtype`)**:
하나의 `logType` 내부에서 API 사용자가 자주 조회하는 대표 하위 분류 하나다. 같은 의미의 값을 `attributes`에 중복 저장하지 않는다.
_Avoid_: 임의의 모든 메타데이터를 subtype으로 승격

**Attributes (`attributes`)**:
`subtype` 외에 로그 파일에서 추출하거나 기준정보로 부여하는 가변 key-value 메타데이터다. Configuration File에도 동일 개념을 적용할지는 별도 설계 결정으로 확정한다.
_Avoid_: subtype과 같은 의미의 값을 중복 저장
