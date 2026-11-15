using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

public class SnippetGeneratorTests
{
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Generate_WithoutBodyReturnsNull(string? body)
    {
        await Assert.That(SnippetGenerator.Generate(body, "kafka")).IsNull();
    }

    [Test]
    public async Task Generate_ShortBodyIsReturnedWhole()
    {
        await Assert.That(SnippetGenerator.Generate("The kafka cluster is healthy.", "kafka"))
            .IsEqualTo("The kafka cluster is healthy.");
    }

    [Test]
    public async Task Generate_WithoutTermsTruncatesAtWordBoundary()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 100));

        var snippet = SnippetGenerator.Generate(body, null);

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.EndsWith("word...")).IsTrue();
        await Assert.That(snippet.Length).IsLessThanOrEqualTo(203);
    }

    [Test]
    public async Task Generate_CentersOnFirstMatchIgnoringCase()
    {
        var body = string.Join(' ', Enumerable.Repeat("filler", 60)) + " KAFKA outage " +
            string.Join(' ', Enumerable.Repeat("trailer", 60));

        var snippet = SnippetGenerator.Generate(body, "missing kafka") ?? "";

        await Assert.That(snippet).Contains("KAFKA outage");
        await Assert.That(snippet.StartsWith("...")).IsTrue();
        await Assert.That(snippet.EndsWith("...")).IsTrue();
        await Assert.That(snippet.Length).IsLessThanOrEqualTo(220);
    }

    [Test]
    public async Task Generate_UnmatchedTermsFallBackToLeadingText()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 100));

        var snippet = SnippetGenerator.Generate(body, "absent");

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.StartsWith("word word")).IsTrue();
    }
}
