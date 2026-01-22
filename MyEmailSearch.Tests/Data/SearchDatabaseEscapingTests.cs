namespace MyEmailSearch.Tests.Data;

using MyEmailSearch.Data;

public class SearchDatabaseEscapingTests
{
    [Test]
    public async Task EscapeFts5Query_WithSpecialCharacters_EscapesCorrectly()
    {
        // Input: test"query -> Output: "test""query"
        var result = SearchDatabase.EscapeFts5Query("test\"query");

        await Assert.That(result).IsEqualTo("\"test\"\"query\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNormalText_WrapsInQuotes()
    {
        var result = SearchDatabase.EscapeFts5Query("hello world");

        await Assert.That(result).IsEqualTo("\"hello world\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithEmptyString_ReturnsEmptyQuotes()
    {
        var result = SearchDatabase.EscapeFts5Query("");

        // Empty string wrapped in quotes
        await Assert.That(result).IsEqualTo("\"\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.EscapeFts5Query(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWhitespace_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("   ");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithSimpleText_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("search term");

        await Assert.That(result).IsEqualTo("\"search term\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("search*");

        await Assert.That(result).IsEqualTo("\"search\"*");
    }
}
