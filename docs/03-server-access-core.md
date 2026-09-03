# Server Access Core

## 역할

`FileGateway.Core`의 파일 접근 영역은 로그에 한정되지 않는 **프로토콜 비종속 파일 I/O 계약**을 제공한다.

## 핵심 계약

`IFileAccess`는 다음 구체 시그니처를 제공한다.

```csharp
interface IFileAccess
{
    Task<RemoteDirectoryListing> ListFilesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct);
    Task<RemoteDirectoryNames> ListDirectoriesAsync(FileServerConnection server, string relativeDirectory, CancellationToken ct);
    Task<long> StatFileAsync(FileServerConnection server, string relativePath, CancellationToken ct);
    Task<bool> FileExistsAsync(FileServerConnection server, string relativePath, CancellationToken ct);
    Task<RemoteOpenRead> OpenReadAsync(FileServerConnection server, string relativePath, CancellationToken ct);
```

`ListDirectoriesAsync`는 직계 자식 디렉터리 이름을 `RemoteDirectoryNames(Exists, Names)`로 반환한다. 목록 대상 디렉터리 부재는 `Exists=false`(예외 아님), 존재하지만 비어 있으면 `Exists=true, Names=[]`로 구분하고, 전송/인증/프로토콜 장애는 다른 메서드와 동일하게 예외로 전달한다. 반환 이름에는 `.`·`..`가 포함되지 않는다. `StatFileAsync`는 파일 부재 시 `FileAccessException(FileAccessError.FileNotFound)`를 던지고, `FileExistsAsync`는 파일 부재 시 `false`를 반환한다. 전송 오류는 두 메서드 모두 예외로 전달한다. 모든 메서드는 `CancellationToken`으로 요청 취소를 전달한다.

파일 크기는 조회한 시점의 관측값이다. Continuous 로그나 Current Configuration처럼 변경 가능한 파일은 이후 크기가 달라질 수 있다.

## MVP 구현

Infrastructure에서 `IFileAccess`를 두 구현(`FtpFileAccess`, `LocalFileAccess`)과 그 앞의 라우팅 composite(`RoutingFileAccess`)로 제공한다. 상위 계층(Logs/Configurations/Api)은 `IFileAccess` 하나만 의존하며 이 분기를 모른다.

MVP FTP/FTPS Adapter는 **FluentFTP를 사용**하는 방향으로 구현한다.

```text
FileGateway.Core.IFileAccess
          ↑
FileGateway.Infrastructure
  RoutingFileAccess (composite)
    ├ Host == "localhost" → LocalFileAccess → System.IO
    └ 그 외             → FluentFTP 기반 FTP/FTPS Adapter → FluentFTP
```

`RoutingFileAccess`는 상태 없는 singleton이고 호출마다 `server.Host`로 위임 대상을 고른다.

- 판정은 `Host?.Trim()`이 `"localhost"`와 정확히 일치(`OrdinalIgnoreCase`)하는 경우만 로컬이다. `127.0.0.1`, `::1`, 머신명, FQDN, trailing-dot `localhost.`, null/빈 값은 모두 FTP 경로로 간다.
- 라우팅 조건은 이 composite에만 존재한다. 서버 추가/변경에 별도 설정이 필요 없고, 새 Provider 추상화나 factory 계약을 도입하지 않는다.
- DI는 `LocalFileAccess`/`FtpFileAccess`를 구체형으로 등록하고 `IFileAccess`는 factory로 composite를 만들어 등록한다(선언 타입 기준 해석의 자기참조를 피하기 위함).

`LocalFileAccess`는 `System.IO`로 직접 읽으며 별도 옵션/설정, 동시성 pool이 없다(`FtpClientPool`은 FTP 연결 자원 보호용이라 로컬에는 적용하지 않는다).

- **`RootPath` 해석**: localhost 서버의 `RootPath`는 기준정보(SP)가 내려주는 값을 **로컬 파일시스템 절대 경로로 그대로 사용**한다(예: `C:\FileGateway\files`). 변환 계층은 없다. 상대 경로가 들어오면 프로세스 CWD 기준으로 조용히 절대화하지 않고 `ProtocolError`로 fail-fast한다.
- **`RootPath` 제약**: 드라이브 루트(`C:\`, `/`) 자체를 `RootPath`로 지정하지 않는다. 루트 검증이 끝 구분자를 정규화하는 과정에서 정상 하위 경로까지 거부되는 fail-closed 부작용이 있다. 운영 기준정보는 드라이브 루트 하위의 전용 디렉터리를 사용한다.
- **경로 검증(삼중 방어)**: 상대 경로는 먼저 FTP 어댑터와 동일한 `RemotePath.Combine` 가드를 통과해야 한다(rooted 경로, `.`/`..` 세그먼트 거부). 이후 `Path.GetFullPath`로 정규화한 물리 경로가 정규화된 `RootPath` 하위인지 접두사 재검증한다. 마지막으로 root부터 대상까지 경로 구성요소 중 symlink/junction(reparse point)이 하나라도 있으면 거부한다 — `GetFullPath`는 링크 대상을 해석하지 않으므로 접두사 검사만으로는 root 밖 이탈을 막을 수 없다. 어느 쪽이든 위반은 `FileAccessError.ProtocolError`(FTP 가드 위반과 동일한 오류 코드)로 거부하며, 파일시스템 접근보다 먼저 수행된다. 빈/공백 상대 경로는 루트 자체로 해석한다(FTP와 동일).
- **에러 매핑**: FTP와 같은 `FileAccessException`/`FileAccessError` 체계를 공유한다. 대상 파일 부재는 `FileNotFound`, 목록 대상 디렉터리 부재는 예외가 아니라 `RemoteDirectoryListing.Missing`, 공유 위반/일반 IO 오류는 `IoFailure`, 그 외는 `ProtocolError`다. `ConnectionFailed`/`AuthenticationFailed`/`Timeout`은 로컬 경로에서 발생하지 않는다. 존재 여부를 `Exists`로 선판정하지 않고 실제 enumerate/메타데이터 조회/open 결과로 분류한다 — `Exists`는 접근 거부에서도 `false`를 반환해 권한 문제를 '없음'으로 오분류할 수 있기 때문이다.
- **권한 거부는 FTP와 의도적으로 다르다**: FTP 서버는 권한 거부를 550으로 돌려주어 파일 없음과 뭉개지지만, 로컬은 `UnauthorizedAccessException`을 구분할 수 있으므로 `IoFailure`로 매핑한다("경로는 맞는데 실행 계정에 권한 없음" 장애가 404로 표시되지 않게 한다 — "오류 구분" 원칙).
- **취소**: 목록 나열 루프에서도 취소 토큰을 관찰해 클라이언트 단절 시 대량 디렉터리 스캔을 즉시 중단한다. 취소(`OperationCanceledException`)는 오류 매핑 없이 그대로 전파한다.
- **스트리밍**: `FileShare.Read | Write | Delete`로 열어 생산자의 append/rotation과 병행 읽기를 허용한다. 반환 크기는 open된 스트림의 관측값이고, 읽기 중 IO 오류는 `IoFailure`다 — 아래 "스트리밍" 문단의 계약을 그대로 따른다.

FluentFTP는 구현 세부사항으로 `FileGateway.Infrastructure` 안에 격리한다.

- Core/Logs/Configurations에 FluentFTP 타입을 노출하지 않는다.
- FluentFTP 예외/응답 모델을 그대로 상위 계층에 전달하지 않고 `IFileAccess`의 공통 원격 I/O 의미로 변환한다.
- FTP 서버 wildcard 동작, 경로 표현 등 라이브러리/프로토콜별 차이가 도메인 규칙에 새지 않게 한다.
- 향후 다른 프로토콜 Adapter 도입 시 기존 feature 계층을 변경하지 않는 것을 목표로 한다.
- `FluentFTP` 버전은 `csproj`에 고정한다.

FTP/FTPS 옵션 계약은 `FtpOptions.Security` = `Plain | ExplicitTls | ImplicitTls`(기본 `Plain`)와 `FtpOptions.AcceptUntrustedCertificates`(기본 `false`)를 사용한다. 두 값은 `FtpConfig`의 `EncryptionMode`와 인증서 검증 정책에 반영한다.

`FtpClientPool`은 FileGateway 전체와 파일 서버별 permit을 함께 확보하고, 서버 `Host`를 case-insensitive 키로 연결된 idle `AsyncFtpClient`를 재사용한다. 단기 FTP 명령은 `RunAsync`가 permit과 client를 checkout해 성공 시 idle queue로 반납하고, 실패 시 해당 client를 폐기한다. checkout한 재사용 client에서 예외 체인에 `SocketException`·`IOException`·`TimeoutException`이 있으면 새 client로 한 번만 재연결·재시도하며, 신규 client의 실패는 재시도하지 않는다. checkout 시 이미 `IsConnected == false`인 idle client도 폐기하고 새로 연결한다.

`OpenReadAsync`가 반환하는 스트림은 client와 permit을 소유하므로 다운로드 동안 동시성 한도를 유지한다. 비동기 dispose에서만 (1) 선언 길이만큼 전달했거나 inner EOF를 관측했고, (2) `FtpSocketStream.CloseAsync`가 FTP 완료 응답을 성공적으로 검증했으며, (3) 검증 후 client가 연결 상태인 세 조건을 모두 만족할 때 client를 idle queue로 반납한다. 부분 읽기, 완료 응답 실패, 연결 단절, 동기 dispose는 보수적으로 client를 폐기한다. permit은 client 반납 또는 폐기 뒤에 해제하며 dispose 오류는 밖으로 전파하지 않는다.

운영 시 서버당 최대 `MaxConcurrentPerServer`개의 제어 연결이 idle 상태로 유지될 수 있다. 별도 keep-alive나 idle eviction timer는 두지 않는다.

연결 이후 FTP 명령 오류도 `ConnectAsync`와 동일한 `FileAccessException` 매핑으로 변환한다. 실제 FTPS 연동과 인증서 검증은 Task 21 수동 게이트에서 확인한다.

MVP 전제:

- 분산 서버들의 접근 방식 동일. 단, `Host == "localhost"` 서버는 FTP를 거치지 않고 동일 머신 파일시스템에서 직접 읽는다.
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

다른 Site에서 접근 방식이 달라지는 경우 `IFileAccess` 구현으로 SMB/SFTP 등을 추가할 수 있다. MVP에서는 미리 구현하지 않는다. 로컬 파일 접근은 이미 `LocalFileAccess`로 구현되어 있으므로 이 목록에 해당하지 않으며, `RoutingFileAccess`는 `localhost` 2갈래 분기일 뿐 일반화된 Provider 선택 계층이 아니다.
