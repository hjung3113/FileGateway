// src/FileGateway.Infrastructure/ReferenceData/IReferenceDataSource.cs
namespace FileGateway.Infrastructure.ReferenceData;

/// <summary>기준정보 원시 조회 원천(MSSQL SP). FTP 접근 없음.</summary>
public interface IReferenceDataSource
{
    Task<ReferenceDataRaw> ReadAsync(CancellationToken ct);
}
