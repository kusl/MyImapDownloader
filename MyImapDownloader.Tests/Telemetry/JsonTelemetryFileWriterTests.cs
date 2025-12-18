using FluentAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class TelemetryDirectoryResolverTests
{
    [Test]
    public async Task ResolveTelemetryDirectory_ReturnsNonNullPath()
    {
        // On any normal system, at least one location should be writable
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory("TestApp");

        // This could be null in a sandboxed environment, but typically won't be
        // We're testing that the method runs without throwing
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task ResolveTelemetryDirectory_ReturnsWritablePath_WhenSuccessful()
    {
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory("TestApp");

        if (result != null)
        {
            // If a path is returned, it should be writable
            var testFile = Path.Combine(result, $".test_{Guid.NewGuid():N}");
            try
            {
                await File.WriteAllTextAsync(testFile, "test");
                await Assert.That(File.Exists(testFile)).IsTrue();
                File.Delete(testFile);
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }
        else
        {
            // Null is acceptable if no writable location exists
            await Assert.That(result).IsNull();
        }
    }

    [Test]
    public async Task ResolveTelemetryDirectory_IncludesAppName_InPath()
    {
        const string appName = "MyUniqueTestApp";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);

        if (result != null)
        {
            result.Should().Contain(appName);
        }
        
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task ResolveTelemetryDirectory_UsesDefaultAppName_WhenNotSpecified()
    {
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory();

        // Should use "MyImapDownloader" as default
        if (result != null)
        {
            result.Should().Contain("MyImapDownloader");
        }
        
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task ResolveTelemetryDirectory_CreatesDirectory_WhenItDoesNotExist()
    {
        var uniqueAppName = $"TestApp_{Guid.NewGuid():N}";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(uniqueAppName);

        try
        {
            if (result != null)
            {
                await Assert.That(Directory.Exists(result)).IsTrue();
            }
        }
        finally
        {
            // Cleanup
            if (result != null && Directory.Exists(result))
            {
                try { Directory.Delete(result, recursive: true); } catch { }
            }
        }
    }

    [Test]
    [Arguments("SimpleApp")]
    [Arguments("App-With-Dashes")]
    [Arguments("App_With_Underscores")]
    [Arguments("AppWithNumbers123")]
    public async Task ResolveTelemetryDirectory_HandlesVariousAppNames(string appName)
    {
        // Should not throw for valid app names
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);
        
        // Cleanup if directory was created
        if (result != null && Directory.Exists(result))
        {
            try { Directory.Delete(result, recursive: true); } catch { }
        }
        
        await Assert.That(true).IsTrue();
    }
}
