namespace FileGateway.Core.Files;

public sealed record RemoteDirectoryListing(bool Exists, IReadOnlyList<RemoteFileEntry> Files)
{
    public static RemoteDirectoryListing Missing { get; } = new(false, []);
}
