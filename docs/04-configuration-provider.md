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
- History 생산자가 제공한 완료 marker 존재 여부를 확인해 완료된 Snapshot Set만 조회 대상으로 삼기

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
- `historyRule`: 날짜별 히스토리 디렉터리/파일 패턴, `snapshotTimestamp`, Snapshot Set 완료 marker 이름/위치를 해석하는 규칙

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

## 파일명 비교 규칙

MVP Windows/IIS FTP 환경에서는 Configuration `fileName` 비교를 case-insensitive로 수행하고 실제 파일명의 원래 casing은 응답에 그대로 보존한다.

이 규칙은 다음에 동일하게 적용한다.

- Current/Snapshot 후보 파일명 matching
- Current logical identity의 `fileName`
- Snapshot logical identity의 `fileName`
- Current `fileName ASC` 정렬
- History 동일 timestamp 내 `fileName ASC` 정렬과 cursor 비교

대소문자만 다른 파일명은 같은 논리 파일명으로 취급한다. 향후 case-sensitive 저장소를 지원할 때 재검토한다.

## Current Configuration

- 시간 필터와 무관하게 현재 파일 집합을 조회한다.
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

### Current 직접 다운로드

```http
GET /api/v1/configurations/current/download?equipmentId=...&configurationType=...
```

Current 조회와 같은 Resolver 규칙을 사용한다.

- 0개 일치: `FileNotFound`
- 1개 일치: 해당 Current Configuration File 다운로드
- 2개 이상 일치: 임의 선택하지 않고 `MultipleFilesMatched`(409)

여러 Current 파일이 있는 Configuration Type은 Current 목록에서 원하는 파일의 `fileId`를 받은 뒤 공통 `/api/v1/files/{fileId}/download`를 사용한다. 직접 다운로드 endpoint에 `fileName`/`subtype` 같은 추가 선택 축을 만들지 않는다.

## Configuration Snapshot History

- 별도 시스템이 자정에 날짜 폴더를 만들고 해당 시점의 Current Configuration Set을 그대로 복사한다.
- snapshot 생성 후에도 Current 원본 파일은 그대로 유지된다.
- 한 날짜/시점의 복사 결과는 하나의 `Configuration Snapshot Set`이며 그 안에 PM1, PM2, PM3, PM4처럼 여러 파일이 존재할 수 있다.
- 같은 Snapshot Set의 모든 파일은 동일한 `snapshotTimestamp`를 공유한다.
- 현재 운영 계획에서 `snapshotTimestamp`는 해당 날짜의 Site local `00:00`이며 날짜 폴더/경로 규칙에서 해석한다.
- History 생산자는 Snapshot Set 복사 완료 시 marker 파일을 생성한다.
- marker 파일명/위치는 `historyRule` 기준정보로 설정한다.
- FileGateway는 marker 파일의 **존재 여부만 확인**하고 내용은 읽거나 해석하지 않는다.
- marker가 존재하는 Snapshot Set만 조회 대상으로 삼고 복사 중인 부분 Snapshot Set은 노출하지 않는다.
- 완료 marker 자체는 Configuration File 결과에 포함하지 않는다.
- 생성이 완료된 Snapshot File은 불변이다. 기존 파일을 수정하지 않고 다음 snapshot에서 새 파일로 반영한다.
- FTP modified time을 snapshot 시각으로 사용하지 않는다.
- timezone 없는 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.
- 시간 범위는 `[from, to)` 규칙을 사용한다.
- History 조회에서는 `from`과 `to`를 모두 필수로 요구한다. 전체 히스토리 또는 임의 기본 기간을 암묵적으로 조회하지 않는다.
- History 조회에는 설정 가능한 `Configurations.HistoryMaxQueryRange`를 적용하고 초과 요청은 `InvalidRequest`로 처리한다.
- History API는 Snapshot Set을 별도 중첩 객체로 만들지 않고 개별 Snapshot File을 반환하며, 같은 시점의 파일들은 동일한 `snapshotTimestamp`로 구분할 수 있다.
- Snapshot File용 `fileId`는 `equipmentId + configurationType + snapshotTimestamp + fileName`의 논리 identity로 특정 파일 하나를 가리키며 `fileName`은 case-insensitive 비교한다.
- `subtype`/`attributes`는 MVP Configuration 모델에 두지 않는다.

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
- History 결과 item은 `fileId`, `fileName`, `equipmentId`, `configurationType`, `snapshotTimestamp`, `size`를 가진다.
- History 목록은 pagination envelope의 `items`로 결과를 반환하며 결과가 없으면 `items=[]`, `continuationToken=null`이다.
- History 목록은 `limit + continuationToken` 페이지네이션을 사용한다.
- `limit`의 기본값/최댓값은 운영 설정으로 두며 최대값을 넘는 요청은 `InvalidRequest`다.
- `Configurations.HistoryMaxQueryRange`를 초과하는 조회는 `InvalidRequest`다.
- History 기본 정렬은 `snapshotTimestamp DESC`, 동일 시각에서는 case-insensitive `fileName ASC`다.
- 페이지네이션은 원격 파일 집합의 완전한 snapshot을 보장하지 않는다. 조회 중 파일이 추가/삭제되면 후속 페이지 결과가 달라질 수 있다.
- Configuration History 전용 조건 기반 직접 다운로드 endpoint는 두지 않는다. History 목록에서 `fileId`를 받은 뒤 공통 `/files/{fileId}/download`를 사용한다.
