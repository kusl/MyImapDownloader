using System.Text;

namespace MyEmailSearch.Search;

/// <summary>
/// Generates contextual snippets from email body text highlighting matched terms.
/// </summary>
public sealed class SnippetGenerator
{
    private const int SnippetLength = 200;
    private const int ContextPadding = 50;

    /// <summary>
    /// Generates a snippet from the body text centered around the search terms.
    /// </summary>
    public string? Generate(string? bodyText, string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(searchTerms))
        {
            return Truncate(bodyText, SnippetLength);
        }

        var terms = searchTerms.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstMatchIndex = -1;

        // Find the first occurrence of any search term
        foreach (var term in terms)
        {
            var index = bodyText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (firstMatchIndex < 0 || index < firstMatchIndex))
            {
                firstMatchIndex = index;
            }
        }

        if (firstMatchIndex < 0)
        {
            return Truncate(bodyText, SnippetLength);
        }

        // Calculate snippet window
        var start = Math.Max(0, firstMatchIndex - ContextPadding);
        var end = Math.Min(bodyText.Length, start + SnippetLength);

        // Adjust start to word boundary
        if (start > 0)
        {
            var wordStart = bodyText.LastIndexOf(' ', start);
            if (wordStart > 0)
            {
                start = wordStart + 1;
            }
        }

        // Adjust end to word boundary
        if (end < bodyText.Length)
        {
            var wordEnd = bodyText.IndexOf(' ', end);
            if (wordEnd > 0)
            {
                end = wordEnd;
            }
        }

        var snippet = new StringBuilder();
        if (start > 0)
        {
            snippet.Append("...");
        }

        snippet.Append(bodyText.AsSpan(start, end - start));

        if (end < bodyText.Length)
        {
            snippet.Append("...");
        }

        return snippet.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var truncated = text[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated + "...";
    }
}
