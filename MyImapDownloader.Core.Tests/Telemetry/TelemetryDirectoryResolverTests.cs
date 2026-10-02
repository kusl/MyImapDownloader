using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class TelemetryDirectoryResolverTests
{
    [Test]
    public async Task ResolveTelemetryDirectory_ReturnsExistingDirectory()
    {
        var appName = $"ResolverTest_{Guid.NewGuid():N}";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);

        try
        {
            await Assert.That(result).IsNotNull();
            await Assert.That(Directory.Exists(result!)).IsTrue();
        }
        finally
        {
            DeleteAppDirectory(result, appName);
        }
    }

    [Test]
    public async Task ResolveTelemetryDirectory_UsesLowercaseAppNameAndTelemetryLeaf()
    {
        var appName = $"ResolverCase_{Guid.NewGuid():N}";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);

        try
        {
            await Assert.That(result).IsNotNull();
            await Assert.That(Path.GetFileName(result!)).IsEqualTo("telemetry");

            if (!OperatingSystem.IsWindows())
            {
                await Assert.That(result!).Contains(appName.ToLowerInvariant());
            }
        }
        finally
        {
            DeleteAppDirectory(result, appName);
        }
    }

    private static void DeleteAppDirectory(string? telemetryDirectory, string appName)
    {
        if (telemetryDirectory == null) return;

        try
        {
            var appDirectory = Path.GetDirectoryName(telemetryDirectory);
            if (appDirectory != null &&
                string.Equals(Path.GetFileName(appDirectory), appName, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(appDirectory))
            {
                Directory.Delete(appDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
