using System.CommandLine;

namespace MyImapDownloader.Tests;

public class DownloadCommandTests
{
    private static async Task<(int ExitCode, DownloadOptions? Options)> InvokeAsync(int actionExitCode, params string[] args)
    {
        DownloadOptions? captured = null;
        var command = DownloadCommand.Create((options, _) =>
        {
            captured = options;
            return Task.FromResult(actionExitCode);
        });

        var invocation = new InvocationConfiguration
        {
            Output = TextWriter.Null,
            Error = TextWriter.Null,
            ProcessTerminationTimeout = null
        };

        var exitCode = await DownloadCommand.Parse(command, args).InvokeAsync(invocation);
        return (exitCode, captured);
    }

    [Test]
    public async Task LongOptions_PopulateDownloadOptions()
    {
        var (exitCode, options) = await InvokeAsync(0,
            "--server", "imap.example.com",
            "--username", "user@example.com",
            "--password", "secret",
            "--port", "143",
            "--output", "/tmp/archive",
            "--start-date", "2024-01-15",
            "--end-date", "2024-02-01",
            "--all-folders",
            "--verbose");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(options).IsNotNull();
        await Assert.That(options!.Server).IsEqualTo("imap.example.com");
        await Assert.That(options.Username).IsEqualTo("user@example.com");
        await Assert.That(options.Password).IsEqualTo("secret");
        await Assert.That(options.Port).IsEqualTo(143);
        await Assert.That(options.OutputDirectory).IsEqualTo("/tmp/archive");
        await Assert.That(options.StartDate).IsEqualTo(new DateTime(2024, 1, 15));
        await Assert.That(options.EndDate).IsEqualTo(new DateTime(2024, 2, 1));
        await Assert.That(options.AllFolders).IsTrue();
        await Assert.That(options.Verbose).IsTrue();
    }

    [Test]
    public async Task ShortAliases_PopulateDownloadOptions()
    {
        var (exitCode, options) = await InvokeAsync(0,
            "-s", "imap.example.com", "-u", "me", "-p", "pw", "-r", "1143", "-o", "out", "-a", "-v");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(options).IsNotNull();
        await Assert.That(options!.Port).IsEqualTo(1143);
        await Assert.That(options.OutputDirectory).IsEqualTo("out");
        await Assert.That(options.AllFolders).IsTrue();
        await Assert.That(options.Verbose).IsTrue();
    }

    [Test]
    public async Task OptionalValues_UseDefaults()
    {
        var (_, options) = await InvokeAsync(0, "-s", "imap.example.com", "-u", "me", "-p", "pw");

        await Assert.That(options).IsNotNull();
        await Assert.That(options!.Port).IsEqualTo(993);
        await Assert.That(options.OutputDirectory).IsEqualTo("EmailArchive");
        await Assert.That(options.StartDate).IsNull();
        await Assert.That(options.EndDate).IsNull();
        await Assert.That(options.AllFolders).IsFalse();
        await Assert.That(options.Verbose).IsFalse();
    }

    [Test]
    public async Task MissingRequiredOption_FailsWithoutInvokingAction()
    {
        var (exitCode, options) = await InvokeAsync(0, "-s", "imap.example.com", "-u", "me");

        await Assert.That(exitCode).IsNotEqualTo(0);
        await Assert.That(options).IsNull();
    }

    [Test]
    public async Task PasswordStartingWithAt_IsNotTreatedAsResponseFile()
    {
        var (_, options) = await InvokeAsync(0, "-s", "imap.example.com", "-u", "me", "-p", "@secret");

        await Assert.That(options).IsNotNull();
        await Assert.That(options!.Password).IsEqualTo("@secret");
    }

    [Test]
    public async Task ActionExitCode_IsPropagated()
    {
        var (exitCode, _) = await InvokeAsync(7, "-s", "imap.example.com", "-u", "me", "-p", "pw");

        await Assert.That(exitCode).IsEqualTo(7);
    }
}
