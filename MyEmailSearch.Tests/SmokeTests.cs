using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify core types compile and are accessible.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task CoreTypes_AreAccessible()
    {
        // Verify core types can be instantiated
        var parser = new QueryParser();
        var generator = new SnippetGenerator();
        
        await Assert.That(parser).IsNotNull();
        await Assert.That(generator).IsNotNull();
    }

    [Test]
    public async Task QueryParser_CanBeInstantiated()
    {
        var parser = new QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task SnippetGenerator_CanBeInstantiated()
    {
        var generator = new SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }

    [Test]
    public async Task SearchQuery_HasDefaultValues()
    {
        var query = new SearchQuery();
        await Assert.That(query.Take).IsEqualTo(100);
        await Assert.That(query.Skip).IsEqualTo(0);
    }

    [Test]
    public async Task EmailDocument_CanBeCreated()
    {
        var doc = new EmailDocument
        {
            MessageId = "test@example.com",
            FilePath = "/test/path.eml",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await Assert.That(doc.MessageId).IsEqualTo("test@example.com");
    }
}
