namespace MyEmailSearch.Search;

public static class SnippetGenerator
{
    private const int SnippetLength = 200;
    private const int ContextPadding = 50;
    private const string Ellipsis = "...";

    public static string? Generate(string? bodyText, string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(searchTerms))
        {
            return Truncate(bodyText);
        }

        var firstMatch = searchTerms
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(term => bodyText.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        if (firstMatch < 0)
        {
            return Truncate(bodyText);
        }

        var start = Math.Max(0, firstMatch - ContextPadding);
        var end = Math.Min(bodyText.Length, start + SnippetLength);

        if (start > 0 && bodyText.LastIndexOf(' ', start) is > 0 and var wordStart)
        {
            start = wordStart + 1;
        }

        if (end < bodyText.Length && bodyText.IndexOf(' ', end) is > 0 and var wordEnd)
        {
            end = wordEnd;
        }

        return string.Concat(
            start > 0 ? Ellipsis : "",
            bodyText.AsSpan(start, end - start),
            end < bodyText.Length ? Ellipsis : "");
    }

    private static string Truncate(string text)
    {
        if (text.Length <= SnippetLength)
        {
            return text;
        }

        var truncated = text[..SnippetLength];
        var lastSpace = truncated.LastIndexOf(' ');
        return (lastSpace > SnippetLength / 2 ? truncated[..lastSpace] : truncated) + Ellipsis;
    }
}
