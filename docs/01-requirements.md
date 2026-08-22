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
- 실제 파일 서버명/IP, FTP 경로, DB 구조는 알 필요가 없다.
- FileGateway는 MSSQL 기준정보를 사용해 실제 서버와 탐색 규칙을 해석한다.
- 분산 파일 서버들의 MVP 접근 방식과 기본 root 구조는 동일하다.

## 4. MVP 기능

### 로그

- 로그 목록 조회
- 파일 존재 여부 확인
- 파일 크기/기본 메타데이터 조회
- 파일 스트리밍 다운로드
- `fileId` 기반 접근
- 조건 기반 직접 조회/다운로드
- `subtype` 및 동적 `attributes` 조회/필터
- `limit + continuationToken` 페이지네이션

### Configuration File

- 현재 설정파일 조회/다운로드
- 파일 서버에 보관된 히스토리 설정파일 조회/다운로드
- `equipmentId + configurationType` 기반 논리 조회
- 실제 파일명/물리 경로는 외부 계약의 식별자로 사용하지 않음

Configuration Provider와 외부 API의 구체적 경계는 설계 인터뷰에서 별도 확정한다.

## 5. 로그 생성 유형 (`generationType`)

`generationType`은 로그의 업무 종류(`logType`)와 별개의 축이며 파일의 생성 주기/생명주기를 나타낸다.

### Hourly

- 특정 시각 또는 시간 범위 조회
- 대표 EventLog는 주로 시간당 한 파일
- 일부 로그는 같은 시간대에 여러 파일 생성 가능

### Daily

- 특정 일자 또는 일자 범위 조회

### Continuous

- 날짜/시간 필터와 무관하게 현재 파일을 목록에 포함
- 다운로드 시작 시점의 파일 크기까지만 전송

## 6. Configuration File

- 설비가 실제 동작에 사용하는 파라미터 값들이 저장된 설정 파일이다.
- 로그가 아니므로 `logType` 또는 `generationType`의 한 종류로 취급하지 않는다.
- `configurationType`으로 업무 의미를 구분하며 실제 파일명과 분리한다.
- 현재 설정파일과 파일 서버에 보관된 히스토리 설정파일 모두 제공 대상이다.
- 한 설비에 서로 다른 역할의 설정파일 여러 개가 동시에 존재할 수 있다.

## 7. 시간 조회 규칙

- 로그의 `timestamp`는 파일명/경로 규칙에서 추출한 논리 시각이다.
- timezone 없는 논리 시각은 현재 Site 운영 시간대 `Asia/Seoul`로 해석한다.
- API에서는 UTC offset이 포함된 ISO-8601 값으로 표현한다.
- `from`/`to`는 `[from, to)`로 해석해 `from`은 포함하고 `to`는 제외한다.
- 시간 조건이 없으면 로그는 최근 24시간을 기본 조회 범위로 사용한다.
- Continuous 로그는 위 시간 범위와 별도로 현재 파일을 포함한다.
- Configuration 히스토리의 기본 시간 범위는 해당 Provider/API 경계와 함께 별도 확정한다.
- 단일 파일을 요구하는 직접 다운로드 조건이 여러 파일과 일치하면 임의 선택하지 않고 충돌 오류 반환

## 8. 기술/운영 MVP 결정

- 서버: ASP.NET Core/.NET
- 운영 OS: Windows Server
- 호스팅: IIS
- 외부 통신: HTTPS
- 인증: API Key
- 파일 서버 접근: FTP/FTPS 가능 구조, 실제 IIS FTP SSL 설정은 배포 전 확인
- 기준정보: MSSQL Stored Procedure
- 기준정보 캐시: 프로세스 메모리
- 주요 파일 크기: 대부분 100MB 이하 기준
- 규모: 파일 서버 수십~수백 대, 동시 다운로드 수십 건 수준 고려

## 9. MVP 제외

- 설비 직접 접근/로그 수집/가공
- Linux 실제 배포/검증
- SMB/SFTP Adapter 구현
- Site별 다중 credential 관리
- Range/Resume 다운로드
- 여러 파일 자동 ZIP 묶음
- 세밀한 API Key별 설비/로그 권한
- Web UI/WPF 클라이언트 자체 구현
- 고가용성/분산 캐시 구성
