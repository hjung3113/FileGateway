-- db/mvp-schema.sql (테스트/개발용 계약 구현)
CREATE TABLE dbo.FgEquipment (EquipmentId nvarchar(64) NOT NULL PRIMARY KEY);
CREATE TABLE dbo.FgServer (ServerId nvarchar(64) NOT NULL PRIMARY KEY,
    Host nvarchar(255) NOT NULL, RootPath nvarchar(512) NOT NULL);
CREATE TABLE dbo.FgLogDefinition (
    EquipmentId nvarchar(64) NOT NULL, LogType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL, GenerationType nvarchar(16) NOT NULL,
    PathTemplate nvarchar(512) NOT NULL, FilePattern nvarchar(256) NOT NULL,
    Cardinality nvarchar(16) NOT NULL, MetadataMode nvarchar(16) NOT NULL,
    MetadataPattern nvarchar(1024) NOT NULL, MetadataMappings nvarchar(max) NOT NULL DEFAULT '[]',
    CONSTRAINT PK_FgLogDefinition PRIMARY KEY (EquipmentId, LogType));
CREATE TABLE dbo.FgConfigurationDefinition (
    EquipmentId nvarchar(64) NOT NULL, ConfigurationType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL,
    CurrentPathTemplate nvarchar(512) NOT NULL, CurrentFilePattern nvarchar(256) NOT NULL,
    HistoryPathTemplate nvarchar(512) NOT NULL, HistoryFilePattern nvarchar(256) NOT NULL,
    HistoryMarkerPathTemplate nvarchar(512) NOT NULL,
    CONSTRAINT PK_FgConfigurationDefinition PRIMARY KEY (EquipmentId, ConfigurationType));
