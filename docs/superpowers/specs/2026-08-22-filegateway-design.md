# FileGateway Design Spec

Date: 2026-08-22
Status: Approved
Process: Superpowers brainstorming (Architectural)

## 1. 목적과 경계

FileGateway는 분산 파일 서버에 이미 저장된 설비 로그를 WPF/Web Backend/다른 서버 등의 클라이언트에 제공한다.

설비 직접 접근, 로그 수집, 로그 가공은 별도 시스템 책임이며 FileGateway에 포함하지 않는다.

## 2. MVP 결정 요약

- ASP.NET Core/.NET
- Windows Server + IIS
- HTTPS + API Key
- MSSQL Stored Procedure 기준정보
- FTP/FTPS Adapter 1개
- 모든 MVP 파일 서버 동일 접근 방식/root/credential
- 목록 + 존재/metadata + streaming download
- 조건 기반 직접 다운로드
- `fileId`: signed opaque, 24h
- 기본 조회: 최근 24시간
- pagination: `limit + continuationToken`
- Continuous: 다운로드 시작 시점 크기까지만 전송
- 주요 파일 크기: 대부분 100MB 이하
- 규모: 파일 서버 수십~수백, 동시 다운로드 수십 건 고려

## 3. 아키텍처

```text
Clients
  |
 HTTPS
  v
FileGateway.Api
  |
FileGateway.Logs
  |
  +-- LogResolver
  +-- fileId / pagination
  |
FileGateway.Core contracts
  ^                    ^
  |                    |
MSSQL + cache      FTP/FTPS Adapter
       \              /
       FileGateway.Infrastructure
```

프로젝트:

- FileGateway.Api
- FileGateway.Core
- FileGateway.Logs
- FileGateway.Infrastructure

Core는 Log/FTP/MSSQL/IIS를 알지 않는다.

## 4. 로그/Resolver 모델

`EquipmentLogDefinition`

- equipment, logType, serverId
- generationType
- discoveryRule
- metadataRule

`DiscoveryRule`

- pathTemplate
- filePattern
- cardinality

`MetadataRule`

- Template 또는 Regex
- 추출 값 → timestamp/subtype/attributes 매핑

`FileDescriptor`

- fileId
- equipment/logType/subtype
- timestamp
- fileName/size/isContinuous
- attributes

Resolver는 항상 파일 집합을 반환한다. EventLog처럼 시간당 한 파일인 경우도 동일하다.

## 5. 로그 유형

- Hourly: 시간/범위 조회, 같은 시간 여러 파일 허용
- Daily: 일자/범위 조회
- Continuous: 시간 필터 없이 현재 파일 포함
- Configuration: subtype/attributes를 동적으로 추출/필터

파일명/디렉터리 규칙은 로그별로 다르므로 코드 하드코딩 대신 MSSQL 기준정보의 Template/Regex 규칙으로 관리한다.

## 6. API

- `GET /api/v1/logs`
- `GET /api/v1/files/{fileId}`
- `HEAD /api/v1/files/{fileId}`
- `GET /api/v1/files/{fileId}/download`
- `GET /api/v1/logs/download?...`

직접 다운로드도 목록과 같은 Resolver를 사용한다. 2개 이상 일치하면 409 `MultipleFilesMatched`로 임의 선택하지 않는다.

## 7. 기준정보/캐시

SP는 설비/로그 → 서버/host/root와 탐색/metadata 규칙을 제공한다. credential은 제공하지 않는다.

Memory cache를 사용하며 TTL은 운영 설정으로 둔다. DB 장애 시 유효 cache가 있으면 계속 처리하고 없으면 503으로 실패한다.

향후 CallerId 기반 권한 필터를 SP/Policy에 추가할 수 있으나 MVP API Key는 전체 접근이다.

## 8. 보안/운영

- HTTPS
- API Key 원문 비로깅
- FTP credential 별도 Secret
- 물리 host/path 비노출
- signed fileId, 24h
- 감사로그: caller/IP/equipment/logType/file/fileSize/result/elapsed
- health live/ready
- timeout/cancel/concurrency limit 설정 가능

IIS FTP의 21번 사용만으로 FTPS 여부는 알 수 없으므로 배포 전 SSL 설정을 확인한다. Passive 데이터 포트 범위도 실제 환경 검증이 필요하다.

## 9. 테스트/배포

Unit: Resolver/Template/Regex/filter/token/pagination.

Integration: MSSQL/cache/FTP/stream/error.

API: auth/list/stat/download/direct/error/cancel.

MVP 완료는 Windows Server + IIS에서 실제 MSSQL/FTP 연동까지 검증하는 것을 포함한다.

## 10. MVP 제외/후속

- 설비 직접 수집/가공
- Linux 실제 배포
- SMB/SFTP
- 다중 Site/credential
- API Key별 세밀 권한
- Range/Resume
- 다중 파일 자동 ZIP
- Web UI/WPF 구현
- 분산 cache/HA

역할별 최신 설계는 `docs/INDEX.md`에서 안내하는 문서를 기준으로 한다.
