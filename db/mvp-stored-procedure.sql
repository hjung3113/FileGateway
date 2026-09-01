-- db/mvp-stored-procedure.sql
CREATE OR ALTER PROCEDURE dbo.FileGateway_GetReferenceData AS
BEGIN
    SET NOCOUNT ON;
    SELECT EquipmentId FROM dbo.FgEquipment;
    SELECT ServerId, Host, FileRootPath FROM dbo.FgServer;
    SELECT EquipmentId, LogType, ServerId, GenerationType, DirectoryTemplate, FileNamePattern,
           SlotCardinality, MetadataParseMode, RelativePathMetadataPattern, MetadataGroupMappings
    FROM dbo.FgLogDefinition;
    SELECT EquipmentId, ConfigurationType, ServerId,
           CurrentDirectoryTemplate, CurrentFileNamePattern,
           ISNULL(CurrentFileNameMatchMode, '') AS CurrentFileNameMatchMode,
           HistoryDirectoryTemplate, HistoryFileNamePattern,
           ISNULL(HistoryFileNameMatchMode, '') AS HistoryFileNameMatchMode,
           HistoryCompletionMarkerPathTemplate,
           ISNULL(HistoryTimestampParseMode, '') AS HistoryTimestampParseMode,
           ISNULL(HistoryFileNameTimestampPattern, '') AS HistoryFileNameTimestampPattern,
           ISNULL(HistoryTimestampMappings, '') AS HistoryTimestampMappings
    FROM dbo.FgConfigurationDefinition;
END
