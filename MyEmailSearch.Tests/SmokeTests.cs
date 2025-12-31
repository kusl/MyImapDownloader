using MyEmailSearch.Search;

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
        var result = 1 + 1;
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Can_Create_QueryParser()
    {
        var parser = new QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task Can_Create_SnippetGenerator()
    {
        var generator = new SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }
}
