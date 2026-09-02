using FileGateway.Core.Errors;
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.Api.ReferenceData;

public sealed class ReferenceDataWarmupService(
    IReferenceDataView referenceData,
    ILogger<ReferenceDataWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await referenceData.GetSnapshotAsync(cancellationToken);
        }
        catch (FileGatewayException ex) when (ex.Code == "ReferenceDataUnavailable")
        {
            logger.LogWarning(ex,
                "reference data startup warm-up failed; process remains live and readiness will retry");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
