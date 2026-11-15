using System.Diagnostics;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests;

public class ActivityExtensionsTests
{
    [Test]
    public async Task RecordError_SetsErrorStatusAndExceptionEvent()
    {
        using var activity = new Activity("test").Start();

        activity.RecordError(new InvalidOperationException("boom"));

        var recorded = activity.Events.Single();
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(activity.StatusDescription).IsEqualTo("boom");
        await Assert.That(recorded.Name).IsEqualTo("exception");
        await Assert.That(recorded.Tags.Any(tag => tag.Key == "exception.message" && (string?)tag.Value == "boom")).IsTrue();
    }

    [Test]
    public async Task RecordError_IgnoresMissingActivity()
    {
        Activity? activity = null;

        activity.RecordError(new InvalidOperationException("boom"));

        await Assert.That(activity).IsNull();
    }
}
