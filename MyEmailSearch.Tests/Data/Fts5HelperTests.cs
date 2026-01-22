using System.Threading.Tasks;

using MyEmailSearch.Data;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

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
