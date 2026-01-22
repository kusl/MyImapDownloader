using System.Diagnostics;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Extension methods for Activity to provide RecordException functionality
/// that works across OpenTelemetry versions.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Records an exception as an event on the activity with standard attributes.
    /// </summary>
    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null) return;

        var tags = new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
        };

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            tags["exception.stacktrace"] = exception.StackTrace;
        }

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }

    /// <summary>
    /// Sets the activity status to error with the exception message.
    /// </summary>
    public static void SetErrorStatus(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.RecordException(exception);
    }
}
