// Dependency Injection

// Resilience and Retry

// IMAP and Email

// Command Line Parsing
namespace MyImapDownloader
{
    // Custom Exceptions
    public class EmailDownloadException(string message, int messageIndex, Exception innerException) : Exception(message, innerException)
    {
        public int MessageIndex { get; } = messageIndex;
    }
}