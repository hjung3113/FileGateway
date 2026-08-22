# Server Access Core

## 역할

`FileGateway.Core`의 파일 접근 영역은 로그에 한정되지 않는 **프로토콜 비종속 파일 I/O 계약**을 제공한다.

## 핵심 계약

개념적으로 `IFileAccess`는 다음 기능을 제공한다.

- 목록 조회
- 존재 여부 확인
- 파일 크기/기본 메타데이터 조회
- 읽기 스트림 열기
- 요청 취소 전달

구체적인 메서드 이름/시그니처는 구현 계획에서 확정한다.

파일 크기는 조회한 시점의 관측값이다. Continuous 로그나 Current Configuration처럼 변경 가능한 파일은 이후 크기가 달라질 수 있다.

## MVP 구현

Infrastructure에서 단일 FTP/FTPS Adapter가 `IFileAccess`를 구현한다.

MVP FTP/FTPS Adapter는 **FluentFTP를 사용**하는 방향으로 구현한다.

```text
FileGateway.Core.IFileAccess
          ↑
FileGateway.Infrastructure
  FluentFTP 기반 FTP/FTPS Adapter
          ↓
       FluentFTP
```

FluentFTP는 구현 세부사항으로 `FileGateway.Infrastructure` 안에 격리한다.

- Core/Logs/Configurations에 FluentFTP 타입을 노출하지 않는다.
- FluentFTP 예외/응답 모델을 그대로 상위 계층에 전달하지 않고 `IFileAccess`의 공통 원격 I/O 의미로 변환한다.
- FTP 서버 wildcard 동작, 경로 표현 등 라이브러리/프로토콜별 차이가 도메인 규칙에 새지 않게 한다.
- 향후 다른 프로토콜 Adapter 도입 시 기존 feature 계층을 변경하지 않는 것을 목표로 한다.
- 구체 패키지 버전은 구현 시점의 .NET 지원 범위와 유지보수 상태를 확인해 고정한다.

MVP 전제:

- 분산 서버들의 접근 방식 동일
- 기본 FTP root 구조 동일
- 동일 credential 사용
- 서버별 주요 차이는 host와 기준정보에서 받은 논리 경로/탐색 규칙

FTP 계정정보는 DB 결과에 포함하지 않고 FileGateway Secret/설정에서 관리한다.

## 비책임

Core에는 다음을 넣지 않는다.

- 설비명 → 서버 매핑
- MSSQL SP 업무 규칙
- Event/Configuration 로그 구분
- 시간/일/Continuous 정책
- 파일명/경로 템플릿/정규식 해석
- subtype/attributes 필터
- HTTP/API Key 처리
- 파일 생산 측의 원자적 교체/쓰기 완료/내용 일관성 보장
- FluentFTP 등 특정 프로토콜 라이브러리 타입

## 원격 조회 의미

프로토콜 Adapter는 원격 상태를 업무 의미와 구분해 상위 계층이 판단할 수 있도록 한다.

- 계산된 디렉터리/경로가 존재하지 않는 상태와 파일 서버 연결/인증/프로토콜 장애를 구분한다.
- 목록 조회 대상 디렉터리가 없다는 사실 자체는 FileGateway 전체 장애가 아니다. Logs/Configurations Resolver가 해당 조회 슬롯의 결과 0개로 해석할 수 있어야 한다.
- 특정 파일을 직접 stat/open하는 시점의 파일 부재는 파일 없음으로 구분한다.

## 스트리밍

- 파일 전체를 메모리에 적재하지 않는다.
- 클라이언트 요청 취소/연결 종료를 원격 파일 스트림 취소에 전달한다.
- 다운로드 응답은 스트림 시작 직전 확인한 파일 크기를 전송 길이 기준으로 사용한다.
- Continuous 로그는 Logs/Download 계층에서 다운로드 시작 직전 파일 크기를 확정하고 그 크기를 해당 응답의 전송 상한으로 사용한다.
- Current Configuration도 다운로드 시작 직전의 현재 파일 크기를 기준으로 사용한다.
- 다운로드 중 원격 파일이 커져도 시작 시점 크기를 초과해 읽지 않는다.
- 다운로드 중 truncate/rotation 등으로 시작 시점 크기까지 읽을 수 없게 되면 정상 완료로 처리하지 않고 streaming I/O 실패로 분류한다.
- truncate된 파일 뒤에 새 파일을 이어 붙이거나 자동으로 새 스트림을 열어 재시도하지 않는다.
- FileGateway는 읽기 전용 제공 계층이며, 외부 생산자가 파일을 쓰는 동안의 바이트 일관성을 보정하기 위해 snapshot 복사/잠금/버전 고정을 수행하지 않는다.

## 오류 구분

최소한 다음 원인을 구분한다.

- 파일 서버 연결 실패
- 인증 실패
- 원격 경로 없음
- 파일 없음
- 명령/프로토콜 오류
- 스트리밍 중 I/O 오류
- timeout
- 클라이언트 취소/연결 종료

클라이언트 취소는 파일 서버/스트리밍 장애와 구분한다.

외부 HTTP 오류 매핑은 API/운영 문서에서 정의한다.

## 향후 확장

다른 Site에서 접근 방식이 달라지는 경우 `IFileAccess` 구현으로 SMB/SFTP 등을 추가할 수 있다. MVP에서는 미리 구현하지 않는다.
