-- db/mvp-schema.sql (테스트/개발용 계약 구현)
CREATE TABLE dbo.FgEquipment (EquipmentId nvarchar(64) NOT NULL PRIMARY KEY);
CREATE TABLE dbo.FgServer (ServerId nvarchar(64) NOT NULL PRIMARY KEY,
    Host nvarchar(255) NOT NULL, FileRootPath nvarchar(512) NOT NULL);
CREATE TABLE dbo.FgLogDefinition (
    EquipmentId nvarchar(64) NOT NULL, LogType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL, GenerationType nvarchar(16) NOT NULL,
    DirectoryTemplate nvarchar(512) NOT NULL, FileNamePattern nvarchar(256) NOT NULL,
    SlotCardinality nvarchar(16) NOT NULL, MetadataParseMode nvarchar(16) NOT NULL,
    RelativePathMetadataPattern nvarchar(1024) NOT NULL,
    MetadataGroupMappings nvarchar(max) NOT NULL DEFAULT '[]',
    CONSTRAINT PK_FgLogDefinition PRIMARY KEY (EquipmentId, LogType));
CREATE TABLE dbo.FgConfigurationDefinition (
    EquipmentId nvarchar(64) NOT NULL, ConfigurationType nvarchar(128) NOT NULL,
    ServerId nvarchar(64) NOT NULL,
    CurrentDirectoryTemplate nvarchar(512) NOT NULL, CurrentFileNamePattern nvarchar(256) NOT NULL,
    CurrentFileNameMatchMode nvarchar(16) NOT NULL DEFAULT '',
    HistoryDirectoryTemplate nvarchar(512) NOT NULL, HistoryFileNamePattern nvarchar(256) NOT NULL,
    HistoryFileNameMatchMode nvarchar(16) NOT NULL DEFAULT '',
    HistoryCompletionMarkerPathTemplate nvarchar(512) NOT NULL,
    HistoryTimestampParseMode nvarchar(16) NOT NULL DEFAULT '',
    HistoryFileNameTimestampPattern nvarchar(1024) NOT NULL DEFAULT '',
    HistoryTimestampMappings nvarchar(max) NOT NULL DEFAULT '',
    CONSTRAINT PK_FgConfigurationDefinition PRIMARY KEY (EquipmentId, ConfigurationType));
