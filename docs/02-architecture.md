# 전체 아키텍처

## 목표

FileGateway는 클라이언트와 실제 분산 파일 서버 사이의 추상화 계층이다.

```text
.NET / Python(FastAPI) Client
            |
         HTTP(S)
            |
      FileGateway API
            |
   +-------------------+
   | Feature Providers |
   | - Log Provider    |
   | - Future Provider |
   +-------------------+
            |
   +-------------------+
   | Server Access Core|
   +-------------------+
       |           |
    MSSQL SP    File Servers
```

## 역할 분리

### FileGateway API

- 외부 HTTP/HTTPS 인터페이스 제공
- 요청 검증
- 인증/인가 적용 지점
- Provider 호출
- JSON 메타데이터 및 파일 스트림 응답

### Feature Provider

업무 의미를 처리한다.

초기에는 `Log Provider`를 제공하며 향후 다른 파일 Provider를 추가한다.

Provider는 다음을 결정한다.

- 어떤 기준정보를 조회할지
- 어떤 파일을 사용자에게 노출할지
- 날짜/시간 필터를 어떻게 적용할지
- 논리적 파일 정보를 어떻게 구성할지

### Server Access Core

공통 파일 접근 기능만 담당한다.

- 서버 대상 해석
- 기준정보 조회 추상화
- 파일 목록 읽기
- 파일 존재/크기 확인
- 파일 스트리밍
- 공통 접근 오류 처리

`Server Access Core`에는 로그 전용 규칙을 넣지 않는다.

### Infrastructure

- MSSQL Stored Procedure 호출
- Windows 파일 서버 접근
- 네트워크/파일 시스템 I/O

## 핵심 설계 원칙

1. 클라이언트는 실제 서버 구조를 모른다.
2. 물리 파일 경로를 외부 API에 노출하지 않는다.
3. 파일 경로 규칙을 코드에 하드코딩하지 않는다.
4. 로그 도메인 로직과 파일 접근 기능을 분리한다.
5. 파일 다운로드는 대용량을 고려하여 스트리밍한다.
6. 서버 재배치나 경로 변경이 클라이언트 인터페이스 변경으로 이어지지 않게 한다.

## 권장 솔루션 논리 구조

```text
FileGateway
├─ FileGateway.Api
├─ FileGateway.Core
├─ FileGateway.Infrastructure
└─ FileGateway.LogProvider
```

실제 서버 구현 기술 스택이 확정될 때 프로젝트 구조를 최종 확정한다.
