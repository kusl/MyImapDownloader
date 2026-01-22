using System.Diagnostics;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class ActivityExtensionsTests
{
    private readonly ActivitySource _source = new("TestSource", "1.0.0");

    [Test]
    public async Task RecordException_AddsExceptionEvent()
    {
        using var activity = _source.StartActivity("test");
        var ex = new InvalidOperationException("Test error");

        activity?.RecordException(ex);

        var events = activity?.Events.ToList() ?? [];
        await Assert.That(events.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(events[0].Name).IsEqualTo("exception");
    }

    [Test]
    public async Task RecordException_SetsErrorStatus()
    {
        using var activity = _source.StartActivity("test");
        var ex = new InvalidOperationException("Test error");

        activity?.RecordException(ex);

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task SetSuccess_SetsOkStatus()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetSuccess("All good");

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Ok);
    }

    [Test]
    public async Task SetError_SetsErrorStatus()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetError("Something failed");

        await Assert.That(activity?.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task SetTagIfNotEmpty_AddsTag_WhenValueNotEmpty()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetTagIfNotEmpty("key", "value");

        var tag = activity?.Tags.FirstOrDefault(t => t.Key == "key");
        await Assert.That(tag?.Value).IsEqualTo("value");
    }

    [Test]
    public async Task SetTagIfNotEmpty_DoesNotAddTag_WhenValueEmpty()
    {
        using var activity = _source.StartActivity("test");

        activity?.SetTagIfNotEmpty("key", "");

        var hasTag = activity?.Tags.Any(t => t.Key == "key") ?? false;
        await Assert.That(hasTag).IsFalse();
    }

    [Test]
    public async Task RecordException_HandlesNullActivity()
    {
        Activity? activity = null;
        var ex = new InvalidOperationException("Test");

        // Should not throw
        activity.RecordException(ex);

        await Assert.That(true).IsTrue(); // Just verify no exception
    }
}
