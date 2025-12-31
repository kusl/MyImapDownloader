using System.Diagnostics;
using AwesomeAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class ActivityExtensionsTests : IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _recordedActivities = [];

    public ActivityExtensionsTests()
    {
        _activitySource = new ActivitySource("TestSource");
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _recordedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activitySource.Dispose();
    }

    [Test]
    public async Task RecordException_AddsExceptionEvent_ToActivity()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var exception = new InvalidOperationException("Test error message");

        activity.RecordException(exception);

        var events = activity!.Events.ToList();
        await Assert.That(events.Count).IsEqualTo(1);

        var exceptionEvent = events[0];
        await Assert.That(exceptionEvent.Name).IsEqualTo("exception");
    }

    [Test]
    public async Task RecordException_IncludesExceptionType()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var exception = new ArgumentNullException("paramName");

        activity.RecordException(exception);

        var events = activity!.Events.ToList();
        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().ContainKey("exception.type");
        tags["exception.type"].Should().Be(typeof(ArgumentNullException).FullName);
    }

    [Test]
    public async Task RecordException_IncludesExceptionMessage()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var exception = new Exception("Specific error details");

        activity.RecordException(exception);

        var events = activity!.Events.ToList();
        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().ContainKey("exception.message");
        tags["exception.message"]!.ToString().Should().Contain("Specific error details");
    }

    [Test]
    public async Task RecordException_IncludesStackTrace_WhenAvailable()
    {
        using var activity = _activitySource.StartActivity("TestOperation");

        Exception? capturedException = null;
        try
        {
            throw new Exception("Error with stack trace");
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }

        activity.RecordException(capturedException!);

        var events = activity!.Events.ToList();
        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().ContainKey("exception.stacktrace");
        tags["exception.stacktrace"]!.ToString().Should().Contain("RecordException_IncludesStackTrace");
    }

    [Test]
    public async Task RecordException_WithNullActivity_DoesNotThrow()
    {
        Activity? nullActivity = null;
        var exception = new Exception("Test");

        // Should not throw
        nullActivity.RecordException(exception);

        // If we reach here, the test passed
        await Assert.That(nullActivity).IsNull();
    }

    [Test]
    public async Task RecordException_WithNullException_DoesNotThrow()
    {
        using var activity = _activitySource.StartActivity("TestOperation");

        // Should not throw
        activity.RecordException(null!);

        var events = activity!.Events.ToList();
        await Assert.That(events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SetErrorStatus_SetsStatusToError()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var exception = new Exception("Operation failed");

        activity.SetErrorStatus(exception);

        await Assert.That(activity!.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task SetErrorStatus_IncludesExceptionMessage_InStatusDescription()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var exception = new Exception("Detailed failure reason");

        activity.SetErrorStatus(exception);

        activity!.StatusDescription.Should().Contain("Detailed failure reason");
    }

    [Test]
    public async Task SetErrorStatus_WithNullActivity_DoesNotThrow()
    {
        Activity? nullActivity = null;
        var exception = new Exception("Test");

        // Should not throw
        nullActivity.SetErrorStatus(exception);

        // If we reach here, the test passed
        await Assert.That(nullActivity).IsNull();
    }

    [Test]
    public async Task RecordException_HandlesNestedExceptions()
    {
        using var activity = _activitySource.StartActivity("TestOperation");
        var inner = new ArgumentException("Inner error");
        var outer = new InvalidOperationException("Outer error", inner);

        activity.RecordException(outer);

        var events = activity!.Events.ToList();
        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);

        // Should record the outer exception's details
        tags["exception.type"].Should().Be(typeof(InvalidOperationException).FullName);
        tags["exception.message"]!.ToString().Should().Contain("Outer error");
    }
}
