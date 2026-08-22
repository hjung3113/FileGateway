# 확장성 및 주요 리스크

## 확장 원칙

현재 필요한 경계만 추상화하고 후속 기능을 미리 구현하지 않는다.

- `IFileAccess`는 유지하되 MVP Adapter는 FTP/FTPS 하나
- MVP의 로그와 Configuration은 각각 별도 Feature Provider로 유지
- 향후 다른 파일 종류가 실제로 필요해질 때 동일한 Core 파일 접근 기반 위에 별도 Feature Provider 추가 여부를 판단
- Core/Logs/Configurations는 Windows 전용 API에 직접 종속되지 않게 유지

## 확정된 MVP 밖 확장

### Linux 배포

MVP는 Windows Server + IIS다. Linux 실제 배포/검증은 후속이다. MVP 파일명 비교는 Windows/IIS FTP에 맞춰 case-insensitive이므로 case-sensitive 저장소 도입 시 identity/정렬/cursor 규칙을 재검토한다.

### 다른 Site / credential

현재 Site는 모든 파일 서버가 동일 credential과 기본 접근 방식을 사용한다. 다른 Site 확장 시 `credentialProfileId` 또는 Site별 연결 정책 필요 여부를 재평가한다.

### 다른 파일 프로토콜

필요 시 SMB/SFTP Adapter를 `IFileAccess` 구현으로 추가한다. 현재는 구현하지 않는다.

### 권한 세분화

MVP는 여러 API Key/`callerId`를 동시에 사용할 수 있지만 모든 활성 key의 권한 범위는 동일하다. 향후 CallerId를 기준으로 Site/설비/파일 종류 범위를 제한할 수 있다.

### Range/Resume

주요 파일이 대부분 100MB 이하이므로 MVP에서는 제외한다.

### 다중 파일 다운로드

여러 결과를 자동 ZIP하는 기능은 제외한다. 필요 시 별도 요구사항으로 설계한다.

### 다중 discovery rule

현재 동일 `equipmentId + logType` 조회에서 서로 다른 디렉터리/파일명 규칙을 동시에 검색해야 하는 사례가 없다. MVP는 로그 정의당 하나의 `discoveryRule`만 사용하며 실제 요구가 생기기 전 `discoveryRules[]` 구조를 만들지 않는다.

하나의 discovery rule 안에서는 **여러 논리 시간 슬롯이 같은 물리 디렉터리를 사용할 수 있다.** 시간 슬롯과 물리 디렉터리를 1:1로 고정하지 않는다.

## 현재 리스크

### FTP/FTPS 보안 상태 미확인

IIS에서 21번 포트를 사용한다는 사실만으로 FTPS 여부를 판단할 수 없다. 배포 전 `FTP SSL Settings`와 인증서 설정을 확인한다. 일반 FTP이면 내부망에서도 credential/내용이 평문일 수 있다.

### FTP Passive 데이터 포트

21번은 제어 연결 포트다. 목록/다운로드를 위해 Passive 데이터 포트 범위와 방화벽/NAT 정책이 필요할 수 있으므로 실제 서버 환경에서 검증한다.

### API Key 권한 범위

호출자 구분과 회전을 위해 여러 key를 동시에 활성화하지만 MVP에서는 각 key의 권한이 전체 설비/제공 파일에 미친다. 하나의 key 유출 영향이 크므로 key 회전과 감사 로그를 필수 운영 항목으로 둔다.

### Token 보호 key 운영

`fileId`는 24시간 유효하므로 token 보호 key가 IIS 재시작 때 소실되거나 회전 시 즉시 폐기되면 이미 발급한 token 계약을 깨뜨린다. 보호 key는 프로세스 재시작에 내구적인 방식으로 공급/보관하고 이전 key를 최대 token TTL 동안 검증 가능하게 유지한다.

### 분산 서버 장애 / 부분 결과

한 파일 서버 장애를 전체 FileGateway 장애로 간주하지 않는다. 다만 **하나의 요청이 필요로 하는 원격 디렉터리들 중 일부만 FTP I/O 실패한 경우에는 부분 결과를 정상 성공으로 반환하지 않는다.** 해당 요청 전체를 오류로 처리한다. 정상적인 디렉터리 부재는 결과 0개로 구분한다.

### FTP 동시성

파일 서버 수와 동시 요청이 늘면 한 서버 또는 전체 Gateway가 FTP 연결/명령으로 포화될 수 있다. FileGateway 전체 동시 FTP 작업 한도와 서버별 한도를 운영 설정으로 두고 실제 환경 측정으로 수치를 확정한다.

### 기준정보/파일 불일치

DB에는 정의가 있지만 실제 경로/파일이 없을 수 있다. 기준정보 refresh에서는 FTP 실재 여부를 검증하지 않고 실제 요청에서 판단하므로 운영 진단을 위해 원인을 구분한다.

### stale 기준정보의 장기 사용

기준정보 refresh가 실패해도 last-known-good cache가 있으면 availability를 위해 계속 사용한다. MVP에는 별도 max-stale 차단 시간이 없으므로 DB 장애가 장기화되면 삭제/변경된 정의가 예상보다 오래 사용될 수 있다. stale 사용 여부와 마지막 정상 갱신 시각을 반드시 관측하고 운영 절차로 관리한다.

### 생산 중 파일 일관성

FileGateway는 읽기 전용 제공 계층이므로 Current Configuration 및 Hourly/Daily 로그의 생산 방식, 원자적 replace, 쓰기 중 읽기 일관성을 보장하지 않는다. 생산 중 파일이 변경되어 길이 불일치/I/O 실패가 발생하면 일반 streaming failure로 드러날 수 있다. 이 문제를 해결하기 위한 snapshot 복사/잠금/버전 고정은 MVP 범위 밖이다.

Configuration History는 생산자가 완료 marker를 제공하고, FileGateway는 marker가 존재하는 Snapshot Set만 읽는 것으로 부분 복사 노출을 방지한다. Snapshot `fileId` 재접근 시에도 marker 존재 여부를 다시 확인한다.

### 목록 변경 중 페이지네이션

FTP 파일 목록은 조회 중 변할 수 있다. continuation token과 안정된 정렬 기준을 사용하지만 완전한 snapshot을 보장하지 않는다.

Continuation token은 발급 당시 결과 집합을 결정한 조회조건에 종속한다. 페이지 이동 중 `equipmentId`, 타입, 시간 범위, subtype/attributes 등 결과 집합 조건 변경은 허용하지 않는다. `limit`은 페이지 크기이므로 변경할 수 있다.

조회 중 원격 파일이 추가/삭제되면 후속 페이지 결과가 달라질 수 있다. 완전한 snapshot 보장이 실제로 필요해질 때 별도 설계를 검토한다.

## 명시적 범위 제외

설비에 직접 접속해 로그를 수집/가공하는 시스템과 Configuration History를 생성하는 시스템은 FileGateway와 별도 프로젝트/서비스로 유지한다.
