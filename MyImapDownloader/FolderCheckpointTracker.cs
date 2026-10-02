namespace MyImapDownloader;

public sealed class FolderCheckpointTracker
{
    private long _safeCheckpoint;
    private long? _lowestFailedUid;

    public long SafeCheckpoint => _safeCheckpoint;

    public long? LowestFailedUid => _lowestFailedUid;

    public void MarkProcessed(uint uid)
    {
        if (_lowestFailedUid is null || uid < _lowestFailedUid)
        {
            _safeCheckpoint = Math.Max(_safeCheckpoint, uid);
        }
    }

    public void MarkFailed(uint uid)
    {
        if (_lowestFailedUid is null || uid < _lowestFailedUid)
        {
            _lowestFailedUid = uid;
        }

        if (_safeCheckpoint >= _lowestFailedUid)
        {
            _safeCheckpoint = _lowestFailedUid.Value - 1;
        }
    }
}
