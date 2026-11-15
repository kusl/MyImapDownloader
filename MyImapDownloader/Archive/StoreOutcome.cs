namespace MyImapDownloader.Archive;

public sealed record Stored(string MessageId, string Path, long Bytes);

public sealed record Duplicate(string MessageId);

public union StoreOutcome(Stored, Duplicate);
