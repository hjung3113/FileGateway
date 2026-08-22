namespace FileGateway.Core.Files;

public sealed class GlobPattern(string pattern)
{
    public string Pattern { get; } = pattern;

    public static void Validate(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) throw new ArgumentException("empty file pattern");
        if (p.Contains('/')) throw new ArgumentException("file pattern must not contain '/'");
    }

    public bool Matches(string fileName) => Match(Pattern, 0, fileName, 0, out _);

    // 표준 two-pointer backtracking matcher (case-insensitive)
    private static bool Match(string p, int pi, string s, int si, out int end)
    {
        end = si;
        while (pi < p.Length)
        {
            var c = p[pi];
            if (c == '*')
            {
                var slash = s.IndexOf('/', si);
                var limit = slash < 0 ? s.Length : slash; // *는 /를 넘지 않는다
                for (var k = si; k <= limit; k++)
                    if (Match(p, pi + 1, s, k, out end)) return true;
                return false;
            }
            if (si >= s.Length) return false;
            if (c != '?' && !FileNameComparison.Same(c.ToString(), s[si].ToString())) return false;
            pi++; si++;
        }
        end = si;
        return si == s.Length;
    }
}
