namespace FileGateway.Core.Files;

public static class FileNameComparison
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    public static bool Same(string a, string b) => Comparer.Equals(a, b);
    public static int Compare(string a, string b) => Comparer.Compare(a, b);
}
