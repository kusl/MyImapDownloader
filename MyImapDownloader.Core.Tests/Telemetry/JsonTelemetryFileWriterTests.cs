using MyImapDownloader.Core.Infrastructure;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class JsonTelemetryFileWriterTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("writer_test");
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private JsonTelemetryFileWriter CreateWriter(
        string? subDir = null,
        string prefix = "test",
        long maxSize = 1024 * 1024)
    {
        var dir = subDir != null
            ? Path.Combine(_temp.Path, subDir)
            : _temp.Path;
        Directory.CreateDirectory(dir);

        var writer = new JsonTelemetryFileWriter(
            dir, prefix, maxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    [Test]
    public async Task Enqueue_CreatesFile_AfterFlush()
    {
        var writer = CreateWriter("enqueue_test");

        writer.Enqueue(new { Message = "test" });
        await writer.FlushAsync();

        var files = Directory.GetFiles(_temp.Path, "*.jsonl", SearchOption.AllDirectories);
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Enqueue_WritesJsonLines()
    {
        var writer = CreateWriter("jsonl_test");

        writer.Enqueue(new { Id = 1, Name = "First" });
        writer.Enqueue(new { Id = 2, Name = "Second" });
        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "jsonl_test"), "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("\"Id\":1");
        await Assert.That(lines[1]).Contains("\"Id\":2");
    }

    [Test]
    public async Task Writer_RotatesFile_WhenSizeExceeded()
    {
        var writer = CreateWriter("rotate_test", maxSize: 100);

        // Write enough data to trigger rotation
        for (int i = 0; i < 10; i++)
        {
            writer.Enqueue(new { Index = i, Data = new string('x', 50) });
            await writer.FlushAsync();
        }

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "rotate_test"), "*.jsonl");
        await Assert.That(files.Length).IsGreaterThan(1);
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        var subDir = Path.Combine(_temp.Path, "dispose_test");
        Directory.CreateDirectory(subDir);

        var writer = new JsonTelemetryFileWriter(
            subDir, "test", 1024 * 1024, TimeSpan.FromMinutes(5));

        writer.Enqueue(new { FinalRecord = true });
        writer.Dispose();

        await Task.Delay(100);

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }
}
