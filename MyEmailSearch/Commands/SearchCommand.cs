using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

        var limitOption = new Option<int>("--limit", "Maximum number of results to return")
        {
            DefaultValueFactory = _ => 100,
        };

        var formatOption = new Option<string>("--format", "Output format: table, json, or csv")
        {
            DefaultValueFactory = _ => "table"
        };

        var openOption = new Option<bool>("--open", "Interactively select and open an email in your default application")
        {
            DefaultValueFactory = _ => false
        };

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);
        command.Options.Add(openOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var openInteractive = parseResult.GetValue(openOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(query, limit, format, openInteractive, archivePath, databasePath, verbose, ct)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        bool openInteractive,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
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

        await database.InitializeAsync(ct).ConfigureAwait(false);

        var results = await searchEngine.SearchAsync(query, limit, 0, ct).ConfigureAwait(false);

        try
        {
            if (openInteractive && results.Results.Count > 0)
            {
                await HandleInteractiveOpenAsync(results, ct).ConfigureAwait(false);
            }
            else
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
        }
        catch (IOException ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Output error: {ex.Message}");
            }
        }
    }

    private static async Task HandleInteractiveOpenAsync(SearchResultSet results, CancellationToken ct)
    {
        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();

        var displayCount = Math.Min(results.Results.Count, 20);
        for (var i = 0; i < displayCount; i++)
        {
            var result = results.Results[i];
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 25);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 45);

            Console.WriteLine($"[{i + 1,2}] {date}  {from,-25}  {subject}");
        }

        if (results.TotalCount > displayCount)
        {
            Console.WriteLine($"... and {results.TotalCount - displayCount} more (use --limit to see more)");
        }

        Console.WriteLine();
        Console.Write($"Open which result? (1-{displayCount}, or q to quit): ");

        var input = await ReadLineAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLowerInvariant() == "q")
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        if (!int.TryParse(input.Trim(), out var selection) || selection < 1 || selection > displayCount)
        {
            Console.Error.WriteLine($"Invalid selection. Please enter a number between 1 and {displayCount}.");
            return;
        }

        var selectedResult = results.Results[selection - 1];
        var filePath = selectedResult.Email.FilePath;

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: Email file not found: {filePath}");
            return;
        }

        Console.WriteLine($"Opening: {filePath}");
        OpenFileWithDefaultApplication(filePath);
    }

    private static async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                return Console.ReadLine();
            }
            catch (IOException)
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    private static void OpenFileWithDefaultApplication(string filePath)
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                Console.Error.WriteLine("Unsupported platform for opening files.");
                return;
            }

            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error opening file: {ex.Message}");
        }
    }

    private static void OutputTable(SearchResultSet results)
    {
        if (results.TotalCount == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();
        Console.WriteLine($"{"Date",-12} {"From",-30} {"Subject",-50}");
        Console.WriteLine(new string('-', 94));

        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 28);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 48);

            Console.WriteLine($"{date,-12} {from,-30} {subject,-50}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                var snippet = TruncateString(result.Snippet.Replace("\n", " ").Replace("\r", ""), 80);
                Console.WriteLine($"             {snippet}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Showing {results.Results.Count} of {results.TotalCount} results");
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(results, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        Console.WriteLine("MessageId,From,Subject,Date,Folder,Account,FilePath");
        foreach (var result in results.Results)
        {
            var messageId = EscapeCsvField(result.Email.MessageId ?? "");
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
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
