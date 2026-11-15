using System.CommandLine;
using System.Diagnostics;

using MyEmailSearch.Data;

namespace MyEmailSearch.Commands;

public static class SearchCommand
{
    private const int MaxChoices = 20;

    public static Command Create(GlobalOptions global, TextReader input, Action<string> open)
    {
        var query = new Argument<string>("query")
        {
            Description = "Search query (e.g., 'from:alice@example.com subject:report kafka')"
        };
        var limit = new Option<int>("--limit", "-l")
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };
        var format = new Option<string>("--format", "-f")
        {
            Description = "Output format: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };
        format.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, ResultFormatter.Formats);
        var interactive = new Option<bool>("--open", "-o")
        {
            Description = "Interactively select and open an email in your default application"
        };

        var command = new Command("search", "Search emails in the archive") { query, limit, format, interactive };

        command.SetAction(async (parseResult, ct) =>
        {
            var settings = global.Resolve(parseResult);
            var output = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;
            var text = parseResult.GetRequiredValue(query);

            if (string.IsNullOrWhiteSpace(text))
            {
                error.WriteLine("Error: Search query cannot be empty");
                return 1;
            }

            if (!File.Exists(settings.DatabasePath))
            {
                error.WriteLine($"Error: No index exists at {settings.DatabasePath}");
                error.WriteLine("Run 'myemailsearch index' first to create the index.");
                return 1;
            }

            await using var context = await SearchContext.OpenAsync(settings, ct);
            var results = await context.Engine.SearchAsync(text, parseResult.GetValue(limit), 0, ct);

            try
            {
                if (parseResult.GetValue(interactive) && results.Results.Count > 0)
                {
                    Choose(results, input, output, error, open);
                }
                else
                {
                    ResultFormatter.Write(output, results, parseResult.GetRequiredValue(format));
                }
            }
            catch (IOException exception) when (settings.Verbose)
            {
                error.WriteLine($"Output error: {exception.Message}");
            }
            catch (IOException)
            {
            }

            return 0;
        });

        return command;
    }

    public static void OpenWithDefaultApplication(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void Choose(
        SearchResultSet results,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Action<string> open)
    {
        var shown = Math.Min(results.Results.Count, MaxChoices);

        output.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        output.WriteLine();

        for (var i = 0; i < shown; i++)
        {
            var email = results.Results[i].Email;
            var date = email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = ResultFormatter.Truncate(email.FromAddress ?? "Unknown", 25);
            var subject = ResultFormatter.Truncate(email.Subject ?? "(no subject)", 45);
            output.WriteLine($"[{i + 1,2}] {date}  {from,-25}  {subject}");
        }

        if (results.TotalCount > shown)
        {
            output.WriteLine($"... and {results.TotalCount - shown} more (use --limit to see more)");
        }

        output.WriteLine();
        output.Write($"Open which result? (1-{shown}, or q to quit): ");

        var answer = input.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer) || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Cancelled.");
            return;
        }

        if (!int.TryParse(answer, out var selection) || selection < 1 || selection > shown)
        {
            error.WriteLine($"Invalid selection. Please enter a number between 1 and {shown}.");
            return;
        }

        var path = results.Results[selection - 1].Email.FilePath;
        if (!File.Exists(path))
        {
            error.WriteLine($"Error: Email file not found: {path}");
            return;
        }

        output.WriteLine($"Opening: {path}");
        try
        {
            open(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error.WriteLine($"Error opening file: {exception.Message}");
        }
    }
}
