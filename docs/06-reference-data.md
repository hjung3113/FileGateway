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

`logType`은 업무적인 로그 종류이고 `generationType`은 파일 생성 주기/생명주기다. 두 값을 같은 분류로 취급하지 않는다.

로그 `timestamp`는 파일명/경로 규칙에서 추출한 논리 시각이다. FTP modified time과 구분하며 timezone 정보가 없으면 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다. Daily 로그의 논리 `timestamp`는 해당 날짜의 Site local `00:00`이다.

`subtype` 및 동적 attribute 값은 외부 조회에서 정확한 문자열 일치(case-sensitive)를 사용한다. 대소문자 비구분이 필요한 업무 값은 기준정보/파싱 단계에서 canonical value로 정규화한다.

`cardinality=Single`은 탐색 결과가 최대 하나여야 한다는 정의상의 invariant다. 실제 결과가 2개 이상이면 정상적인 다중 결과가 아니라 기준정보/파일 상태 불일치로 취급한다.

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

- `currentRule`: Current Configuration 파일 하나의 논리 위치를 해석하는 규칙
- `historyRule`: History 디렉터리/파일 패턴 및 snapshot 논리 시각을 추출하는 규칙

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

Configuration Snapshot의 논리 시각은 파일명/경로 규칙에서 추출하고 FTP modified time과 구분한다. 생성 완료된 snapshot은 불변으로 취급한다.

MVP Configuration 정의에는 `subtype`/동적 `attributes` 규칙을 추가하지 않는다.

FTP 비밀번호 등 credential은 SP에서 반환하지 않는다.

## MVP 전제

- 분산 서버의 기본 FTP root 구조 동일
- 동일 credential 사용
- 로그 및 Configuration 종류별 실제 탐색/파일명 규칙은 서로 다를 수 있음
- DB/SP는 확장 가능하며 탐색/파싱 규칙을 기준정보로 관리
- FileGateway는 Configuration History를 생성/보관하지 않고 별도 시스템이 저장한 파일을 읽기 전용으로 제공

## 호출자 확장 포인트

MVP API Key는 전체 설비/제공 파일에 접근 가능하다. 향후 권한 분리가 필요해지면 CallerId와 논리 리소스 식별자를 기준으로 정책 필터를 추가할 수 있다.

raw API Key를 Stored Procedure에 전달하지 않는다.

## 캐시

- 프로세스 memory cache 사용
- TTL은 설정 가능(초기 권장 10~30분 범위)
- 기준정보 변경 빈도가 낮다는 현재 운영 특성을 전제로 함

DB 장애 시:

- 유효 캐시 존재 → 캐시로 계속 처리
- 캐시 없음 → `ReferenceDataUnavailable`

로컬 영속 fallback/분산 cache는 MVP에서 제외한다.

## 필수 검증

SP 결과 수신 시:

- 설비/서버 매핑 존재 여부
- 로그/Configuration 정의 존재 여부
- `equipmentId + logType` 중복 정의 여부
- 중복/충돌 매핑
- 잘못된 root/path template
- Current rule이 하나의 Current Configuration 슬롯을 결정하는지
- History rule이 유효한 snapshot 집합과 논리 시각을 해석할 수 있는지
- 지원하지 않는 generation/metadata mode
- 유효하지 않은 regex/template/mapping

실제 탐색 시 `cardinality=Single`인데 여러 파일이 발견되는 경우도 별도 invariant 위반으로 유지한다.

기준정보 오류와 실제 파일 서버 장애는 별도 원인으로 유지한다.
