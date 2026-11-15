using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

public class QueryParserTests
{
    private static DateTimeOffset LocalDay(int year, int month, int day) =>
        new(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local));

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Parse_EmptyInputYieldsEmptyQuery(string? input)
    {
        var query = QueryParser.Parse(input);

        await Assert.That(query.ContentTerms).IsNull();
        await Assert.That(query.FromAddress).IsNull();
        await Assert.That(query.Take).IsEqualTo(100);
        await Assert.That(query.Skip).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_PlainTextBecomesContentTerms()
    {
        var query = QueryParser.Parse("  kafka cluster  ");

        await Assert.That(query.ContentTerms).IsEqualTo("kafka cluster");
    }

    [Test]
    public async Task Parse_ExtractsEveryField()
    {
        var query = QueryParser.Parse(
            "from:alice@example.com to:bob@example.com subject:report account:work folder:INBOX kafka");

        await Assert.That(query.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(query.ToAddress).IsEqualTo("bob@example.com");
        await Assert.That(query.Subject).IsEqualTo("report");
        await Assert.That(query.Account).IsEqualTo("work");
        await Assert.That(query.Folder).IsEqualTo("INBOX");
        await Assert.That(query.ContentTerms).IsEqualTo("kafka");
    }

    [Test]
    public async Task Parse_FieldNamesAreCaseInsensitive()
    {
        var query = QueryParser.Parse("FROM:Alice@Example.com");

        await Assert.That(query.FromAddress).IsEqualTo("Alice@Example.com");
        await Assert.That(query.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_QuotedValuesMayContainSpacesAndColons()
    {
        var query = QueryParser.Parse("subject:\"from:x project update\" folder:\"Sent Items\" budget");

        await Assert.That(query.Subject).IsEqualTo("from:x project update");
        await Assert.That(query.Folder).IsEqualTo("Sent Items");
        await Assert.That(query.FromAddress).IsNull();
        await Assert.That(query.ContentTerms).IsEqualTo("budget");
    }

    [Test]
    public async Task Parse_FieldPrefixInsideAWordIsContent()
    {
        var query = QueryParser.Parse("photo:xyz");

        await Assert.That(query.ToAddress).IsNull();
        await Assert.That(query.ContentTerms).IsEqualTo("photo:xyz");
    }

    [Test]
    public async Task Parse_RepeatedFieldKeepsFirstAndLeavesRestAsContent()
    {
        var query = QueryParser.Parse("from:a@example.com from:b@example.com");

        await Assert.That(query.FromAddress).IsEqualTo("a@example.com");
        await Assert.That(query.ContentTerms).IsEqualTo("from:b@example.com");
    }

    [Test]
    public async Task Parse_SingleDateStartsAtLocalMidnight()
    {
        var query = QueryParser.Parse("date:2025-01-01 invoice");

        await Assert.That(query.DateFrom).IsEqualTo(LocalDay(2025, 1, 1));
        await Assert.That(query.DateTo).IsNull();
        await Assert.That(query.ContentTerms).IsEqualTo("invoice");
    }

    [Test]
    public async Task Parse_DateRangeIncludesWholeEndDay()
    {
        var query = QueryParser.Parse("date:2025-01-01..2025-06-30");

        await Assert.That(query.DateFrom).IsEqualTo(LocalDay(2025, 1, 1));
        await Assert.That(query.DateTo).IsEqualTo(LocalDay(2025, 6, 30).AddDays(1).AddTicks(-1));
    }

    [Test]
    public async Task Parse_AfterAndBeforeBoundTheRange()
    {
        var query = QueryParser.Parse("after:2024-03-01 before:2024-03-31 trip");

        await Assert.That(query.DateFrom).IsEqualTo(LocalDay(2024, 3, 1));
        await Assert.That(query.DateTo).IsEqualTo(LocalDay(2024, 3, 31).AddDays(1).AddTicks(-1));
        await Assert.That(query.ContentTerms).IsEqualTo("trip");
    }

    [Test]
    [Arguments("after:notadate")]
    [Arguments("before:2024-13-45")]
    [Arguments("date:2024-01-01..oops")]
    public async Task Parse_InvalidDatesStayInContent(string input)
    {
        var query = QueryParser.Parse(input);

        await Assert.That(query.DateFrom).IsNull();
        await Assert.That(query.DateTo).IsNull();
        await Assert.That(query.ContentTerms).IsEqualTo(input);
    }
}
