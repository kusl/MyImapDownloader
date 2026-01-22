using System.Threading.Tasks;

using AwesomeAssertions;

using MyEmailSearch.Search;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MyEmailSearch.Tests.Search;

public class QueryParserTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_SimpleText_SetsContentTerms()
    {
        var result = _parser.Parse("hello world");

        await Assert.That(result.ContentTerms).IsEqualTo("hello world");
    }

    [Test]
    public async Task Parse_FromFilter_SetsFromAddress()
    {
        var result = _parser.Parse("from:alice@example.com");

        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
    }

    [Test]
    public async Task Parse_ToFilter_SetsToAddress()
    {
        var result = _parser.Parse("to:bob@example.com");

        await Assert.That(result.ToAddress).IsEqualTo("bob@example.com");
    }

    [Test]
    public async Task Parse_SubjectFilter_SetsSubject()
    {
        var result = _parser.Parse("subject:meeting");

        await Assert.That(result.Subject).IsEqualTo("meeting");
    }

    [Test]
    public async Task Parse_QuotedSubject_PreservesSpaces()
    {
        var result = _parser.Parse("subject:\"project update\"");

        await Assert.That(result.Subject).IsEqualTo("project update");
    }

    [Test]
    public async Task Parse_DateRange_SetsDateFromAndTo()
    {
        var result = _parser.Parse("date:2024-01-01..2024-12-31");

        await Assert.That(result.DateFrom?.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom?.Month).IsEqualTo(1);
        await Assert.That(result.DateTo?.Year).IsEqualTo(2024);
        await Assert.That(result.DateTo?.Month).IsEqualTo(12);
    }

    [Test]
    public async Task Parse_AfterDate_SetsDateFrom()
    {
        var result = _parser.Parse("after:2024-06-01");

        await Assert.That(result.DateFrom?.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom?.Month).IsEqualTo(6);
    }

    [Test]
    public async Task Parse_BeforeDate_SetsDateTo()
    {
        var result = _parser.Parse("before:2024-06-30");

        await Assert.That(result.DateTo?.Year).IsEqualTo(2024);
        await Assert.That(result.DateTo?.Month).IsEqualTo(6);
    }

    [Test]
    public async Task Parse_FolderFilter_SetsFolder()
    {
        var result = _parser.Parse("folder:INBOX");

        await Assert.That(result.Folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task Parse_AccountFilter_SetsAccount()
    {
        var result = _parser.Parse("account:user@example.com");

        await Assert.That(result.Account).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task Parse_CombinedFilters_SetsAllFields()
    {
        var result = _parser.Parse("from:alice@example.com to:bob@example.com subject:meeting kafka");

        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.ToAddress).IsEqualTo("bob@example.com");
        await Assert.That(result.Subject).IsEqualTo("meeting");
        result.ContentTerms.Should().Contain("kafka");
    }

    [Test]
    public async Task Parse_EmptyQuery_ReturnsEmptySearchQuery()
    {
        var result = _parser.Parse("");

        await Assert.That(result.FromAddress).IsNull();
        await Assert.That(result.ToAddress).IsNull();
        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_CaseInsensitiveFilters_Works()
    {
        var result = _parser.Parse("FROM:alice@example.com SUBJECT:test");

        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.Subject).IsEqualTo("test");
    }
}
