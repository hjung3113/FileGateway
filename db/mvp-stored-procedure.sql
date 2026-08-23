-- db/mvp-stored-procedure.sql
CREATE OR ALTER PROCEDURE dbo.FileGateway_GetReferenceData AS
BEGIN
    SET NOCOUNT ON;
    SELECT EquipmentId FROM dbo.FgEquipment;
    SELECT ServerId, Host, RootPath FROM dbo.FgServer;
    SELECT EquipmentId, LogType, ServerId, GenerationType, PathTemplate, FilePattern,
           Cardinality, MetadataMode, MetadataPattern, MetadataMappings
    FROM dbo.FgLogDefinition;
    SELECT EquipmentId, ConfigurationType, ServerId, CurrentPathTemplate, CurrentFilePattern,
           HistoryPathTemplate, HistoryFilePattern, HistoryMarkerPathTemplate
    FROM dbo.FgConfigurationDefinition;
END
