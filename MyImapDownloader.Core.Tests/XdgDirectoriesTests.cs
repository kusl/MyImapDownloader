using MyImapDownloader.Core.Paths;

namespace MyImapDownloader.Core.Tests;

public class XdgDirectoriesTests
{
    private static XdgDirectories Create(params (string Name, string Value)[] variables) =>
        new(name => variables.FirstOrDefault(v => v.Name == name).Value, "/home/test");

    [Test]
    public async Task Defaults_FollowXdgSpecification()
    {
        var xdg = Create();

        await Assert.That(xdg.DataHome).IsEqualTo(Path.Combine("/home/test", ".local", "share"));
        await Assert.That(xdg.StateHome).IsEqualTo(Path.Combine("/home/test", ".local", "state"));
        await Assert.That(xdg.DataHomeOverride).IsNull();
        await Assert.That(xdg.StateHomeOverride).IsNull();
    }

    [Test]
    public async Task EnvironmentVariables_OverrideDefaults()
    {
        var xdg = Create(("XDG_DATA_HOME", "/data"), ("XDG_STATE_HOME", "/state"));

        await Assert.That(xdg.DataHome).IsEqualTo("/data");
        await Assert.That(xdg.StateHome).IsEqualTo("/state");
    }

    [Test]
    public async Task BlankVariables_AreIgnored()
    {
        var xdg = Create(("XDG_DATA_HOME", "  "));

        await Assert.That(xdg.DataHomeOverride).IsNull();
        await Assert.That(xdg.Variable("MISSING")).IsNull();
    }
}
