-- db/mvp-stored-procedure-diagnostics.sql (별도 배치 파일: CREATE PROCEDURE는 배치 첫 문장이어야 하므로 분리)
CREATE OR ALTER PROCEDURE dbo.FileGateway_LogFileAccessFailure
    @EquipmentId nvarchar(64), @LogType nvarchar(128), @ServerId nvarchar(64),
    @RequestedSlotUtc datetime2, @ComputedRelativePath nvarchar(512), @FailureReason nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.FgFileAccessFailureLog
        (OccurredAtUtc, EquipmentId, LogType, ServerId, RequestedSlotUtc, ComputedRelativePath, FailureReason)
    VALUES
        (SYSUTCDATETIME(), @EquipmentId, @LogType, @ServerId, @RequestedSlotUtc, @ComputedRelativePath, @FailureReason);
END
