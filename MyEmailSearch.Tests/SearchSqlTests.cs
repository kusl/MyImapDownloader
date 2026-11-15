using MyEmailSearch.Data;

namespace MyEmailSearch.Tests;

public class SearchSqlTests
{
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("*")]
    public async Task FtsMatch_WithoutTermsIsNull(string? terms)
    {
        await Assert.That(SearchSql.FtsMatch(terms)).IsNull();
    }

    [Test]
    [Arguments("kafka", "\"kafka\"")]
    [Arguments("  hello world  ", "\"hello world\"")]
    [Arguments("kaf*", "\"kaf\"*")]
    [Arguments("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [Arguments("a OR b", "\"a OR b\"")]
    [Arguments("col:value", "\"col:value\"")]
    public async Task FtsMatch_QuotesTermsAsOnePhrase(string terms, string expected)
    {
        await Assert.That(SearchSql.FtsMatch(terms)).IsEqualTo(expected);
    }

    [Test]
    public async Task Select_WithoutContentOrdersByDate()
    {
        var statement = SearchSql.Select(new SearchQuery { Account = "work", Take = 5, Skip = 10 });

        await Assert.That(statement.Text).Contains("WHERE emails.account = @account");
        await Assert.That(statement.Text).Contains("ORDER BY emails.date_sent_unix DESC");
        await Assert.That(statement.Text).DoesNotContain("emails_fts");
        await Assert.That(statement.Parameters.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Select_WithContentJoinsFullTextIndexAndRanks()
    {
        var statement = SearchSql.Select(new SearchQuery { ContentTerms = "kafka" });

        await Assert.That(statement.Text).Contains("emails_fts MATCH @ftsQuery");
        await Assert.That(statement.Text).Contains("ORDER BY bm25(emails_fts)");
    }

    [Test]
    public async Task Count_HasNoPagingParameters()
    {
        var statement = SearchSql.Count(new SearchQuery { Subject = "report" });

        await Assert.That(statement.Text).IsEqualTo("SELECT COUNT(*) FROM emails WHERE emails.subject LIKE @subject;");
        await Assert.That(statement.Parameters.Count).IsEqualTo(1);
    }
}
