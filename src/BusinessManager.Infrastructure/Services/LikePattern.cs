namespace BusinessManager.Infrastructure.Services;

internal static class LikePattern
{
    public const string Escape = "\\";
    public const int MaxSearchLength = 100;

    /// <summary>
    /// Builds a "contains" LIKE pattern, escaping wildcards so user input such
    /// as "50%" or "a_b" is matched literally. Returns null for blank input.
    /// </summary>
    public static string? Contains(string? text)
    {
        string? term = Normalize(text);
        return term is null ? null : $"%{EscapeText(term)}%";
    }

    public static string? StartsWith(string? text)
    {
        string? term = Normalize(text);
        return term is null ? null : $"{EscapeText(term)}%";
    }

    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }

    private static string EscapeText(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
