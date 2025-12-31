using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'search' command for querying the email index.
/// </summary>
public static class SearchCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Search query (e.g., 'from:alice@example.com subject:report kafka')"
        };

        var limitOption = new Option<int>(["--limit", "-l"])
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>(["--format", "-f"])
        {
            Description = "Output format: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(query, limit, format, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        // Ensure database is initialized
        await database.InitializeAsync(ct);

        // Execute search
        var results = await searchEngine.SearchAsync(query, limit, 0, ct);

        // Output results
        switch (format.ToLowerInvariant())
        {
            case "json":
                OutputJson(results);
                break;
            case "csv":
                OutputCsv(results);
                break;
            default:
                OutputTable(results);
                break;
        }
    }

    private static void OutputTable(SearchResultSet results)
    {
        Console.WriteLine($"Found {results.Results.Count} results in {results.QueryTime.TotalMilliseconds:F0}ms");
        Console.WriteLine(new string('-', 100));

        if (results.Results.Count == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        foreach (var result in results.Results)
        {
            var email = result.Email;
            var date = email.DateSent?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var from = Truncate(email.FromAddress ?? "Unknown", 30);
            var subject = Truncate(email.Subject ?? "(no subject)", 50);

            Console.WriteLine($"{date}  {from,-30}  {subject}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                Console.WriteLine($"           {Truncate(result.Snippet, 90)}");
            }
            Console.WriteLine();
        }
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(results, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        Console.WriteLine("date,from,to,subject,file_path");
        foreach (var result in results.Results)
        {
            var email = result.Email;
            var date = email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var from = EscapeCsv(email.FromAddress ?? "");
            var to = EscapeCsv(string.Join(";", email.ToAddresses));
            var subject = EscapeCsv(email.Subject ?? "");
            var path = EscapeCsv(email.FilePath);

            Console.WriteLine($"{date},{from},{to},{subject},{path}");
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
