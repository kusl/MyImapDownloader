using System.Diagnostics;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Extension methods for System.Diagnostics.Activity.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Records an exception on the activity with full details.
    /// </summary>
    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity == null || exception == null) return;

        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message }
        };

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            tags.Add("exception.stacktrace", exception.StackTrace);
        }

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    /// <summary>
    /// Sets the activity status to OK.
    /// </summary>
    public static void SetSuccess(this Activity? activity, string? description = null)
    {
        activity?.SetStatus(ActivityStatusCode.Ok, description);
    }

    /// <summary>
    /// Sets the activity status to Error.
    /// </summary>
    public static void SetError(this Activity? activity, string? description = null)
    {
        activity?.SetStatus(ActivityStatusCode.Error, description);
    }

    /// <summary>
    /// Adds a tag if the value is not null or empty.
    /// </summary>
    public static Activity? SetTagIfNotEmpty(this Activity? activity, string key, string? value)
    {
        if (activity != null && !string.IsNullOrEmpty(value))
        {
            activity.SetTag(key, value);
        }
        return activity;
    }
}
