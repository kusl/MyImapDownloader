namespace MyImapDownloader.Tests;

public class FolderCheckpointTrackerTests
{
    [Test]
    public async Task NoFailures_AdvancesToHighestProcessedUid()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkProcessed(5);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(5L);
        await Assert.That(tracker.LowestFailedUid).IsNull();
    }

    [Test]
    public async Task FailureInMiddle_HoldsCheckpointBeforeFailure()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkFailed(3);
        tracker.MarkProcessed(4);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(2L);
        await Assert.That(tracker.LowestFailedUid).IsEqualTo(3L);
    }

    [Test]
    public async Task FailureInEarlierBatch_IsNotSkippedByLaterBatches()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(10);
        tracker.MarkFailed(11);

        for (uint uid = 12; uid <= 200; uid++)
        {
            tracker.MarkProcessed(uid);
        }

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(10L);
    }

    [Test]
    public async Task FirstUidFails_CheckpointStaysAtZero()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkFailed(7);
        tracker.MarkProcessed(8);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(0L);
    }

    [Test]
    public async Task LowerFailureAfterHigherFailure_LowersCheckpoint()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkProcessed(3);
        tracker.MarkFailed(5);
        tracker.MarkFailed(2);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(1L);
        await Assert.That(tracker.LowestFailedUid).IsEqualTo(2L);
    }
}
