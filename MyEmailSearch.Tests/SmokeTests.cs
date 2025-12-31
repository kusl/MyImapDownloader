namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify the project builds and runs.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task Application_ShouldCompileAndRun()
    {
        // This test simply verifies the project compiles
        // More comprehensive tests will be added as features are implemented
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task SearchCommand_ShouldExist()
    {
        // Verify the search command can be created
        var command = Commands.SearchCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("search");
    }

    [Test]
    public async Task IndexCommand_ShouldExist()
    {
        var command = Commands.IndexCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("index");
    }

    [Test]
    public async Task StatusCommand_ShouldExist()
    {
        var command = Commands.StatusCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("status");
    }

    [Test]
    public async Task RebuildCommand_ShouldExist()
    {
        var command = Commands.RebuildCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("rebuild");
    }
}
