using System.CommandLine;

namespace MyImapDownloader;

public static class DownloadCommand
{
    public const string PasswordVariable = "MYIMAPDOWNLOADER_PASSWORD";

    public static RootCommand Create(
        Func<DownloadOptions, CancellationToken, Task<int>> run,
        Func<string, string?>? environment = null)
    {
        Func<string, string?> variable = environment ?? Environment.GetEnvironmentVariable;

        var server = new Option<string>("--server", "-s") { Description = "IMAP server address", Required = true };
        var username = new Option<string>("--username", "-u") { Description = "Email username", Required = true };
        var password = new Option<string?>("--password", "-p")
        {
            Description = $"Email password (defaults to the {PasswordVariable} environment variable)"
        };
        var port = new Option<int>("--port", "-r") { Description = "IMAP port", DefaultValueFactory = _ => 993 };
        var output = new Option<string>("--output", "-o")
        {
            Description = "Output directory for archived emails",
            DefaultValueFactory = _ => "EmailArchive"
        };
        var startDate = new Option<DateTime?>("--start-date")
        {
            Description = "Download emails from this date (yyyy-MM-dd)"
        };
        var endDate = new Option<DateTime?>("--end-date")
        {
            Description = "Download emails until this date (yyyy-MM-dd)"
        };
        var allFolders = new Option<bool>("--all-folders", "-a")
        {
            Description = "Download from all folders, not just INBOX"
        };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Enable verbose logging" };

        var command = new RootCommand("Archive emails from an IMAP server to local .eml files")
        {
            server, username, password, port, output, startDate, endDate, allFolders, verbose
        };

        string? ResolvePassword(string? value) =>
            !string.IsNullOrEmpty(value) ? value
            : variable(PasswordVariable) is { Length: > 0 } fallback ? fallback
            : null;

        command.Validators.Add(result =>
        {
            if (ResolvePassword(result.GetValue(password)) is null)
            {
                result.AddError($"Option '--password' is required unless {PasswordVariable} is set.");
            }
        });

        command.SetAction((parseResult, ct) => run(
            new DownloadOptions
            {
                Server = parseResult.GetRequiredValue(server),
                Username = parseResult.GetRequiredValue(username),
                Password = ResolvePassword(parseResult.GetValue(password))!,
                Port = parseResult.GetValue(port),
                OutputDirectory = parseResult.GetRequiredValue(output),
                StartDate = parseResult.GetValue(startDate),
                EndDate = parseResult.GetValue(endDate),
                AllFolders = parseResult.GetValue(allFolders),
                Verbose = parseResult.GetValue(verbose)
            },
            ct));

        return command;
    }

    public static ParseResult Parse(RootCommand command, IReadOnlyList<string> args) =>
        command.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
}
