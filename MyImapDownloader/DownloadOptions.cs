using CommandLine;

namespace MyImapDownloader;

public class DownloadOptions
{
    [Option('s', "server", Required = true, HelpText = "IMAP server address")]
    public required string Server { get; set; }

    [Option('u', "username", Required = true, HelpText = "Email username")]
    public required string Username { get; set; }

    [Option('p', "password", Required = true, HelpText = "Email password")]
    public required string Password { get; set; }

    [Option('r', "port", Default = 993, HelpText = "IMAP port (default: 993)")]
    public int Port { get; set; } = 993;

    [Option('o', "output", Default = "EmailArchive", HelpText = "Output directory for archived emails")]
    public required string OutputDirectory { get; set; }

    [Option("start-date", HelpText = "Download emails from this date (yyyy-MM-dd)")]
    public DateTime? StartDate { get; set; }

    [Option("end-date", HelpText = "Download emails until this date (yyyy-MM-dd)")]
    public DateTime? EndDate { get; set; }

    [Option('a', "all-folders", Default = false, HelpText = "Download from all folders, not just INBOX")]
    public bool AllFolders { get; set; }

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose logging")]
    public bool Verbose { get; set; }
}
