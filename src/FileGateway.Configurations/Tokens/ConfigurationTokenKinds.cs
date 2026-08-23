// src/FileGateway.Configurations/Tokens/ConfigurationTokenKinds.cs
namespace FileGateway.Configurations.Tokens;

/// <summary>Configuration 파일 종류별 opaque token purpose 상수.</summary>
public static class ConfigurationTokenKinds
{
    public const string FileIdCurrentPurpose = "fg.fileid.cfgcurrent";
    public const string FileIdSnapshotPurpose = "fg.fileid.cfgsnapshot";
    public const string ContinuationPurpose = "fg.page.cfghistory";
}
