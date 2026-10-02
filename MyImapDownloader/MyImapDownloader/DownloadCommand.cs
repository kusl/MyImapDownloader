using System.CommandLine;

namespace MyImapDownloader;

public static class DownloadCommand
{
    public static RootCommand Create(Func<DownloadOptions, CancellationToken, Task<int>> run)
    {
        var serverOption = new Option<string>("--server", "-s")
        {
            Description = "IMAP server address",
            Required = true
        };

        var usernameOption = new Option<string>("--username", "-u")
        {
            Description = "Email username",
            Required = true
        };

        var passwordOption = new Option<string>("--password", "-p")
        {
            Description = "Email password",
            Required = true
        };

        var portOption = new Option<int>("--port", "-r")
        {
            Description = "IMAP port",
            DefaultValueFactory = _ => 993
        };

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output directory for archived emails",
            DefaultValueFactory = _ => "EmailArchive"
        };

        var startDateOption = new Option<DateTime?>("--start-date")
        {
            Description = "Download emails from this date (yyyy-MM-dd)"
        };

        var endDateOption = new Option<DateTime?>("--end-date")
        {
            Description = "Download emails until this date (yyyy-MM-dd)"
        };

        var allFoldersOption = new Option<bool>("--all-folders", "-a")
        {
            Description = "Download from all folders, not just INBOX"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose logging"
        };

        var rootCommand = new RootCommand("Archive emails from an IMAP server to local .eml files");
        rootCommand.Options.Add(serverOption);
        rootCommand.Options.Add(usernameOption);
        rootCommand.Options.Add(passwordOption);
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(startDateOption);
        rootCommand.Options.Add(endDateOption);
        rootCommand.Options.Add(allFoldersOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.SetAction((parseResult, ct) => run(
            new DownloadOptions
            {
                Server = parseResult.GetValue(serverOption)!,
                Username = parseResult.GetValue(usernameOption)!,
                Password = parseResult.GetValue(passwordOption)!,
                Port = parseResult.GetValue(portOption),
                OutputDirectory = parseResult.GetValue(outputOption)!,
                StartDate = parseResult.GetValue(startDateOption),
                EndDate = parseResult.GetValue(endDateOption),
                AllFolders = parseResult.GetValue(allFoldersOption),
                Verbose = parseResult.GetValue(verboseOption)
            },
            ct));

        return rootCommand;
    }

    public static ParseResult Parse(RootCommand command, IReadOnlyList<string> args) =>
        command.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
}
