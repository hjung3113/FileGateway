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

## MVP 구현

Infrastructure에서 단일 FTP/FTPS Adapter가 `IFileAccess`를 구현한다.

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

## 스트리밍

- 파일 전체를 메모리에 적재하지 않는다.
- 클라이언트 요청 취소/연결 종료를 원격 파일 스트림 취소에 전달한다.
- 계속 갱신되는 파일은 Logs/Download 계층에서 다운로드 시작 시점 크기를 확정하고 그 크기까지만 읽는다.

## 오류 구분

최소한 다음 원인을 구분한다.

- 파일 서버 연결 실패
- 인증 실패
- 원격 경로 없음
- 파일 없음
- 명령/프로토콜 오류
- 스트리밍 중 I/O 오류
- timeout/cancel

외부 HTTP 오류 매핑은 API/운영 문서에서 정의한다.

## 향후 확장

다른 Site에서 접근 방식이 달라지는 경우 `IFileAccess` 구현으로 SMB/SFTP 등을 추가할 수 있다. MVP에서는 미리 구현하지 않는다.
