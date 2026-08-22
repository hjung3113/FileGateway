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
- 논리 `fileId` 발급/해석

FileGateway가 담당하지 않는다.

- 설비에서 설정값 수집
- Current Configuration 변경
- Configuration Snapshot 생성
- snapshot 복사/보관/삭제 정책

히스토리 파일은 별도 시스템이 파일 서버에 저장한 결과를 FileGateway가 읽기 전용으로 제공한다.

## 도메인 관계

특정 `equipmentId + configurationType`에는 하나의 Current Configuration 논리 슬롯이 있다.

```text
Equipment + Configuration Type
├─ Current Configuration
└─ Configuration Snapshot History
   ├─ Snapshot @ T1
   ├─ Snapshot @ T2
   └─ Snapshot @ T3
```

Current와 History는 서로 다른 `configurationType`이 아니라 같은 Configuration의 현재 상태와 과거 snapshot 관계다.

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

- `currentRule`: 현재 설정파일 하나의 위치를 해석하는 규칙
- `historyRule`: 히스토리 디렉터리/파일 패턴과 snapshot 시각 추출 규칙

Current와 History는 의미가 다르므로 하나의 범용 discovery rule로 합치지 않는다.

## Current Configuration

- 시간 필터와 무관하게 현재 파일을 조회한다.
- Current용 `fileId`는 특정 물리 파일 버전을 고정하지 않고 `equipmentId + configurationType`의 현재 파일 슬롯을 가리킨다.
- 목록/정보 조회 후 파일 내용이 변경될 수 있다.
- 같은 Current `fileId`로 이후 다운로드하면 다운로드 시점의 현재 내용을 제공한다.
- 과거 특정 버전이 필요하면 History snapshot의 `fileId`를 사용한다.
- `subtype`/`attributes`는 MVP Configuration 모델에 두지 않는다.

## Configuration Snapshot History

- 별도 시스템이 생성해 파일 서버에 저장한 과거 설정파일이다.
- 생성이 완료된 snapshot은 불변이다. 기존 snapshot을 수정하지 않고 새 snapshot을 생성한다.
- snapshot의 논리 시각은 파일명/경로 규칙에서 추출한다.
- FTP modified time을 snapshot 시각으로 사용하지 않는다.
- timezone 없는 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.
- 시간 범위는 `[from, to)` 규칙을 사용한다.
- History 조회에서는 `from`과 `to`를 모두 필수로 요구한다. 전체 히스토리 또는 임의 기본 기간을 암묵적으로 조회하지 않는다.
- snapshot용 `fileId`는 특정 논리 snapshot 파일 하나를 가리킨다.
- `subtype`/`attributes`는 MVP Configuration 모델에 두지 않는다.

## 조회 원칙

- Current와 History는 API에서 명시적으로 구분한다.
- Current를 History 결과에 암묵적으로 포함하지 않는다.
- Current는 `equipmentId + configurationType`으로 하나의 논리 슬롯을 결정한다.
- History는 `equipmentId + configurationType + [from, to)`로 snapshot 집합을 조회한다.
- History 목록은 `limit + continuationToken` 페이지네이션을 사용한다.
- History 기본 정렬은 `snapshotTimestamp DESC`, 동일 시각에서는 `fileName ASC`다.
- 페이지네이션은 원격 파일 집합의 완전한 snapshot을 보장하지 않는다. 조회 중 파일이 추가/삭제되면 후속 페이지 결과가 달라질 수 있다.
