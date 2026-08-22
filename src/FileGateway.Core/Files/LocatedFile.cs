namespace FileGateway.Core.Files;

public sealed record LocatedFile(FileServerConnection Server, string RelativePath, string FileName, long Size);
