// Dependency Injection

// Resilience and Retry

// IMAP and Email

// Command Line Parsing
namespace MyImapDownloader
{
    // Configuration Model
    public class ImapConfiguration
    {
        public required string Server { get; set; }
        public int Port { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
        public bool UseSsl { get; set; } = true;
    }
}