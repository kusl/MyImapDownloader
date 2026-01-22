using System.Text.Json;

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
    public async Task Constructor_CreatesDirectory_WhenItDoesNotExist()
    {
        var newDir = Path.Combine(_temp.Path, "new_subdir");

        var writer = new JsonTelemetryFileWriter(newDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);

        await Assert.That(Directory.Exists(newDir)).IsTrue();
    }

    [Test]
    public async Task Enqueue_DoesNotThrow_WhenCalled()
    {
        var writer = CreateWriter();
        var record = new { Message = "Test", Timestamp = DateTime.UtcNow };

        // Should not throw
        writer.Enqueue(record);

        await Assert.That(writer).IsNotNull();
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

        writer.Enqueue(new TestRecord { Id = 1, Name = "First" });
        writer.Enqueue(new TestRecord { Id = 2, Name = "Second" });
        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "jsonl_test"), "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("\"Id\":1");
        await Assert.That(lines[1]).Contains("\"Id\":2");
    }

    [Test]
    public async Task FlushAsync_WritesEnqueuedRecords_ToFile()
    {
        var writer = CreateWriter("flush_test");

        var record = new TestRecord { Id = 1, Name = "Hello" };
        writer.Enqueue(record);

        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "flush_test"), "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        var content = await File.ReadAllTextAsync(files[0]);
        await Assert.That(content).Contains("Hello");
        await Assert.That(content).Contains("\"Id\":1");
    }

    [Test]
    public async Task FlushAsync_WritesMultipleRecords_InJsonlFormat()
    {
        var writer = CreateWriter("multi_test");

        writer.Enqueue(new TestRecord { Id = 1, Name = "First" });
        writer.Enqueue(new TestRecord { Id = 2, Name = "Second" });
        writer.Enqueue(new TestRecord { Id = 3, Name = "Third" });

        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "multi_test"), "*.jsonl");
        var lines = await File.ReadAllLinesAsync(files[0]);

        // Each record should be on its own line (JSONL format)
        await Assert.That(lines.Length).IsEqualTo(3);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            var parsed = JsonSerializer.Deserialize<TestRecord>(line);
            await Assert.That(parsed).IsNotNull();
        }
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
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        var writer = new JsonTelemetryFileWriter(
            Path.Combine(_temp.Path, "dispose_multi"),
            "test",
            1024 * 1024,
            TimeSpan.FromSeconds(30));

        // Should not throw when called multiple times
        writer.Dispose();
        writer.Dispose();
        writer.Dispose();

        await Assert.That(writer).IsNotNull();
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        var subDir = Path.Combine(_temp.Path, "dispose_flush");
        Directory.CreateDirectory(subDir);

        // Create writer - NOT tracked by _writers to control disposal manually
        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));

        // Enqueue a record
        writer.Enqueue(new { FinalRecord = true, Message = "This should be flushed on dispose" });

        // Dispose should flush the remaining records
        writer.Dispose();

        // Small delay for any file system operations
        await Task.Delay(50);

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        // Verify the content was actually written
        var content = await File.ReadAllTextAsync(files[0]);
        await Assert.That(content).Contains("FinalRecord");
    }

    [Test]
    public async Task FlushAsync_WithEmptyBuffer_DoesNotThrow()
    {
        var writer = CreateWriter("empty_flush");

        // Flush with nothing enqueued should not throw
        await writer.FlushAsync();

        await Assert.That(writer).IsNotNull();
    }

    private record TestRecord
    {
        public int Id { get; init; }
        public string? Name { get; init; }
    }
}
