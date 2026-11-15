using System.Text.Json;

using MyEmailSearch.Commands;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests;

public class ResultFormatterTests
{
    private static SearchResultSet Results() => new()
    {
        Results =
        [
            new SearchResult
            {
                Email = Sample.Document("/mail/1.eml", subject: "Say \"hi\"", body: "line one"),
                Snippet = "line one\nline two",
                MatchedTerms = ["line"]
            }
        ],
        TotalCount = 3,
        Take = 1,
        QueryTime = TimeSpan.FromMilliseconds(12)
    };

    private static string Format(SearchResultSet results, string format)
    {
        var writer = new StringWriter { NewLine = "\n" };
        ResultFormatter.Write(writer, results, format);
        return writer.ToString();
    }

    [Test]
    public async Task Table_ListsResultsWithSnippetAndTotals()
    {
        var output = Format(Results(), "table");

        await Assert.That(output).Contains("Found 3 results (12ms):");
        await Assert.That(output).Contains("alice@example.com");
        await Assert.That(output).Contains("Say \"hi\"");
        await Assert.That(output).Contains("             line one line two");
        await Assert.That(output).Contains("Showing 1 of 3 results");
    }

    [Test]
    public async Task Table_UnknownFormatFallsBackToTable()
    {
        await Assert.That(Format(Results(), "other")).Contains("Showing 1 of 3 results");
    }

    [Test]
    public async Task Table_WithoutResultsSaysSo()
    {
        await Assert.That(Format(new SearchResultSet(), "table")).IsEqualTo("No results found.\n");
    }

    [Test]
    public async Task Csv_QuotesEveryFieldAndEscapesQuotes()
    {
        var lines = Format(Results(), "CSV").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).IsEqualTo("MessageId,From,Subject,Date,Folder,Account,FilePath");
        await Assert.That(lines[1]).IsEqualTo(
            "\"/mail/1.eml@example.com\",\"alice@example.com\",\"Say \"\"hi\"\"\",\"2023-11-14 22:13:20\",\"INBOX\",\"work\",\"/mail/1.eml\"");
    }

    [Test]
    public async Task Json_KeepsTheEstablishedShape()
    {
        using var document = JsonDocument.Parse(Format(Results(), "json"));
        var root = document.RootElement;
        var first = root.GetProperty("Results")[0];
        var email = first.GetProperty("Email");

        await Assert.That(root.GetProperty("TotalCount").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("Skip").GetInt32()).IsEqualTo(0);
        await Assert.That(root.GetProperty("Take").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("HasMore").GetBoolean()).IsTrue();
        await Assert.That(root.TryGetProperty("QueryTime", out _)).IsTrue();
        await Assert.That(first.GetProperty("Snippet").GetString()).IsEqualTo("line one\nline two");
        await Assert.That(first.GetProperty("MatchedTerms")[0].GetString()).IsEqualTo("line");
        await Assert.That(first.TryGetProperty("Score", out _)).IsTrue();
        await Assert.That(email.GetProperty("MessageId").GetString()).IsEqualTo("/mail/1.eml@example.com");
        await Assert.That(email.GetProperty("FilePath").GetString()).IsEqualTo("/mail/1.eml");
        await Assert.That(email.GetProperty("FromAddress").GetString()).IsEqualTo("alice@example.com");
        await Assert.That(email.GetProperty("ToAddressesJson").GetString()).IsEqualTo("[\"bob@example.com\"]");
        await Assert.That(email.GetProperty("ToAddresses")[0].GetString()).IsEqualTo("bob@example.com");
        await Assert.That(email.GetProperty("DateSentUnix").GetInt64()).IsEqualTo(1_700_000_000L);
        await Assert.That(email.TryGetProperty("DateSent", out _)).IsTrue();
        await Assert.That(email.GetProperty("Folder").GetString()).IsEqualTo("INBOX");
        await Assert.That(email.GetProperty("Account").GetString()).IsEqualTo("work");
    }

    [Test]
    public async Task Truncate_ShortensWithEllipsis()
    {
        await Assert.That(ResultFormatter.Truncate("abcdefghij", 8)).IsEqualTo("abcde...");
        await Assert.That(ResultFormatter.Truncate("abc", 8)).IsEqualTo("abc");
    }
}
