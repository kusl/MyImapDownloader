using System.CommandLine;

namespace MyEmailSearch.Commands;

public static class RebuildCommand
{
    public static Command Create(GlobalOptions global, TextReader input)
    {
        var yes = new Option<bool>("--yes", "-y") { Description = "Skip confirmation prompt" };
        var content = new Option<bool>("--content") { Description = "Also index email body content" };

        var command = new Command("rebuild", "Rebuild the entire search index from scratch") { yes, content };

        command.SetAction(async (parseResult, ct) =>
        {
            var settings = global.Resolve(parseResult);
            var output = parseResult.InvocationConfiguration.Output;

            if (!parseResult.GetValue(yes))
            {
                output.Write("This will delete and rebuild the entire index. Continue? [y/N]: ");
                if (!string.Equals(input.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteLine("Cancelled.");
                    return;
                }
            }

            output.WriteLine("Rebuilding index...");
            output.WriteLine($"Archive path: {settings.ArchivePath}");
            output.WriteLine($"Database path: {settings.DatabasePath}");
            output.WriteLine();

            await using var context = await SearchContext.OpenAsync(settings, ct);
            var result = await context.Indexer.RebuildAsync(
                settings.ArchivePath, parseResult.GetValue(content), new ConsoleProgress(output), ct);

            output.WriteLine();
            output.WriteLine();
            output.WriteLine("Rebuild complete:");
            output.WriteLine($"  Indexed: {result.Indexed:N0}");
            output.WriteLine($"  Errors:  {result.Errors:N0}");
            output.WriteLine($"  Time:    {result.Duration}");
        });

        return command;
    }
}
