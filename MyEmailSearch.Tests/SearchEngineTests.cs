using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

public class SearchEngineTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        _temp.Dispose();
    }

    private async Task<SearchEngine> CreateAsync(params EmailDocument[] documents)
    {
        _database = await SearchDatabase.OpenAsync(Path.Combine(_temp.Path, "search.db"));
        await _database.UpsertAsync(documents);
        return new SearchEngine(_database, NullLogger<SearchEngine>.Instance);
    }

    [Test]
    public async Task SearchAsync_BlankQueryReturnsNothing()
    {
        var engine = await CreateAsync(Sample.Document("/mail/1.eml"));

        var results = await engine.SearchAsync("   ", limit: 10, offset: 5);

        await Assert.That(results.TotalCount).IsEqualTo(0);
        await Assert.That(results.Results.Count).IsEqualTo(0);
        await Assert.That(results.Take).IsEqualTo(10);
        await Assert.That(results.Skip).IsEqualTo(5);
    }

    [Test]
    public async Task SearchAsync_CombinesFieldFiltersWithFullText()
    {
        var engine = await CreateAsync(
            Sample.Document("/mail/1.eml", from: "alice@example.com", subject: "Report", body: "kafka outage review"),
            Sample.Document("/mail/2.eml", from: "bob@example.com", subject: "Report", body: "kafka upgrade"),
            Sample.Document("/mail/3.eml", from: "alice@example.com", subject: "Lunch", body: "sandwiches"));

        var results = await engine.SearchAsync("from:alice@example.com kafka");

        await Assert.That(results.TotalCount).IsEqualTo(1);
        await Assert.That(results.Results[0].Email.FilePath).IsEqualTo("/mail/1.eml");
        await Assert.That(results.Results[0].Snippet).IsEqualTo("kafka outage review");
        await Assert.That(results.Results[0].MatchedTerms.Count).IsEqualTo(1);
        await Assert.That(results.Results[0].MatchedTerms[0]).IsEqualTo("kafka");
        await Assert.That(results.HasMore).IsFalse();
    }

    [Test]
    public async Task SearchAsync_ReportsTotalBeyondThePage()
    {
        var engine = await CreateAsync(
            Enumerable.Range(1, 5).Select(i => Sample.Document($"/mail/{i}.eml", dateSentUnix: i)).ToArray());

        var results = await engine.SearchAsync("account:work", limit: 2);

        await Assert.That(results.Results.Count).IsEqualTo(2);
        await Assert.That(results.TotalCount).IsEqualTo(5);
        await Assert.That(results.HasMore).IsTrue();
        await Assert.That(results.Results[0].Email.FilePath).IsEqualTo("/mail/5.eml");
    }

    [Test]
    public async Task SearchAsync_WithoutContentTermsUsesPreviewAsSnippet()
    {
        var engine = await CreateAsync(Sample.Document("/mail/1.eml", body: "preview text"));

        var results = await engine.SearchAsync("folder:INBOX");

        await Assert.That(results.Results[0].Snippet).IsEqualTo("preview text");
        await Assert.That(results.Results[0].MatchedTerms.Count).IsEqualTo(0);
    }
}
