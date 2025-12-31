namespace MyEmailSearch.Tests.Data;

using MyEmailSearch.Data;

public class SearchDatabaseEscapingTests
{
    [Test]
    public async Task EscapeFts5Query_WithSpecialCharacters_EscapesCorrectly()
    {
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
    public async Task EscapeFts5Query_WithEmptyString_ReturnsEmpty()
    {
        var result = SearchDatabase.EscapeFts5Query("");

        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task EscapeFts5Query_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.EscapeFts5Query(null!);

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
        // Users shouldn't be able to inject FTS5 operators like OR, AND, NOT
        var result = SearchDatabase.PrepareFts5MatchQuery("test OR hack");

        await Assert.That(result).IsEqualTo("\"test OR hack\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithParentheses_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("(test)");

        await Assert.That(result).IsEqualTo("\"(test)\"");
    }
}
