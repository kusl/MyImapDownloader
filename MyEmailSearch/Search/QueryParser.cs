using System.Globalization;
using System.Text.RegularExpressions;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

public static partial class QueryParser
{
    public static SearchQuery Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SearchQuery();
        }

        var query = new SearchQuery();
        HashSet<string> seen = [];

        var remaining = Term().Replace(input, match =>
        {
            var key = match.Groups["key"].Value.ToLowerInvariant();
            if (seen.Contains(key) || Apply(query, key, Unquote(match.Groups["value"].Value)) is not { } updated)
            {
                return match.Value;
            }

            seen.Add(key);
            query = updated;
            return "";
        });

        var terms = remaining.Trim();
        return query with { ContentTerms = terms.Length == 0 ? null : terms };
    }

    private static SearchQuery? Apply(SearchQuery query, string key, string value) => key switch
    {
        "from" => query with { FromAddress = value },
        "to" => query with { ToAddress = value },
        "subject" => query with { Subject = value },
        "account" => query with { Account = value },
        "folder" => query with { Folder = value },
        "after" when ParseDay(value) is { } day => query with { DateFrom = day },
        "before" when ParseDay(value) is { } day => query with { DateTo = EndOfDay(day) },
        "date" when ParseRange(value) is { } range => query with { DateFrom = range.From, DateTo = range.To ?? query.DateTo },
        _ => null
    };

    private static (DateTimeOffset From, DateTimeOffset? To)? ParseRange(string value)
    {
        var parts = value.Split("..");
        if (parts.Length > 2 || ParseDay(parts[0]) is not { } start)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return (start, null);
        }

        return ParseDay(parts[1]) is { } end ? (start, EndOfDay(end)) : null;
    }

    private static DateTimeOffset? ParseDay(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
            : null;

    private static DateTimeOffset EndOfDay(DateTimeOffset day) => day.AddDays(1).AddTicks(-1);

    private static string Unquote(string value) =>
        value.Length > 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    [GeneratedRegex("""(?<!\S)(?<key>from|to|subject|account|folder|after|before|date):(?<value>"[^"]+"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Term();
}
