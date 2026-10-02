using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

public class Fts5HelperTests
{
    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmptyString_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWhitespace_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("   ");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithOnlyWildcard_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("*");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test*");

        await Assert.That(result).IsEqualTo("\"test\"*");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithoutWildcard_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test query");

        await Assert.That(result).IsEqualTo("\"test query\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithFts5Operators_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test OR hack");

        await Assert.That(result).IsEqualTo("\"test OR hack\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithParentheses_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("(test)");

        await Assert.That(result).IsEqualTo("\"(test)\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmbeddedQuote_DoublesIt()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test\"query");

        await Assert.That(result).IsEqualTo("\"test\"\"query\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithQuotedPhraseAndWildcard_EscapesAndKeepsWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("\"exact phrase\"*");

        await Assert.That(result).IsEqualTo("\"\"\"exact phrase\"\"\"*");
    }

    [Test]
    public async Task QueryAsync_WithUnbalancedQuote_DoesNotThrowAndFindsMatch()
    {
        using var temp = new TempDirectory("fts_quote_test");
        await using var db = new SearchDatabase(
            Path.Combine(temp.Path, "search.db"),
            NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "quote@example.com",
            FilePath = "/test/quote.eml",
            Subject = "Quarterly report",
            BodyText = "the quarterly report is attached",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "quarterly\" report" });

        await Assert.That(results.Count).IsEqualTo(1);
    }
}
