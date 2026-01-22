using System;
using System.Collections.Generic;
using System.IO;

namespace MyEmailSearch.Configuration;

/// <summary>
/// Resolves paths following XDG Base Directory Specification.
/// </summary>
public static class PathResolver
{
    private const string AppName = "myemailsearch";

    /// <summary>
    /// Gets the default archive path, checking environment and common locations.
    /// </summary>
    public static string GetDefaultArchivePath()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("MYIMAPDOWNLOADER_ARCHIVE");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // Check XDG_DATA_HOME
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            var xdgPath = Path.Combine(xdgDataHome, "myimapdownloader");
            if (Directory.Exists(xdgPath))
            {
                return xdgPath;
            }
        }

        // Check common locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            Path.Combine(home, ".local", "share", "myimapdownloader"),
            Path.Combine(home, "Documents", "mail"),
            Path.Combine(home, "mail"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mail")
        };

        foreach (var path in commonPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Default to XDG location even if it doesn't exist
        return Path.Combine(
            xdgDataHome ?? Path.Combine(home, ".local", "share"),
            "myimapdownloader");
    }

    /// <summary>
    /// Gets the default database path following XDG specification.
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dataDir = !string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(xdgDataHome, AppName)
            : Path.Combine(home, ".local", "share", AppName);

        return Path.Combine(dataDir, "search.db");
    }

    /// <summary>
    /// Gets the telemetry directory following XDG specification.
    /// </summary>
    public static string? GetTelemetryDirectory()
    {
        var candidates = GetCandidateDirectories("telemetry");

        foreach (var dir in candidates)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Test write access
                var testFile = Path.Combine(dir, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                return dir;
            }
            catch
            {
                // Try next candidate
            }
        }

        return null; // No writable location found
    }

    private static IEnumerable<string> GetCandidateDirectories(string subdir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // XDG_DATA_HOME
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            yield return Path.Combine(xdgDataHome, AppName, subdir);
        }

        // LocalApplicationData (works on Windows too)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, AppName, subdir);
        }

        // XDG_STATE_HOME
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome))
        {
            yield return Path.Combine(xdgStateHome, AppName, subdir);
        }

        // Fallbacks
        yield return Path.Combine(home, ".local", "state", AppName, subdir);
        yield return Path.Combine(home, ".local", "share", AppName, subdir);

        // Current directory as last resort
        yield return Path.Combine(Directory.GetCurrentDirectory(), subdir);
    }
}
