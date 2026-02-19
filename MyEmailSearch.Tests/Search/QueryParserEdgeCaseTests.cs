using AwesomeAssertions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Tests for QueryParser edge cases and combined filter scenarios.
/// </summary>
public class QueryParserEdgeCaseTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_CombinedFilters_ExtractsAllFields()
    {
        var result = _parser.Parse("from:alice@example.com subject:meeting quarterly report");

        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.Subject).IsEqualTo("meeting");
        result.ContentTerms.Should().Contain("quarterly");
        result.ContentTerms.Should().Contain("report");
    }

    [Test]
    public async Task Parse_AccountFilter_SetsAccount()
    {
        var result = _parser.Parse("account:work");

        await Assert.That(result.Account).IsEqualTo("work");
    }

    [Test]
    public async Task Parse_FolderFilter_SetsFolder()
    {
        var result = _parser.Parse("folder:INBOX");

        await Assert.That(result.Folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task Parse_AllFiltersAtOnce_ExtractsEverything()
    {
        var query = "from:alice@x.com to:bob@x.com subject:hello account:work folder:Sent after:2024-01-01 before:2024-12-31 free text";
        var result = _parser.Parse(query);

        await Assert.That(result.FromAddress).IsEqualTo("alice@x.com");
        await Assert.That(result.ToAddress).IsEqualTo("bob@x.com");
        await Assert.That(result.Subject).IsEqualTo("hello");
        await Assert.That(result.Account).IsEqualTo("work");
        await Assert.That(result.Folder).IsEqualTo("Sent");
        await Assert.That(result.DateFrom).IsNotNull();
        await Assert.That(result.DateTo).IsNotNull();
        result.ContentTerms.Should().Contain("free text");
    }

    [Test]
    public async Task Parse_EmptyString_ReturnsEmptyQuery()
    {
        var result = _parser.Parse("");

        await Assert.That(result.FromAddress).IsNull();
        await Assert.That(result.ToAddress).IsNull();
        await Assert.That(result.Subject).IsNull();
        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_WhitespaceOnly_ReturnsEmptyQuery()
    {
        var result = _parser.Parse("   ");

        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_QuotedFromAddress_PreservesQuotedValue()
    {
        var result = _parser.Parse("from:\"alice smith@example.com\"");

        await Assert.That(result.FromAddress).IsEqualTo("alice smith@example.com");
    }

    [Test]
    public async Task Parse_SingleDateWithoutRange_SetsDateFrom()
    {
        var result = _parser.Parse("date:2024-06-15");

        await Assert.That(result.DateFrom).IsNotNull();
        await Assert.That(result.DateFrom!.Value.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom!.Value.Month).IsEqualTo(6);
        await Assert.That(result.DateFrom!.Value.Day).IsEqualTo(15);
    }

    [Test]
    public async Task Parse_DefaultPagination_HasCorrectValues()
    {
        var result = _parser.Parse("test");

        await Assert.That(result.Skip).IsEqualTo(0);
        await Assert.That(result.Take).IsEqualTo(100);
        await Assert.That(result.SortOrder).IsEqualTo(SearchSortOrder.DateDescending);
    }

    [Test]
    public async Task Parse_InvalidDate_IgnoresDateFilter()
    {
        var result = _parser.Parse("after:not-a-date some text");

        await Assert.That(result.DateFrom).IsNull();
        result.ContentTerms.Should().Contain("some text");
    }
}
