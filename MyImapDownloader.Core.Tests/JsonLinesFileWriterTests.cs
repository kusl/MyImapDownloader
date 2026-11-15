using System.Text;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests;

public class JsonLinesFileWriterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    private JsonLinesFileWriter CreateWriter(long maxBytes = 1024) => new(_temp.Path, "traces", maxBytes, _time);

    private string PathOf(string date, int sequence) => Path.Combine(_temp.Path, $"traces_{date}_{sequence:D4}.jsonl");

    private static byte[] Line(string text) => Encoding.UTF8.GetBytes(text + "\n");

    [Test]
    public async Task Append_WritesToDatedFile()
    {
        var writer = CreateWriter();

        writer.Append(Line("{\"a\":1}"));
        writer.Append(Line("{\"a\":2}"));

        await Assert.That(await File.ReadAllTextAsync(PathOf("20260115", 0))).IsEqualTo("{\"a\":1}\n{\"a\":2}\n");
    }

    [Test]
    public async Task Append_EmptyPayloadCreatesNoFile()
    {
        var writer = CreateWriter();

        writer.Append([]);

        await Assert.That(Directory.GetFiles(_temp.Path).Length).IsEqualTo(0);
    }

    [Test]
    public async Task Append_RotatesWhenSizeLimitWouldBeExceeded()
    {
        var writer = CreateWriter(maxBytes: 20);

        writer.Append(Line("0123456789"));
        writer.Append(Line("abcdefghij"));

        await Assert.That(await File.ReadAllTextAsync(PathOf("20260115", 0))).IsEqualTo("0123456789\n");
        await Assert.That(await File.ReadAllTextAsync(PathOf("20260115", 1))).IsEqualTo("abcdefghij\n");
    }

    [Test]
    public async Task Append_OversizedPayloadStillGoesToAnEmptyFile()
    {
        var writer = CreateWriter(maxBytes: 4);

        writer.Append(Line("0123456789"));

        await Assert.That(File.Exists(PathOf("20260115", 0))).IsTrue();
    }

    [Test]
    public async Task Append_RotatesDailyAndRestartsSequence()
    {
        var writer = CreateWriter(maxBytes: 12);
        writer.Append(Line("0123456789"));
        writer.Append(Line("0123456789"));

        _time.Now = _time.Now.AddDays(1);
        writer.Append(Line("next"));

        await Assert.That(File.Exists(PathOf("20260115", 1))).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(PathOf("20260116", 0))).IsEqualTo("next\n");
    }

    [Test]
    public async Task Append_ContinuesAfterFilesLeftByEarlierRuns()
    {
        await File.WriteAllTextAsync(PathOf("20260115", 0), new string('x', 30));
        await File.WriteAllTextAsync(PathOf("20260115", 1), "old\n");
        var writer = CreateWriter(maxBytes: 20);

        writer.Append(Line("new"));

        await Assert.That(await File.ReadAllTextAsync(PathOf("20260115", 0))).IsEqualTo(new string('x', 30));
        await Assert.That(await File.ReadAllTextAsync(PathOf("20260115", 1))).IsEqualTo("old\nnew\n");
    }
}
