namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify the project builds and runs.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task Project_Builds_Successfully()
    {
        // This test passes if the project compiles
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Can_Create_QueryParser()
    {
        var parser = new Search.QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task Can_Create_SnippetGenerator()
    {
        var generator = new Search.SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }
}
