using System.CommandLine;
using System.Text.Json;

namespace MyEmailSearch.Tests;

public class SearchCliTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly List<string> _opened = [];

    public void Dispose() => _temp.Dispose();

    private string Archive => Path.Combine(_temp.Path, "archive");

    private string Database => Path.Combine(_temp.Path, "index", "search.db");

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string input, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var invocation = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            ProcessTerminationTimeout = null
        };

        string[] arguments = [.. args, "--archive", Archive, "--database", Database];
        var exitCode = await SearchCli.Create(new StringReader(input), _opened.Add)
            .Parse(arguments)
            .InvokeAsync(invocation);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Test]
    public async Task Status_WithoutIndexExplainsHowToCreateOne()
    {
        var (exitCode, output, _) = await RunAsync("", "status");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("No index exists yet");
        await Assert.That(File.Exists(Database)).IsFalse();
    }

    [Test]
    public async Task Search_WithoutIndexFails()
    {
        var (exitCode, _, error) = await RunAsync("", "search", "kafka");

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(error).Contains("No index exists");
    }

    [Test]
    public async Task IndexThenSearch_FindsArchivedEmailAsJson()
    {
        var path = Sample.WriteEmail(Archive, "work", "INBOX", "one", body: "The kafka cluster is healthy.");

        var (indexExit, indexOutput, _) = await RunAsync("", "index", "--content");
        var (searchExit, searchOutput, _) = await RunAsync("", "search", "from:alice@example.com kafka", "--format", "json");
        var (statusExit, statusOutput, _) = await RunAsync("", "status");

        using var document = JsonDocument.Parse(searchOutput);
        await Assert.That(indexExit).IsEqualTo(0);
        await Assert.That(indexOutput).Contains("New emails indexed: 1");
        await Assert.That(searchExit).IsEqualTo(0);
        await Assert.That(document.RootElement.GetProperty("TotalCount").GetInt32()).IsEqualTo(1);
        await Assert.That(document.RootElement.GetProperty("Results")[0].GetProperty("Email").GetProperty("FilePath").GetString())
            .IsEqualTo(path);
        await Assert.That(statusExit).IsEqualTo(0);
        await Assert.That(statusOutput).Contains("Total emails indexed: 1");
        await Assert.That(statusOutput).Contains("Database health:      OK");
    }

    [Test]
    public async Task Search_RejectsUnknownFormat()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one");
        await RunAsync("", "index");

        var (exitCode, _, _) = await RunAsync("", "search", "kafka", "--format", "xml");

        await Assert.That(exitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task Search_OpenLaunchesTheChosenEmail()
    {
        var path = Sample.WriteEmail(Archive, "work", "INBOX", "one");
        await RunAsync("", "index");

        var (exitCode, output, _) = await RunAsync("1\n", "search", "account:work", "--open");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("Opening:");
        await Assert.That(_opened.Count).IsEqualTo(1);
        await Assert.That(_opened[0]).IsEqualTo(path);
    }

    [Test]
    public async Task Rebuild_WithoutConfirmationDoesNothing()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one");

        var (exitCode, output, _) = await RunAsync("n\n", "rebuild");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("Cancelled.");
        await Assert.That(File.Exists(Database)).IsFalse();
    }

    [Test]
    public async Task Rebuild_WithYesReindexesEverything()
    {
        Sample.WriteEmail(Archive, "work", "INBOX", "one");
        await RunAsync("", "index");
        Sample.WriteEmail(Archive, "work", "INBOX", "two");

        var (exitCode, output, _) = await RunAsync("", "rebuild", "--yes");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("Indexed: 2");
    }
}
