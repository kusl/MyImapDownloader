using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class SnippetGeneratorTests
{
    private readonly SnippetGenerator _generator = new();

    [Test]
    public async Task Generate_WithMatchingTerm_ReturnsContextAroundMatch()
    {
        var bodyText = "This is a long email body that contains the word kafka somewhere in the middle of the text.";
        var searchTerms = "kafka";

        var snippet = _generator.Generate(bodyText, searchTerms);

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.Contains("kafka", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Generate_WithNoMatch_ReturnsBeginningOfText()
    {
        var bodyText = "This is a long email body without any matching terms.";
        var searchTerms = "nonexistent";

        var snippet = _generator.Generate(bodyText, searchTerms);

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.StartsWith("This")).IsTrue();
    }

    [Test]
    public async Task Generate_WithNullBody_ReturnsNull()
    {
        var snippet = _generator.Generate(null, "test");

        await Assert.That(snippet).IsNull();
    }

    [Test]
    public async Task Generate_WithEmptySearchTerms_ReturnsTruncatedBody()
    {
        var bodyText = "This is a test email body.";

        var snippet = _generator.Generate(bodyText, null);

        await Assert.That(snippet).IsEqualTo(bodyText);
    }
}
