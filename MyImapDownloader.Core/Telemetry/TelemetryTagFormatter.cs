using System.Globalization;

namespace MyImapDownloader.Core.Telemetry;

internal static class TelemetryTagFormatter
{
    public static Dictionary<string, string?> ToStringDictionary(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>();
        foreach (var tag in tags)
        {
            result[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
        }
        return result;
    }
}
