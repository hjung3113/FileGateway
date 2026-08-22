namespace FileGateway.Core.Files;

public sealed class FileAccessException(FileAccessError Error, string Message, Exception? Inner = null)
    : Exception(Message, Inner)
{
    public FileAccessError Error { get; } = Error;
}
