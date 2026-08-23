// src/FileGateway.Configurations/Definitions/Models.cs
using FileGateway.Core.Files;

namespace FileGateway.Configurations.Definitions;

public sealed record CurrentRule(string PathTemplate, string FilePattern);

public sealed record HistoryRule(string PathTemplate, string FilePattern, string MarkerPathTemplate);

public sealed record EquipmentConfigurationDefinition(
    string EquipmentId,
    string ConfigurationType,
    string ServerId,
    CurrentRule CurrentRule,
    HistoryRule HistoryRule);

public sealed record ResolvedConfigurationDefinition(EquipmentConfigurationDefinition Definition, FileServerConnection Server);
