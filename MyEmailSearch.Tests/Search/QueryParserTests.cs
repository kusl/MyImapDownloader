using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class QueryParserTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_SimpleFromQuery_ExtractsFromAddress()
    {
        var query = _parser.Parse("from:alice@example.com");

        await Assert.That(query.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(query.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_QuotedSubject_ExtractsSubject()
    {
        var query = _parser.Parse("subject:\"project update\"");

        await Assert.That(query.Subject).IsEqualTo("project update");
    }

    [Test]
    public async Task Parse_DateRange_ParsesBothDates()
    {
        var query = _parser.Parse("date:2024-01-01..2024-12-31");

        await Assert.That(query.DateFrom).IsNotNull();
        await Assert.That(query.DateFrom!.Value.Year).IsEqualTo(2024);
        await Assert.That(query.DateFrom!.Value.Month).IsEqualTo(1);
        await Assert.That(query.DateTo).IsNotNull();
        await Assert.That(query.DateTo!.Value.Year).IsEqualTo(2024);
        await Assert.That(query.DateTo!.Value.Month).IsEqualTo(12);
    }

    [Test]
    public async Task Parse_MixedQuery_ExtractsAllParts()
    {
        var query = _parser.Parse("from:alice@example.com subject:report kafka streaming");

        await Assert.That(query.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(query.Subject).IsEqualTo("report");
        await Assert.That(query.ContentTerms).IsEqualTo("kafka streaming");
    }

    [Test]
    public async Task Parse_WildcardFrom_PreservesWildcard()
    {
        var query = _parser.Parse("from:*@example.com");

        await Assert.That(query.FromAddress).IsEqualTo("*@example.com");
    }

    [Test]
    public async Task Parse_EmptyString_ReturnsEmptyQuery()
    {
        var query = _parser.Parse("");

        await Assert.That(query.FromAddress).IsNull();
        await Assert.That(query.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_ContentOnly_ExtractsContentTerms()
    {
        var query = _parser.Parse("kafka streaming message broker");

        await Assert.That(query.ContentTerms).IsEqualTo("kafka streaming message broker");
        await Assert.That(query.FromAddress).IsNull();
    }

    [Test]
    public async Task Parse_ToAddress_ExtractsToAddress()
    {
        var query = _parser.Parse("to:bob@example.com");

        await Assert.That(query.ToAddress).IsEqualTo("bob@example.com");
    }

    [Test]
    public async Task Parse_AccountFilter_ExtractsAccount()
    {
        var query = _parser.Parse("account:work_backup");

        await Assert.That(query.Account).IsEqualTo("work_backup");
    }

    [Test]
    public async Task Parse_FolderFilter_ExtractsFolder()
    {
        var query = _parser.Parse("folder:INBOX");

        await Assert.That(query.Folder).IsEqualTo("INBOX");
    }
}
