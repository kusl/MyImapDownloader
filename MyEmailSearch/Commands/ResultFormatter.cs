using System.Text.Json;

using MyEmailSearch.Data;

namespace MyEmailSearch.Commands;

public static class ResultFormatter
{
    public static readonly string[] Formats = ["table", "json", "csv"];

    public static void Write(TextWriter output, SearchResultSet results, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                output.WriteLine(JsonSerializer.Serialize(results, SearchOutputJsonContext.Default.SearchResultSet));
                break;
            case "csv":
                WriteCsv(output, results);
                break;
            default:
                WriteTable(output, results);
                break;
        }
    }

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";

    private static void WriteTable(TextWriter output, SearchResultSet results)
    {
        if (results.TotalCount == 0)
        {
            output.WriteLine("No results found.");
            return;
        }

        output.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        output.WriteLine();
        output.WriteLine($"{"Date",-12} {"From",-30} {"Subject",-50}");
        output.WriteLine(new string('-', 94));

        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = Truncate(result.Email.FromAddress ?? "Unknown", 28);
            var subject = Truncate(result.Email.Subject ?? "(no subject)", 48);
            output.WriteLine($"{date,-12} {from,-30} {subject,-50}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                output.WriteLine($"             {Truncate(result.Snippet.Replace("\n", " ").Replace("\r", ""), 80)}");
            }
        }

        output.WriteLine();
        output.WriteLine($"Showing {results.Results.Count} of {results.TotalCount} results");
    }

    private static void WriteCsv(TextWriter output, SearchResultSet results)
    {
        output.WriteLine("MessageId,From,Subject,Date,Folder,Account,FilePath");
        foreach (var email in results.Results.Select(result => result.Email))
        {
            output.WriteLine(string.Join(
                ',',
                Quote(email.MessageId),
                Quote(email.FromAddress),
                Quote(email.Subject),
                Quote(email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss")),
                Quote(email.Folder),
                Quote(email.Account),
                Quote(email.FilePath)));
        }
    }

    private static string Quote(string? value) => $"\"{value?.Replace("\"", "\"\"")}\"";
}
