using System;
using System.Text.RegularExpressions;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Parses user search queries into structured SearchQuery objects.
/// Supports syntax like: from:alice@example.com subject:"project update" kafka
/// </summary>
public sealed partial class QueryParser
{
    [GeneratedRegex(@"from:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"to:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ToPattern();

    [GeneratedRegex(@"subject:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPattern();

    [GeneratedRegex(@"date:(?<from>\d{4}-\d{2}-\d{2})(?:\.\.(?<to>\d{4}-\d{2}-\d{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"account:(?<value>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"folder:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex FolderPattern();

    /// <summary>
    /// Parses a user query string into a SearchQuery object.
    /// </summary>
    public SearchQuery Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SearchQuery();
        }

        var remaining = input;
        string? fromAddress = null;
        string? toAddress = null;
        string? subject = null;
        string? account = null;
        string? folder = null;
        DateTimeOffset? dateFrom = null;
        DateTimeOffset? dateTo = null;

        // Extract from: field
        var fromMatch = FromPattern().Match(remaining);
        if (fromMatch.Success)
        {
            fromAddress = ExtractValue(fromMatch.Groups["value"].Value);
            remaining = FromPattern().Replace(remaining, "", 1);
        }

        // Extract to: field
        var toMatch = ToPattern().Match(remaining);
        if (toMatch.Success)
        {
            toAddress = ExtractValue(toMatch.Groups["value"].Value);
            remaining = ToPattern().Replace(remaining, "", 1);
        }

        // Extract subject: field
        var subjectMatch = SubjectPattern().Match(remaining);
        if (subjectMatch.Success)
        {
            subject = ExtractValue(subjectMatch.Groups["value"].Value);
            remaining = SubjectPattern().Replace(remaining, "", 1);
        }

        // Extract date: field
        var dateMatch = DatePattern().Match(remaining);
        if (dateMatch.Success)
        {
            if (DateTimeOffset.TryParse(dateMatch.Groups["from"].Value, out var from))
            {
                dateFrom = from;
            }
            if (dateMatch.Groups["to"].Success &&
                DateTimeOffset.TryParse(dateMatch.Groups["to"].Value, out var to))
            {
                dateTo = to.AddDays(1).AddTicks(-1); // End of day
            }
            remaining = DatePattern().Replace(remaining, "", 1);
        }

        // Extract account: field
        var accountMatch = AccountPattern().Match(remaining);
        if (accountMatch.Success)
        {
            account = accountMatch.Groups["value"].Value;
            remaining = AccountPattern().Replace(remaining, "", 1);
        }

        // Extract folder: field
        var folderMatch = FolderPattern().Match(remaining);
        if (folderMatch.Success)
        {
            folder = ExtractValue(folderMatch.Groups["value"].Value);
            remaining = FolderPattern().Replace(remaining, "", 1);
        }

        // Remaining text is full-text content search
        var contentTerms = remaining.Trim();

        return new SearchQuery
        {
            FromAddress = fromAddress,
            ToAddress = toAddress,
            Subject = subject,
            ContentTerms = string.IsNullOrWhiteSpace(contentTerms) ? null : contentTerms,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Account = account,
            Folder = folder
        };
    }

    private static string ExtractValue(string value)
    {
        // Remove surrounding quotes if present
        if (value.StartsWith('"') && value.EndsWith('"') && value.Length > 2)
        {
            return value[1..^1];
        }
        return value;
    }
}
