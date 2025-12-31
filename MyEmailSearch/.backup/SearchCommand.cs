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

            await ExecuteAsync(query, limit, format, archivePath, databasePath, verbose, ct)
                .ConfigureAwait(false);
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
        // Validate input
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Error: Search query cannot be empty");
            return;
        }

        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Error: No index exists at {databasePath}");
            Console.Error.WriteLine("Run 'myemailsearch index' first to create the index.");
            return;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        // Ensure database is initialized
        await database.InitializeAsync(ct).ConfigureAwait(false);

        // Execute search
        var results = await searchEngine.SearchAsync(query, limit, 0, ct).ConfigureAwait(false);

        // Output results with error handling
        try
        {
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
        catch (IOException ex)
        {
            // Handle broken pipe or other I/O errors gracefully
            if (verbose)
            {
                Console.Error.WriteLine($"Output error: {ex.Message}");
            }
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
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 30);
            var subject = TruncateString(result.Email.Subject ?? "(No subject)", 50);

            Console.WriteLine($"{date}  {from,-30}  {subject}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                Console.WriteLine($"    {TruncateString(result.Snippet, 90)}");
            }

            Console.WriteLine();
        }

        if (results.HasMore)
        {
            Console.WriteLine($"... and {results.TotalCount - results.Results.Count} more results");
        }
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var output = new
        {
            results.TotalCount,
            QueryTimeMs = results.QueryTime.TotalMilliseconds,
            Results = results.Results.Select(r => new
            {
                r.Email.MessageId,
                r.Email.FromAddress,
                r.Email.Subject,
                DateSent = r.Email.DateSent?.ToString("O"),
                r.Email.Folder,
                r.Email.Account,
                r.Email.FilePath,
                r.Snippet
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(output, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        // Header
        Console.WriteLine("\"MessageId\",\"From\",\"Subject\",\"Date\",\"Folder\",\"Account\",\"FilePath\"");

        foreach (var result in results.Results)
        {
            var messageId = EscapeCsvField(result.Email.MessageId);
            var from = EscapeCsvField(result.Email.FromAddress ?? "");
            var subject = EscapeCsvField(result.Email.Subject ?? "");
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var folder = EscapeCsvField(result.Email.Folder ?? "");
            var account = EscapeCsvField(result.Email.Account ?? "");
            var filePath = EscapeCsvField(result.Email.FilePath);

            Console.WriteLine($"{messageId},{from},{subject},\"{date}\",{folder},{account},{filePath}");
        }
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        // Escape quotes by doubling them and wrap in quotes
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
