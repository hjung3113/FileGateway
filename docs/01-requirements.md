# 요구사항

## 1. 목적

FileGateway는 **분산 파일 서버에 이미 저장된 설비 파일**을 클라이언트에 제공한다.

MVP 제공 대상은 **설비 로그와 Configuration File**이다. `Configuration File`은 설비가 실제 동작에 사용하는 파라미터 설정 파일이며 로그가 아니다. 향후 리포트 등 다른 파일 종류도 같은 기반 파일 접근 기능을 사용할 수 있어야 한다.

설비에 직접 접속해 로그를 수집하거나 가공하는 기능은 별도 시스템의 책임이며 FileGateway 범위에 포함하지 않는다.

## 2. 주요 소비자

- 사용자용 WPF 또는 향후 Web Application의 Backend
- 설비에 직접 접근하지 않고 파일을 가져가야 하는 다른 서버/시스템
- .NET 및 Python 계열 클라이언트

브라우저가 FileGateway API Key를 직접 보유하는 구조는 권장하지 않는다. 향후 Web Application은 Backend/BFF를 통해 FileGateway를 호출한다.

## 3. 기본 조회 모델

- 클라이언트는 **`equipmentId` + 논리 조회 조건**을 사용한다.
- `equipmentId`는 표시명과 구분되는 안정적인 논리 설비 식별자이며 하나의 FileGateway 배포 범위 안에서 유일하다.
- 클라이언트는 `equipmentId`로 해당 설비에서 FileGateway를 통해 제공 가능한 파일 종류(`logType`, `configurationType`)를 먼저 조회할 수 있다.
- 실제 파일 서버명/IP, FTP 경로, DB 구조는 알 필요가 없다.
- FileGateway는 MSSQL 기준정보를 사용해 실제 서버와 탐색 규칙을 해석한다.
- 분산 파일 서버들의 MVP 접근 방식과 기본 root 구조는 동일하다.
- 클라이언트는 raw 물리 경로를 전달할 수 없으며, 모든 실제 파일 접근은 기준정보의 서버 `rootPath` 경계 안에서만 허용한다.
- MVP 파일명 비교는 Windows/IIS FTP 환경에 맞춰 case-insensitive로 수행하고 원래 casing은 응답에 보존한다.

## 4. MVP 기능

### 제공 파일 종류 조회

- `equipmentId` 기준으로 해당 설비에서 제공 가능한 파일 종류 조회
- Log는 DB 기준정보에 등록된 `logType`과 `generationType` 제공
- Configuration은 DB 기준정보에 등록된 `configurationType` 제공
- 파일 종류 조회는 실제 FTP 파일/폴더 존재 여부를 스캔하지 않고 **검증 완료된 기준정보**만 사용
- 설비사/설비 종류에 따라 제공 파일이 다를 수 있으며, 차이는 `equipmentId`별 기준정보로 표현
- 기존 Log/Configuration 계약 안에서 새 종류가 DB에 추가되면 코드 분기 추가 없이 조회 결과에 반영

### 로그

- 로그 목록 조회
- 파일 존재 여부 확인
- 파일 크기/기본 메타데이터 조회
- 파일 스트리밍 다운로드
- `fileId` 기반 접근
- 조건 기반 직접 조회/다운로드 (매치 2건 이상이면 zip 스트리밍)
- `subtype` 및 동적 `attributes` 조회/필터
- `limit + continuationToken` 페이지네이션

### Configuration File

- 현재 설정파일 집합 조회/다운로드
- 파일 서버에 보관된 히스토리 설정파일 조회/다운로드
- `equipmentId + configurationType` 기반 논리 조회
- 같은 `configurationType` 아래 PM1/PM2/PM3/PM4처럼 여러 현재 파일 존재 가능
- 개별 Configuration File을 별도 `subtype`이나 `configurationType`으로 세분화하지 않음
- 실제 물리 경로는 외부 계약의 식별자로 사용하지 않음
- Current와 History를 명시적으로 구분해 조회
- History 생산자가 생성한 완료 marker 파일이 존재하는 Snapshot Set만 조회 대상으로 사용

## 5. 로그 생성 유형 (`generationType`)

`generationType`은 로그의 업무 종류(`logType`)와 별개의 축이며 파일의 생성 주기/생명주기를 나타낸다.

### Hourly

- 특정 시각 또는 시간 범위 조회
- 대표 EventLog는 주로 시간당 한 파일
- 일부 로그는 같은 시간대에 여러 파일 생성 가능

### Daily

- 특정 일자 또는 일자 범위 조회

### Continuous

- 시간 범위를 사용하지 않고 현재 파일을 조회
- `from` 또는 `to`가 들어오면 `InvalidRequest`
- Hourly/Daily의 최근 24시간 기본값을 적용하지 않음
- 다운로드 시작 시점의 파일 크기까지만 전송

## 6. Configuration File

- 설비가 실제 동작에 사용하는 파라미터 값들이 저장된 설정 파일이다.
- 로그가 아니므로 `logType` 또는 `generationType`의 한 종류로 취급하지 않는다.
- `configurationType`으로 업무 의미를 구분하며 실제 파일명과 분리한다.
- 특정 `equipmentId + configurationType`은 현재 사용 중인 Configuration File **집합**을 식별하며 파일이 여러 개일 수 있다.
- Current 조회는 전체 현재 파일 집합을 case-insensitive `fileName ASC` 배열로 반환하고 pagination하지 않는다.
- Current File의 논리 identity는 `equipmentId + configurationType + fileName`이며 `fileName`은 case-insensitive 비교한다.
- Current 직접 다운로드에서 0개 일치는 `FileNotFound`, 1개는 다운로드, 여러 개는 `MultipleFilesMatched`로 처리한다.
- 별도 시스템이 자정에 Current 파일 집합을 날짜 폴더로 복사해 Configuration Snapshot Set을 생성하며 Current 원본은 그대로 유지한다.
- 같은 Snapshot Set의 파일들은 동일한 `snapshotTimestamp`를 공유하고, History API는 개별 Snapshot File 목록을 반환한다.
- History 생산자는 복사 완료 시 marker 파일을 생성한다. FileGateway는 marker 존재 여부만 확인하고 내용은 해석하지 않는다.
- marker가 없는 부분 Snapshot Set은 노출하지 않는다.
- FileGateway는 Current/History 파일을 **읽기 전용으로 제공**하며 히스토리 생성·복사·보관 책임을 갖지 않는다.

## 7. 시간 조회 규칙

- 로그의 `timestamp`는 파일명/경로 규칙에서 추출한 논리 시각이다.
- Configuration Snapshot의 시간은 snapshot 생성 논리 시각이며 파일명/경로 규칙에서 추출한다.
- timezone 없는 논리 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.
- API에서는 UTC offset이 포함된 ISO-8601 값으로 표현한다.
- Hourly/Daily의 `from`/`to`는 `[from, to)`로 해석해 `from`은 포함하고 `to`는 제외한다.
- Hourly/Daily에서 `from`/`to`가 모두 없으면 최근 24시간을 조회한다.
- Hourly/Daily에서 `from`만 있으면 `[from, from + 2일)`을 조회한다.
- Hourly/Daily에서 `to`만 있는 형태는 지원하지 않고 `InvalidRequest`로 처리한다.
- Hourly/Daily에서 `from`/`to`가 모두 있으면 지정한 `[from, to)`를 조회한다.
- `from >= to`는 `InvalidRequest`다.
- 로그 시간 조회에는 설정 가능한 `Logs.MaxQueryRange`를 두며 최대 기간 초과 요청은 `InvalidRequest`다. `from` 단독 요청이 2일 범위를 의미하므로 이 설정은 최소 2일 이상이어야 한다.
- Continuous 로그는 `from`/`to`를 허용하지 않는다.
- Current Configuration은 시간 필터 대상이 아니다.
- Configuration History는 `from`과 `to`를 모두 필수로 요구한다.
- Configuration History에는 로그와 독립적인 `Configurations.HistoryMaxQueryRange`를 두고 초과 요청은 `InvalidRequest`로 처리한다.
- 단일 파일을 요구하는 직접 다운로드 조건이 여러 파일과 일치하면 임의 선택하지 않고 충돌 오류 반환

## 8. fileId 의미

- 로그 `fileId`는 `equipmentId + logType + timestamp + fileName`의 논리 파일을 가리킨다.
- Configuration Snapshot File의 `fileId`는 `equipmentId + configurationType + snapshotTimestamp + fileName`의 논리 파일을 가리킨다.
- Current Configuration File의 `fileId`는 `equipmentId + configurationType + fileName`의 현재 논리 파일을 가리킨다.
- 위 logical identity의 `fileName` 구성요소는 MVP에서 case-insensitive 비교한다.
- Current Configuration File은 목록 조회 후 내용이 변경될 수 있으며 같은 `fileId`로 이후 다운로드하면 다운로드 시점의 현재 내용을 제공한다.
- Current File의 파일명이 대소문자만 바뀐 것은 같은 논리 파일로 취급한다. 그 외 이름 변경은 다른 논리 파일이다.
- 특정 과거 버전이 필요하면 Configuration Snapshot File의 `fileId`를 사용한다.

## 9. 기술/운영 MVP 결정

- 서버: ASP.NET Core/.NET
- 운영 OS: Windows Server
- 호스팅: IIS
- 외부 통신: HTTPS
- 인증: `X-Api-Key` HTTP header
- API Key query string 전달 금지
- API Key 누락/오류: `401 InvalidApiKey`
- 파일 서버 접근: FTP/FTPS 가능 구조, 실제 IIS FTP SSL 설정은 배포 전 확인
- 기준정보: MSSQL Stored Procedure
- 기준정보 캐시: 프로세스 메모리
- 새 기준정보는 필수 result set과 Equipment/Server 전역 식별자 검증 후, 유효한 Log/Configuration 정의만 담아 atomic cache 교체
- 개별 Log/Configuration 정의 validation 실패는 해당 정의만 제외하고 나머지 정상 정의는 새 snapshot에서 제공
- DB/SP 조회, result set 또는 Equipment/Server 전역 식별자 검증 실패 시 last-known-good cache가 있으면 전체를 계속 사용
- 최초 로딩부터 전역 검증을 통과한 usable 기준정보가 없으면 `ReferenceDataUnavailable`
- 주요 파일 크기: 대부분 100MB 이하 기준
- 규모: 파일 서버 수십~수백 대, 동시 다운로드 수십 건 수준 고려

## 10. MVP 제외

- 설비 직접 접근/로그 수집/가공
- Configuration History 생성/복사/보관
- Current Configuration 또는 Hourly/Daily 로그의 생산 방식 제어
- 생산 중 파일의 원자적 교체, 잠금, 내용 일관성 보장
- FileGateway 자체 snapshot 복사/버전 고정
- Linux 실제 배포/검증
- SMB/SFTP Adapter 구현
- Site별 다중 credential 관리
- Range/Resume 다운로드
- Configuration 직접 다운로드의 여러 파일 자동 ZIP 묶음
- 세밀한 API Key별 설비/로그 권한
- Web UI/WPF 클라이언트 자체 구현
- 고가용성/분산 캐시 구성
