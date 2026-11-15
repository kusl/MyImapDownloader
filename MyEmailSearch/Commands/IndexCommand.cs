using System.CommandLine;

namespace MyEmailSearch.Commands;

public static class IndexCommand
{
    public static Command Create(GlobalOptions global)
    {
        var full = new Option<bool>("--full", "-f") { Description = "Force full re-index (ignore incremental state)" };
        var content = new Option<bool>("--content") { Description = "Index email body content for full-text search" };

        var command = new Command("index", "Build or update the search index") { full, content };

        command.SetAction(async (parseResult, ct) =>
        {
            var settings = global.Resolve(parseResult);
            var output = parseResult.InvocationConfiguration.Output;
            var rebuild = parseResult.GetValue(full);
            var includeContent = parseResult.GetValue(content);

            output.WriteLine($"Indexing emails from: {settings.ArchivePath}");
            output.WriteLine($"Database location:    {settings.DatabasePath}");
            output.WriteLine($"Mode:                 {(rebuild ? "Full rebuild" : "Incremental")}");
            output.WriteLine($"Index content:        {(includeContent ? "Yes" : "No")}");
            output.WriteLine();

            await using var context = await SearchContext.OpenAsync(settings, ct);
            var progress = new ConsoleProgress(output);
            var result = rebuild
                ? await context.Indexer.RebuildAsync(settings.ArchivePath, includeContent, progress, ct)
                : await context.Indexer.IndexAsync(settings.ArchivePath, includeContent, progress, ct);

            output.WriteLine();
            output.WriteLine();
            output.WriteLine("Indexing complete:");
            output.WriteLine($"  New emails indexed: {result.Indexed:N0}");
            output.WriteLine($"  Skipped (existing): {result.Skipped:N0}");
            output.WriteLine($"  Errors:             {result.Errors:N0}");
            output.WriteLine($"  Duration:           {result.Duration}");
        });

        return command;
    }
}
