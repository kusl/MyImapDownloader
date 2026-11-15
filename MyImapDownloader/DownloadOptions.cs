namespace MyImapDownloader;

public sealed record DownloadOptions
{
    public required string Server { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public int Port { get; init; } = 993;
    public required string OutputDirectory { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool AllFolders { get; init; }
    public bool Verbose { get; init; }

    public bool HasDateWindow => StartDate is not null || EndDate is not null;
}
