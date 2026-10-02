namespace MyImapDownloader;

public class DownloadOptions
{
    public required string Server { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public int Port { get; set; } = 993;
    public required string OutputDirectory { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool AllFolders { get; set; }
    public bool Verbose { get; set; }
}