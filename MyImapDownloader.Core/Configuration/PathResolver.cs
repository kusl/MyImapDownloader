namespace MyImapDownloader.Core.Configuration;

/// <summary>
/// Resolves paths following XDG Base Directory Specification.
/// Provides consistent cross-platform path resolution for all applications.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Gets the XDG data home directory.
    /// </summary>
    public static string GetDataHome(string appName)
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        }

        return Path.Combine(home, ".local", "share", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Gets the XDG config home directory.
    /// </summary>
    public static string GetConfigHome(string appName)
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName);
        }

        return Path.Combine(home, ".config", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Gets the XDG state home directory (for logs, telemetry, etc.).
    /// </summary>
    public static string GetStateHome(string appName)
    {
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome))
        {
            return Path.Combine(xdgStateHome, appName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName,
                "State");
        }

        return Path.Combine(home, ".local", "state", appName.ToLowerInvariant());
    }

    /// <summary>
    /// Finds the first existing path from a list of candidates.
    /// </summary>
    public static string? FindFirstExisting(params string[] candidates)
    {
        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Ensures a directory exists and is writable.
    /// </summary>
    public static bool EnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
