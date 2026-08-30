using System.Text.RegularExpressions;

namespace FileGateway.Core.Files;

/// <summary>
/// 정의 기준정보(디렉터리 세그먼트·파일명·metadata) regex의 공용 컴파일 헬퍼.
/// 모든 pattern을 \A(?:PATTERN)\z로 감싸 컴파일해 전체 일치를 구조적으로 강제한다 —
/// 시작 ^/종료 $ 문자열 검사만으로는 ^foo|bar$ 같은 alternation 부분 일치와 .NET $의
/// trailing newline 허용을 막을 수 없다. matchTimeout은 RegexMatchTimeoutException 기반
/// (스레드를 죽이지 않는 표준 기제)이며 기본 250ms 상수. 선택 인자는 테스트 전용 주입 seam이지
/// 운영 configuration option이 아니다 — 운영 코드는 항상 인자 없이 호출한다.
/// </summary>
public static class DefinitionRegex
{
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(250);

    public static Regex Compile(string pattern, RegexOptions options, TimeSpan? matchTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new Regex("\\A(?:" + pattern + ")\\z", options | RegexOptions.CultureInvariant,
            matchTimeout ?? DefaultMatchTimeout);
    }
}
