using System.IO.Compression;
using FileGateway.Core.Files;
using FileGateway.Core.Streams;
using Microsoft.AspNetCore.Http.Features;

namespace FileGateway.Api.Downloading;

/// <summary>복수 매치 파일 zip 스트리밍 다운로드. 파일별 순차 복사로 메모리에 전체를 적재하지 않는다.</summary>
public sealed class ZipDownloadResult(
    string zipFileName, IReadOnlyList<LocatedFile> files, IFileAccess fileAccess) : IResult
{
    public async Task ExecuteAsync(HttpContext ctx)
    {
        // BCL ZipArchive는 엔트리 finalize(data descriptor/central directory 기록)를 내부적으로
        // 동기 Write로 수행한다. AllowSynchronousIO=false(기본값)인 호스팅에서는 그 시점에
        // InvalidOperationException이 발생해 zip이 truncate되므로 이 응답에 한해 동기 쓰기를 허용한다.
        // 단일 파일 경로(DownloadResult)는 async-only이므로 그대로 둔다.
        ctx.Features.Get<IHttpBodyControlFeature>()?.AllowSynchronousIO = true;

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/zip";
        ctx.Response.Headers.ContentDisposition = ContentDispositionHelper.Attachment(zipFileName);
        // Content-Length 설정 금지: zip 총 크기를 사전에 알 수 없다 → chunked streaming

        // 실패해도 감사에 남도록 스트리밍 시작 전에 설정한다(전송 바이트는 성공/실패 모두 아래에서 확정).
        ctx.Items["Audit.FileName"] = zipFileName;

        long transferred = 0;
        var buffer = new byte[81_920];
        var used = new List<string>();                                  // case-insensitive 엔트리명 중복 판정
        var zip = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        try
        {
            foreach (var f in files)
            {
                var open = await fileAccess.OpenReadAsync(f.Server, f.RelativePath, ctx.RequestAborted);
                // open 직후 소유권을 넘긴다. CreateEntry/entry.Open()이 중단된 응답에서 던져도
                // 원격 스트림이 해제되지 않으면 FTP lease(동시성 permit)가 영구히 반납되지 않는다.
                await using var capped = new ExactLengthStream(open.Stream, open.Length);
                var entry = zip.CreateEntry(EntryName(f.FileName, used), CompressionLevel.Fastest);
                await using var es = entry.Open();
                int read;                                               // 파일별 스트리밍(전송 바이트를 진행하며 누적)
                while ((read = await capped.ReadAsync(buffer, ctx.RequestAborted)) > 0)
                {
                    await es.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                    transferred += read;
                }
            }
        }
        catch
        {
            ctx.Items["Audit.FileSize"] = transferred;                  // 중단 지점까지 실제로 보낸 바이트
            // 부분 zip이 "정상" 아카이브로 보이면 안 된다. dispose가 central directory를 써서
            // 유효한 200 zip을 완성하기 전에 연결을 끊는다(단일 다운로드는 Content-Length 부족으로
            // 클라이언트가 truncate를 감지하지만 zip은 길이가 없어 이 구분이 불가능하다).
            ctx.Abort();
            try { await zip.DisposeAsync(); } catch { /* 중단 이후의 2차 실패는 원본 분류를 가리지 않는다 */ }
            throw;
        }
        ctx.Items["Audit.FileSize"] = transferred;                      // AuditMiddleware는 응답 완료 후 finally에서 읽음
        await zip.DisposeAsync();                                       // 정상 완료에서만 central directory를 기록한다
    }

    // 첫 등장은 원본 FileName 그대로, 이후 중복은 확장자 앞 _N suffix(대소문자 무시 판정, ListAsync 순서로 결정적).
    private static string EntryName(string fileName, List<string> used)
    {
        if (!used.Contains(fileName, FileNameComparison.Comparer))
        {
            used.Add(fileName);
            return fileName;
        }
        var ext = Path.GetExtension(fileName);
        var stem = ext.Length == 0 ? fileName : fileName[..^ext.Length];
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}_{n}{ext}";
            if (!used.Contains(candidate, FileNameComparison.Comparer))
            {
                used.Add(candidate);
                return candidate;
            }
        }
    }
}
