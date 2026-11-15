using System.Diagnostics;

namespace MyImapDownloader.Core.Telemetry;

public static class ActivityExtensions
{
    extension(Activity? activity)
    {
        public void RecordError(Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
    }
}
