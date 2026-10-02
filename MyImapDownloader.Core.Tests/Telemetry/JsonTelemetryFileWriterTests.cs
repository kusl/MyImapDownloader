using System.Text.Json;
using System.Text.Json.Serialization;

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

        var writer = new JsonTelemetryFileWriter(dir, prefix, maxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    private static WriterTestRecord Record(int id, string name) => new() { Id = id, Name = name };

    [Test]
    public async Task Constructor_CreatesDirectory_WhenItDoesNotExist()
    {
        var newDir = Path.Combine(_temp.Path, "new_subdir");

        var writer = new JsonTelemetryFileWriter(newDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);

        await Assert.That(Directory.Exists(newDir)).IsTrue();
    }

    [Test]
    public async Task Enqueue_CreatesFile_AfterFlush()
    {
        var writer = CreateWriter("enqueue_test");

        writer.Enqueue(Record(1, "test"), WriterTestJsonContext.Default.WriterTestRecord);
        await writer.FlushAsync();

        var files = Directory.GetFiles(_temp.Path, "*.jsonl", SearchOption.AllDirectories);
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Enqueue_WritesJsonLines()
    {
        var writer = CreateWriter("jsonl_test");

        writer.Enqueue(Record(1, "First"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(2, "Second"), WriterTestJsonContext.Default.WriterTestRecord);
        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "jsonl_test"), "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("\"Id\":1");
        await Assert.That(lines[1]).Contains("\"Id\":2");
    }

    [Test]
    public async Task FlushAsync_WritesValidJsonPerLine()
    {
        var writer = CreateWriter("multi_test");

        writer.Enqueue(Record(1, "First"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(2, "Second"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(3, "Third"), WriterTestJsonContext.Default.WriterTestRecord);

        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "multi_test"), "*.jsonl");
        var lines = await File.ReadAllLinesAsync(files[0]);

        await Assert.That(lines.Length).IsEqualTo(3);

        foreach (var line in lines)
        {
            var parsed = JsonSerializer.Deserialize(line, WriterTestJsonContext.Default.WriterTestRecord);
            await Assert.That(parsed).IsNotNull();
        }
    }

    [Test]
    public async Task Writer_RotatesFile_WhenSizeExceeded()
    {
        var writer = CreateWriter("rotate_test", maxSize: 100);

        for (var i = 0; i < 10; i++)
        {
            writer.Enqueue(Record(i, new string('x', 50)), WriterTestJsonContext.Default.WriterTestRecord);
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

        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        writer.Enqueue(Record(42, "FinalRecord"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Dispose();

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        var content = await File.ReadAllTextAsync(files[0]);
        await Assert.That(content).Contains("FinalRecord");
    }

    [Test]
    public async Task Enqueue_AfterDispose_IsIgnored()
    {
        var subDir = Path.Combine(_temp.Path, "after_dispose");
        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        writer.Dispose();

        writer.Enqueue(Record(1, "late"), WriterTestJsonContext.Default.WriterTestRecord);

        await Assert.That(Directory.GetFiles(subDir, "*.jsonl").Length).IsEqualTo(0);
    }

    [Test]
    public async Task FlushAsync_WithEmptyBuffer_DoesNotThrow()
    {
        var writer = CreateWriter("empty_flush");

        await writer.FlushAsync();

        await Assert.That(writer).IsNotNull();
    }
}

public sealed record WriterTestRecord
{
    public int Id { get; init; }
    public string? Name { get; init; }
}

[JsonSerializable(typeof(WriterTestRecord))]
internal partial class WriterTestJsonContext : JsonSerializerContext
{
}
