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

        long total = 0;
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
                await capped.CopyToAsync(es, 81_920, ctx.RequestAborted);   // 파일별 스트리밍
                total += open.Length;
            }
        }
        finally
        {
            // dispose가 이미 중단된 응답 스트림에 central directory를 쓰며 던지는 2차 예외가
            // 원본 오류 분류(FileServerUnavailable/ClientCancelled)를 가리지 않도록 swallow한다.
            try { await zip.DisposeAsync(); } catch { /* 응답 중단 이후의 2차 실패는 무시 */ }
        }
        ctx.Items["Audit.FileName"] = zipFileName;                     // AuditMiddleware는 응답 완료 후 finally에서 읽음
        ctx.Items["Audit.FileSize"] = total;
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
