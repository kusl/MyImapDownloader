namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Resolves telemetry output directory following XDG Base Directory Specification.
/// </summary>
public static class TelemetryDirectoryResolver
{
    /// <summary>
    /// Attempts to resolve a writable telemetry directory.
    /// Returns null if no writable location can be found.
    /// </summary>
    public static string? ResolveTelemetryDirectory(string appName)
    {
        var candidates = GetCandidateDirectories(appName);

        foreach (var candidate in candidates)
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories(string appName)
    {
        var lowerAppName = appName.ToLowerInvariant();

        // 1. XDG_STATE_HOME (preferred for telemetry/logs)
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdgState))
        {
            yield return Path.Combine(xdgState, lowerAppName, "telemetry");
        }

        // 2. XDG_DATA_HOME
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgData))
        {
            yield return Path.Combine(xdgData, lowerAppName, "telemetry");
        }

        // 3. Platform-specific defaults
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, ".local", "state", lowerAppName, "telemetry");
            yield return Path.Combine(home, ".local", "share", lowerAppName, "telemetry");
        }
        else if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, appName, "telemetry");
        }

        // 4. Fallback to current directory
        yield return Path.Combine(Environment.CurrentDirectory, "telemetry");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
