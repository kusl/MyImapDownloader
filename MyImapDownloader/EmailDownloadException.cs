namespace MyImapDownloader;

// Custom Exceptions
public class EmailDownloadException(string message, int messageIndex, Exception innerException)
    : Exception(message, innerException)
{
    public int MessageIndex { get; } = messageIndex;
}
