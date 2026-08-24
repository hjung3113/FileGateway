// src/FileGateway.Api/Auth/ApiKeyMiddleware.cs
using System.Security.Cryptography;
using System.Text;
using FileGateway.Api.Options;
using FileGateway.Core.Errors;
using Microsoft.Extensions.Options;

namespace FileGateway.Api.Auth;

/// <summary>/api/ 경로에만 동작. X-Api-Key header 단독(query string 미사용), 누락/불일치 모두 401 InvalidApiKey.
/// 성공 시 Items["CallerId"]를 남긴다(감사 로그 소비).</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var options = context.RequestServices.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) || provided.Count != 1
                || MatchCaller(options.ApiKeys, provided.ToString()) is not { } callerId)
            {
                throw new FileGatewayException("InvalidApiKey");
            }
            context.Items["CallerId"] = callerId;
        }
        await next(context);
    }

    // 모든 후보와 FixedTimeEquals로 비교해 키 비교 시간을 후보 수로만 결정한다.
    private static string? MatchCaller(IEnumerable<ApiKeyEntry> entries, string provided)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        string? matched = null;
        foreach (var entry in entries)
        {
            var candidate = Encoding.UTF8.GetBytes(entry.Key);
            if (providedBytes.Length == candidate.Length
                && CryptographicOperations.FixedTimeEquals(providedBytes, candidate))
                matched = entry.CallerId;
        }
        return matched;
    }
}
