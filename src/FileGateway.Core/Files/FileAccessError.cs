namespace FileGateway.Core.Files;

public enum FileAccessError
{
    ConnectionFailed,
    AuthenticationFailed,
    Timeout,
    ProtocolError,
    FileNotFound,
    IoFailure,
}
