using AwesomeAssertions;

using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class SnippetGeneratorTests
{
    private readonly SnippetGenerator _generator = new();

    [Test]
    public void Generate_FindsMatchingTerm()
    {
        const string text = "This is a test document with some important content.";
        var snippet = _generator.Generate(text, "important");

        snippet.Should().Contain("important");
    }

    [Test]
    public async Task Generate_ReturnsEmptyForNullText()
    {
        var snippet = _generator.Generate(null, "test");

        await Assert.That(snippet).IsEmpty();
    }

    [Test]
    public async Task Generate_ReturnsEmptyForNoTerms()
    {
        var text = "Some text here";
        var snippet = _generator.Generate(text, "");

        await Assert.That(snippet).IsNotNull();
    }

    [Test]
    public async Task Generate_TruncatesLongText()
    {
        var text = new string('a', 1000) + " important " + new string('b', 1000);
        var snippet = _generator.Generate(text, "important");

        snippet.Should().NotBeNullOrWhiteSpace();
        await Assert.That(snippet.Length).IsLessThanOrEqualTo(210); // Allow some margin
    }

    [Test]
    public void Generate_HandlesMultipleTerms()
    {
        var text = "The quick brown fox jumps over the lazy dog.";
        var snippet = _generator.Generate(text, "quick lazy");

        snippet.Should().NotBeEmpty();
    }
}
