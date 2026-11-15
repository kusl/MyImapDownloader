using MyImapDownloader.Core.Paths;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests;

public class TelemetryDirectoriesTests
{
    [Test]
    public async Task Candidates_PreferStateThenDataOverrides()
    {
        var xdg = new XdgDirectories(
            name => name switch { "XDG_STATE_HOME" => "/state", "XDG_DATA_HOME" => "/data", _ => null },
            "/home/test");

        var candidates = TelemetryDirectories.Candidates("MyApp", xdg).ToList();

        await Assert.That(candidates[0]).IsEqualTo(Path.Combine("/state", "myapp", "telemetry"));
        await Assert.That(candidates[1]).IsEqualTo(Path.Combine("/data", "myapp", "telemetry"));
        await Assert.That(candidates[^1]).IsEqualTo(Path.Combine(Environment.CurrentDirectory, "telemetry"));
    }

    [Test]
    public async Task FirstWritable_SkipsUnusableCandidates()
    {
        using var temp = new TempDirectory();
        var blocker = Path.Combine(temp.Path, "file");
        await File.WriteAllTextAsync(blocker, "x");
        var usable = Path.Combine(temp.Path, "usable", "telemetry");

        var chosen = TelemetryDirectories.FirstWritable([Path.Combine(blocker, "telemetry"), usable]);

        await Assert.That(chosen).IsEqualTo(usable);
        await Assert.That(Directory.Exists(usable)).IsTrue();
        await Assert.That(Directory.GetFiles(usable).Length).IsEqualTo(0);
    }

    [Test]
    public async Task FirstWritable_ReturnsNullWhenNothingIsWritable()
    {
        using var temp = new TempDirectory();
        var blocker = Path.Combine(temp.Path, "file");
        await File.WriteAllTextAsync(blocker, "x");

        var chosen = TelemetryDirectories.FirstWritable([Path.Combine(blocker, "telemetry")]);

        await Assert.That(chosen).IsNull();
    }
}
