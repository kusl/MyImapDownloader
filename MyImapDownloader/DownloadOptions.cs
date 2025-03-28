
// Dependency Injection

// Resilience and Retry

// IMAP and Email

// Command Line Parsing
using CommandLine;

namespace MyImapDownloader
{
    // Command Line Options
    public class DownloadOptions
    {
        [Option('s', "server", Required = true, HelpText = "IMAP Server Address")]
        public required string Server { get; set; }

        [Option('u', "username", Required = true, HelpText = "Email Username")]
        public required string Username { get; set; }

        [Option('p', "password", Required = true, HelpText = "Email Password")]
        public required string Password { get; set; }

        [Option("start-date", HelpText = "Start date for email download")]
        public DateTime? StartDate { get; set; }

        [Option("end-date", HelpText = "End date for email download")]
        public DateTime? EndDate { get; set; }

        [Option('o', "output", Default = "EmailDownloads", HelpText = "Output directory")]
        public required string OutputDirectory { get; set; }
        [Option('r', "port", Default = "993", HelpText = "Port")]
        public int Port { get; set; } = 993;
    }
}