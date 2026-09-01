// tests/FileGateway.IntegrationTests/ReferenceData/SpReaderTests.cs
using FileGateway.Infrastructure.ReferenceData;

namespace FileGateway.IntegrationTests.ReferenceData;

public class SpReaderTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Reader_maps_reordered_columns_by_name()
    {
        const string variant = "dbo.FileGateway_GetReferenceData_ReorderedColumns";
        await db.ExecuteAsync($@"CREATE OR ALTER PROCEDURE {variant} AS
            BEGIN
                SET NOCOUNT ON;
                SELECT 'EQ-REORDERED' AS EquipmentId;
                SELECT 'root-reordered' AS FileRootPath, 'SRV-REORDERED' AS ServerId,
                       'host-reordered' AS Host;
                SELECT 'logs/reordered' AS DirectoryTemplate, 'Multiple' AS SlotCardinality,
                       'EQ-REORDERED' AS EquipmentId, '[]' AS MetadataGroupMappings,
                       'Hourly' AS GenerationType, 'Template' AS MetadataParseMode,
                       'Logs/{{yyyy}}/Event.zip' AS RelativePathMetadataPattern,
                       'Event_*.zip' AS FileNamePattern, 'EventLog' AS LogType,
                       'SRV-REORDERED' AS ServerId;
                SELECT 'History' AS HistoryTimestampParseMode,
                       'PM/history/{{yyyy}}{{MM}}{{dd}}' AS HistoryDirectoryTemplate,
                       'SRV-REORDERED' AS ServerId,
                       'PM_*.cfg' AS CurrentFileNamePattern,
                       'EQ-REORDERED' AS EquipmentId,
                       'PM/history/{{yyyy}}{{MM}}{{dd}}/DONE' AS HistoryCompletionMarkerPathTemplate,
                       'Glob' AS HistoryFileNameMatchMode,
                       'PM' AS ConfigurationType,
                       'PM/current' AS CurrentDirectoryTemplate,
                       'yyyyMMdd' AS HistoryFileNameTimestampPattern,
                       'Glob' AS CurrentFileNameMatchMode,
                       'PM_*.cfg' AS HistoryFileNamePattern,
                       '[]' AS HistoryTimestampMappings;
            END");

        var raw = await new SpReferenceDataSource(db.ConnectionString, variant)
            .ReadAsync(CancellationToken.None);

        var server = Assert.Single(raw.Servers);
        Assert.Equal("SRV-REORDERED", server.ServerId);
        Assert.Equal("host-reordered", server.Host);
        Assert.Equal("root-reordered", server.FileRootPath);

        var log = Assert.Single(raw.LogDefinitions);
        Assert.Equal("EQ-REORDERED", log.EquipmentId);
        Assert.Equal("EventLog", log.LogType);
        Assert.Equal("SRV-REORDERED", log.ServerId);
        Assert.Equal("Hourly", log.GenerationType);
        Assert.Equal("logs/reordered", log.DirectoryTemplate);
        Assert.Equal("Event_*.zip", log.FileNamePattern);
        Assert.Equal("Multiple", log.SlotCardinality);
        Assert.Equal("Template", log.MetadataParseMode);
        Assert.Equal("Logs/{yyyy}/Event.zip", log.RelativePathMetadataPattern);

        var configuration = Assert.Single(raw.ConfigurationDefinitions);
        Assert.Equal("EQ-REORDERED", configuration.EquipmentId);
        Assert.Equal("PM", configuration.ConfigurationType);
        Assert.Equal("SRV-REORDERED", configuration.ServerId);
        Assert.Equal("PM/current", configuration.CurrentDirectoryTemplate);
        Assert.Equal("PM_*.cfg", configuration.CurrentFileNamePattern);
        Assert.Equal("Glob", configuration.CurrentFileNameMatchMode);
        Assert.Equal("PM/history/{yyyy}{MM}{dd}", configuration.HistoryDirectoryTemplate);
        Assert.Equal("PM_*.cfg", configuration.HistoryFileNamePattern);
        Assert.Equal("Glob", configuration.HistoryFileNameMatchMode);
        Assert.Equal("PM/history/{yyyy}{MM}{dd}/DONE", configuration.HistoryCompletionMarkerPathTemplate);
        Assert.Equal("History", configuration.HistoryTimestampParseMode);
        Assert.Equal("yyyyMMdd", configuration.HistoryFileNameTimestampPattern);
        Assert.Equal("[]", configuration.HistoryTimestampMappings);
    }

    [Fact]
    public async Task Reader_rejects_configuration_result_set_with_missing_required_column()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_MissingColumn",
                "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
                "CurrentFileNamePattern, CurrentFileNameMatchMode, HistoryDirectoryTemplate, " +
                "HistoryFileNamePattern, HistoryFileNameMatchMode, HistoryCompletionMarkerPathTemplate, " +
                "HistoryTimestampParseMode, HistoryFileNameTimestampPattern FROM dbo.FgConfigurationDefinition;"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
    }

    [Fact]
    public async Task Reader_rejects_configuration_result_set_with_misspelled_column()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_MisspelledColumn",
                "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
                "CurrentFileNamePattern, CurrentFileNameMatchMode, HistoryDirectoryTemplate, " +
                "HistoryFileNamePattern, HistoryFileNameMatchMode, HistoryCompletionMarkerPathTemplate, " +
                "HistoryTimestampParseMode, HistoryFileNameTimestampPattern, " +
                "HistoryTimestampMappings AS HistoryTimestampMappingz FROM dbo.FgConfigurationDefinition;"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
    }

    [Fact]
    public async Task Reader_rejects_same_count_configuration_result_set_with_unexpected_column()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_UnexpectedColumn",
                "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
                "CurrentFileNamePattern, CurrentFileNameMatchMode, HistoryDirectoryTemplate, " +
                "HistoryFileNamePattern, HistoryFileNameMatchMode, HistoryCompletionMarkerPathTemplate, " +
                "HistoryTimestampParseMode, HistoryFileNameTimestampPattern, " +
                "HistoryTimestampMappings AS UnexpectedColumn FROM dbo.FgConfigurationDefinition;"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
    }

    [Fact]
    public async Task Reader_rejects_duplicate_configuration_column_names()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_DuplicateColumn",
                "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
                "CurrentFileNamePattern, CurrentFileNameMatchMode, HistoryDirectoryTemplate, " +
                "HistoryFileNamePattern, HistoryFileNameMatchMode, HistoryCompletionMarkerPathTemplate, " +
                "HistoryTimestampParseMode, HistoryFileNameTimestampPattern, " +
                "HistoryTimestampMappings AS HistoryFileNameTimestampPattern " +
                "FROM dbo.FgConfigurationDefinition;"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
    }

    [Fact]
    public async Task Reader_maps_four_result_sets()
    {
        await db.ExecuteAsync(@"INSERT dbo.FgEquipment VALUES('EQ-001');
            INSERT dbo.FgServer VALUES('SRV1','ftp1.internal','ftproot');
            INSERT dbo.FgLogDefinition VALUES('EQ-001','EventLog','SRV1','Hourly',
              'Logs/{yyyy}/{MM}/{dd}/{HH}','*.zip','Multiple','Template',
              'Logs/{yyyy}/{MM}/{dd}/{HH}/Event_{subtype}.zip','[]');
            INSERT dbo.FgConfigurationDefinition
              (EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, CurrentFileNamePattern,
               HistoryDirectoryTemplate, HistoryFileNamePattern, HistoryCompletionMarkerPathTemplate)
              VALUES('EQ-001','PM','SRV1',
              'PM/current','PM_*.cfg','PM/history/{yyyy}/{MM}/{dd}','PM_*.cfg',
              'PM/history/{yyyy}/{MM}/{dd}/_DONE');");

        var raw = await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None);

        Assert.Contains("EQ-001", raw.EquipmentIds);
        var log = Assert.Single(raw.LogDefinitions);
        Assert.Equal("Hourly", log.GenerationType);
        var cfg = Assert.Single(raw.ConfigurationDefinitions);
        Assert.Equal("PM/history/{yyyy}/{MM}/{dd}/_DONE", cfg.HistoryCompletionMarkerPathTemplate);
    }

    [Fact]
    public async Task Snapshot_from_sp_passes_validation_without_ftp()
        => Assert.NotNull(ReferenceDataSnapshotBuilder.Build(
               await new SpReferenceDataSource(db.ConnectionString).ReadAsync(CancellationToken.None)));

    [Fact]
    public async Task Reader_rejects_SP_missing_trailing_result_set()
    {
        // 변형 SP: 3개 result set만 반환(4번째 FgConfigurationDefinition 누락).
        // NextResultAsync 반환값을 무시하면 누락 set이 빈 catalog로 해석돼 LKG를 교체할 수 있다.
        const string variant = "dbo.FileGateway_GetReferenceData_Truncated";
        await db.ExecuteAsync(@"CREATE OR ALTER PROCEDURE dbo.FileGateway_GetReferenceData_Truncated AS
            BEGIN
                SET NOCOUNT ON;
                SELECT EquipmentId FROM dbo.FgEquipment;
                SELECT ServerId, Host, FileRootPath FROM dbo.FgServer;
                SELECT EquipmentId, LogType, ServerId, GenerationType, DirectoryTemplate, FileNamePattern,
                       SlotCardinality, MetadataParseMode, RelativePathMetadataPattern, MetadataGroupMappings
                FROM dbo.FgLogDefinition;
            END");

        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => new SpReferenceDataSource(db.ConnectionString, variant).ReadAsync(CancellationToken.None));
        Assert.Equal("ReferenceDataIncomplete", ex.Code);
    }

    [Fact]
    public async Task Reader_rejects_zero_row_configuration_result_set_with_legacy_eight_columns()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_ZeroRowsEight",
                "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
                "CurrentFileNamePattern, HistoryDirectoryTemplate, HistoryFileNamePattern, " +
                "HistoryCompletionMarkerPathTemplate " +
                "FROM dbo.FgConfigurationDefinition;"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
        Assert.Contains("expected 13", ex.Message);
    }

    [Fact]
    public async Task Reader_accepts_zero_row_configuration_result_set_with_thirteen_columns()
    {
        var raw = await ReadConfigurationShapeAsync(
            "dbo.FileGateway_GetReferenceData_ZeroRowsThirteen",
            "SELECT TOP (0) EquipmentId, ConfigurationType, ServerId, CurrentDirectoryTemplate, " +
            "CurrentFileNamePattern, CurrentFileNameMatchMode, HistoryDirectoryTemplate, " +
            "HistoryFileNamePattern, HistoryFileNameMatchMode, HistoryCompletionMarkerPathTemplate, " +
            "HistoryTimestampParseMode, HistoryFileNameTimestampPattern, HistoryTimestampMappings " +
            "FROM dbo.FgConfigurationDefinition;");

        Assert.Empty(raw.ConfigurationDefinitions);
    }

    [Fact]
    public async Task Reader_rejects_rows_with_legacy_eight_column_configuration_result_set()
    {
        var ex = await Assert.ThrowsAsync<FileGateway.Core.Errors.FileGatewayException>(
            () => ReadConfigurationShapeAsync(
                "dbo.FileGateway_GetReferenceData_RowsEight",
                "SELECT 'EQ-001', 'PM', 'SRV1', 'PM/current', 'PM*.cfg', " +
                "'PM/history/{yyyy}/{MM}/{dd}', 'PM*.cfg', 'PM/history/{yyyy}/{MM}/{dd}/_DONE';"));

        Assert.Equal("ReferenceDataIncomplete", ex.Code);
        Assert.Contains("actual 8", ex.Message);
    }

    private async Task<ReferenceDataRaw> ReadConfigurationShapeAsync(string procedureName, string configurationSelect)
    {
        await db.ExecuteAsync($@"CREATE OR ALTER PROCEDURE {procedureName} AS
            BEGIN
                SET NOCOUNT ON;
                SELECT TOP (0) EquipmentId FROM dbo.FgEquipment;
                SELECT TOP (0) ServerId, Host, FileRootPath FROM dbo.FgServer;
                SELECT TOP (0) EquipmentId, LogType, ServerId, GenerationType, DirectoryTemplate, FileNamePattern,
                       SlotCardinality, MetadataParseMode, RelativePathMetadataPattern, MetadataGroupMappings
                FROM dbo.FgLogDefinition;
                {configurationSelect}
            END");

        return await new SpReferenceDataSource(db.ConnectionString, procedureName)
            .ReadAsync(CancellationToken.None);
    }
}
