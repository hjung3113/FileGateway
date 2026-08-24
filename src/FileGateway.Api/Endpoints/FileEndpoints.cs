using FileGateway.Api.Downloading;
using FileGateway.Configurations;
using FileGateway.Configurations.Tokens;
using FileGateway.Core.Errors;
using FileGateway.Core.Files;
using FileGateway.Core.Tokens;
using FileGateway.Logs;
using FileGateway.Logs.Tokens;

namespace FileGateway.Api.Endpoints;

/// <summary>공통 fileId 재해석 엔드포인트. 로그/구성 목록에서 발급한 opaque fileId를 메타데이터 또는 스트리밍 다운로드로 해석한다.</summary>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/files/{fileId}", async (
            string fileId, ITokenCodec codec, ILogQueryService logs,
            IConfigurationQueryService configurations, HttpContext ctx, CancellationToken ct) =>
        {
            var located = await LocateAsync(fileId, codec, logs, configurations, ct);
            ctx.Items["Audit.FileId"] = fileId;
            ctx.Items["Audit.FileName"] = located.FileName;
            return Results.Ok(new { fileId, fileName = located.FileName, size = located.Size });
        });

        app.MapGet("/api/v1/files/{fileId}/download", async (
            string fileId, ITokenCodec codec, ILogQueryService logs,
            IConfigurationQueryService configurations, IFileAccess fileAccess,
            HttpContext ctx, CancellationToken ct) =>
        {
            var located = await LocateAsync(fileId, codec, logs, configurations, ct);
            ctx.Items["Audit.FileId"] = fileId;
            ctx.Items["Audit.FileName"] = located.FileName;
            return new DownloadResult(located, fileAccess);
        });
        return app;
    }

    private static async Task<LocatedFile> LocateAsync(
        string fileId, ITokenCodec codec, ILogQueryService logs,
        IConfigurationQueryService configurations, CancellationToken ct)
    {
        var payload = DecodeFileId(fileId, codec);
        return payload.Purpose switch
        {
            LogTokenKinds.FileIdPurpose => await logs.LocateByFileIdAsync(payload, ct),
            ConfigurationTokenKinds.FileIdCurrentPurpose or ConfigurationTokenKinds.FileIdSnapshotPurpose
                => await configurations.LocateByFileIdAsync(payload, ct),
            _ => throw new FileGatewayException("InvalidFileId", "unknown file id purpose"),
        };
    }

    // 목적을 사전에 알 수 없는 공통 엔드포인트 특성상, 세 종류의 file-id purpose를 순서대로 시도한다.
    // protector purpose가 암호 키에 묶이므로 잘못된 purpose는 복호화 자체에 실패(Invalid)하고,
    // 성공한 purpose는 최대 하나뿐이다(Valid 또는 Expired 하나만 비-Invalid 결과로 나온다).
    private static TokenPayload DecodeFileId(string fileId, ITokenCodec codec)
    {
        var decoded = new TokenDecodeResult(TokenValidity.Invalid, null);
        foreach (var purpose in new[]
        {
            LogTokenKinds.FileIdPurpose,
            ConfigurationTokenKinds.FileIdCurrentPurpose,
            ConfigurationTokenKinds.FileIdSnapshotPurpose,
        })
        {
            var attempt = codec.Unprotect(fileId, purpose);
            if (attempt.Validity != TokenValidity.Invalid)
            {
                decoded = attempt;
                break;
            }
        }
        if (decoded.Validity == TokenValidity.Invalid)
            throw new FileGatewayException("InvalidFileId", "malformed file id");
        if (decoded.Validity == TokenValidity.Expired)
            throw new FileGatewayException("FileIdExpired", "file id expired");
        return decoded.Payload!;
    }
}
