// tests/FileGateway.IntegrationTests/ReferenceData/SpReaderTests.cs
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.IntegrationTests.ReferenceData;

public class SpReaderTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Reader_maps_four_result_sets()
    {
        await db.ExecuteAsync(@"INSERT dbo.FgEquipment VALUES('EQ-001');
            INSERT dbo.FgServer VALUES('SRV1','ftp1.internal','ftproot');
            INSERT dbo.FgLogDefinition VALUES('EQ-001','EventLog','SRV1','Hourly',
              'Logs/{yyyy}/{MM}/{dd}/{HH}','*.zip','Multiple','Template',
              'Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip','[]');
            INSERT dbo.FgConfigurationDefinition VALUES('EQ-001','PM','SRV1',
              'PM/current','PM_*.cfg','PM/history/{yyyy}/{MM}/{dd}','PM_*.cfg',
              'PM/history/{yyyy}/{MM}/{dd}/_DONE');");

        var raw = await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None);

        Assert.Contains("EQ-001", raw.EquipmentIds);
        var log = Assert.Single(raw.LogDefinitions);
        Assert.Equal("Hourly", log.GenerationType);
        var cfg = Assert.Single(raw.ConfigurationDefinitions);
        Assert.Equal("PM/history/{yyyy}/{MM}/{dd}/_DONE", cfg.HistoryMarkerPathTemplate);
    }

    [Fact]
    public async Task Snapshot_from_sp_passes_validation_without_ftp()
        => Assert.NotNull(ReferenceDataSnapshotBuilder.Build(
               await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None)));
}
