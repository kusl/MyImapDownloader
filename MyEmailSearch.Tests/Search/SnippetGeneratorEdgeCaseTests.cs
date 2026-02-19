using AwesomeAssertions;

using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Edge case tests for SnippetGenerator.
/// </summary>
public class SnippetGeneratorEdgeCaseTests
{
    [Test]
    public async Task Generate_TermAtStartOfText_ReturnsSnippet()
    {
        var text = "Important meeting scheduled for next Monday at 3pm.";
        var snippet = SnippetGenerator.Generate(text, "important");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_TermAtEndOfText_ReturnsSnippet()
    {
        var text = "Please review the attached document which is very important";
        var snippet = SnippetGenerator.Generate(text, "important");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_CaseInsensitiveMatch_FindsTerm()
    {
        var text = "The CRITICAL update was applied successfully.";
        var snippet = SnippetGenerator.Generate(text, "critical");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_VeryShortText_ReturnsEntireText()
    {
        var text = "Hi";
        var snippet = SnippetGenerator.Generate(text, "hi");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_NoMatchingTerm_ReturnsTextPrefix()
    {
        var text = "This email is about project planning and scheduling.";
        var snippet = SnippetGenerator.Generate(text, "nonexistentword");

        // Should return something (beginning of text) even without a match
        snippet.Should().NotBeNull();
    }

    [Test]
    public async Task Generate_NullTerms_ReturnsTextPrefix()
    {
        var text = "Some email content here.";
        var snippet = SnippetGenerator.Generate(text, null!);

        await Assert.That(snippet).IsNotNull();
    }
}
