using FileGateway.Core.Files;
using FileGateway.Core.Queries;
using FileGateway.Core.Tokens;

namespace FileGateway.Configurations;

public interface IConfigurationQueryService
{
    Task<IReadOnlyList<ConfigurationItem>> GetCurrentAsync(
        string equipmentId, string configurationType, CancellationToken ct);

    Task<SingleFileMatch> ResolveCurrentSingleAsync(
        string equipmentId, string configurationType, CancellationToken ct);

    Task<PagedResult<ConfigurationHistoryItem>> GetHistoryAsync(
        ConfigurationHistoryQuery q, CancellationToken ct);

    Task<LocatedFile> LocateByFileIdAsync(TokenPayload payload, CancellationToken ct);
}
