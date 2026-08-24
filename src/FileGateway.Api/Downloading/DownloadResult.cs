using FileGateway.Core.Files;
using FileGateway.Core.Streams;

namespace FileGateway.Api.Downloading;

/// <summary>단일 식별된 파일 스트리밍 다운로드. Task 18에서 재사용·완성(여기선 최소 실행기).</summary>
public sealed class DownloadResult(LocatedFile file, IFileAccess fileAccess) : IResult
{
    public async Task ExecuteAsync(HttpContext ctx)
    {
        var open = await fileAccess.OpenReadAsync(file.Server, file.RelativePath, ctx.RequestAborted);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength = open.Length;                       // 시작 직전 크기 = 전송 상한
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.Headers.ContentDisposition =
            ContentDispositionHelper.Attachment(file.FileName);
        await using var capped = new ExactLengthStream(open.Stream, open.Length);
        await capped.CopyToAsync(ctx.Response.Body, 81_920, ctx.RequestAborted); // 시작 후 오류 → 중단(ClientCancelled/IO 분류는 미들웨어)
    }
}
