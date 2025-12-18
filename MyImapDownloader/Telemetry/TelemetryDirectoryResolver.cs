namespace MyImapDownloader.Telemetry;

/// <summary>
/// Resolves telemetry output directory following XDG Base Directory Specification
/// with graceful fallback behavior.
/// </summary>
public static class TelemetryDirectoryResolver
{
    /// <summary>
    /// Attempts to resolve a writable telemetry directory.
    /// Returns null if no writable location can be found.
    /// </summary>
    public static string? ResolveTelemetryDirectory(string appName = "MyImapDownloader")
    {
        // Try locations in order of preference
        var candidates = GetCandidateDirectories(appName);
        
        foreach (var candidate in candidates)
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }
        
        return null; // No writable location found - telemetry will be disabled
    }

    private static IEnumerable<string> GetCandidateDirectories(string appName)
    {
        // 1. XDG_DATA_HOME (Linux/macOS) or LocalApplicationData (Windows)
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
        {
            yield return Path.Combine(xdgDataHome, appName, "telemetry");
        }
        
        // 2. Platform-specific user data directory
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, appName, "telemetry");
        }
        
        // 3. XDG_STATE_HOME for state/log data (more appropriate for telemetry)
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdgStateHome))
        {
            yield return Path.Combine(xdgStateHome, appName, "telemetry");
        }
        
        // 4. Fallback to ~/.local/state on Unix-like systems
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(homeDir))
        {
            yield return Path.Combine(homeDir, ".local", "state", appName, "telemetry");
            yield return Path.Combine(homeDir, ".local", "share", appName, "telemetry");
        }
        
        // 5. Directory relative to executable
        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(exeDir))
        {
            yield return Path.Combine(exeDir, "telemetry");
        }
        
        // 6. Current working directory as last resort
        yield return Path.Combine(Environment.CurrentDirectory, "telemetry");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            // Attempt to create the directory
            Directory.CreateDirectory(path);
            
            // Verify we can write to it
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
