# DB 및 기준정보 연계

## DB

- Microsoft SQL Server
- FileGateway는 Stored Procedure로 기준정보를 조회한다.
- DB 테이블 구조는 애플리케이션/API에 노출하지 않는다.

## SP가 제공해야 하는 논리 정보

### 서버/설비 매핑

- equipmentId
- logType
- serverId
- host 또는 서버 연결에 필요한 비민감 식별정보
- rootPath

`equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자이며 하나의 FileGateway 배포 범위 안에서 유일하다.

### 로그 탐색 규칙

- generationType: Hourly / Daily / Continuous
- pathTemplate
- filePattern
- cardinality

`logType`은 업무적인 로그 종류이고 `generationType`은 파일 생성 주기/생명주기다. 두 값을 같은 분류로 취급하지 않는다.

### 메타데이터 추출 규칙

- mode: Template / Regex
- pattern
- 추출 group/token → timestamp/subtype/attribute 매핑

`timestamp`는 파일명/경로 규칙에서 추출한 로그의 논리 시각이다. FTP modified time과 구분한다. Timezone 정보가 없으면 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.

FTP 비밀번호 등 credential은 SP에서 반환하지 않는다.

## MVP 전제

- 분산 서버의 기본 FTP root 구조 동일
- 동일 credential 사용
- 로그 종류별 실제 탐색/파일명 규칙은 서로 다를 수 있음
- DB/SP는 확장 가능하며 탐색/파싱 규칙을 기준정보로 관리

`Configuration File`은 로그가 아니며 MVP 제공 대상이다. `configurationType`을 실제 파일명과 분리된 업무 분류로 사용한다. Configuration 기준정보의 구체적 구조는 해당 Provider/API 경계가 확정된 뒤 정의한다.

## 호출자 확장 포인트

MVP API Key는 전체 설비/로그에 접근 가능하다. 향후 권한 분리가 필요해지면:

```text
API Key -> CallerId
CallerId + EquipmentId + LogType -> SP/Policy filter
```

형태로 확장할 수 있다. raw API Key를 Stored Procedure에 전달하지 않는다.

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
- 로그 정의 존재 여부
- 중복/충돌 매핑
- 잘못된 root/path template
- 지원하지 않는 generation/metadata mode
- 유효하지 않은 regex/template/mapping

기준정보 오류와 실제 파일 서버 장애는 별도 원인으로 유지한다.
