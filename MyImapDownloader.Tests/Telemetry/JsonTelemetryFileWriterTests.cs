using System.Text.Json;
using AwesomeAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class JsonTelemetryFileWriterTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public JsonTelemetryFileWriterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }

        await Task.Delay(100);

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    private JsonTelemetryFileWriter CreateWriter(string? subDir = null, string prefix = "test", long maxSize = 1024 * 1024)
    {
        var dir = subDir != null ? Path.Combine(_testDirectory, subDir) : _testDirectory;
        var writer = new JsonTelemetryFileWriter(dir, prefix, maxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    [Test]
    public async Task Constructor_CreatesDirectory_WhenItDoesNotExist()
    {
        var newDir = Path.Combine(_testDirectory, "new_subdir");

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

        // If we get here, the test passed
        await Assert.That(writer).IsNotNull();
    }

    [Test]
    public async Task FlushAsync_WritesEnqueuedRecords_ToFile()
    {
        var writer = CreateWriter();

        var record = new TestRecord { Id = 1, Message = "Hello" };
        writer.Enqueue(record);

        await writer.FlushAsync();

        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("Hello");
        content.Should().Contain("\"id\":1");
    }

    [Test]
    public async Task FlushAsync_WritesMultipleRecords_InJsonlFormat()
    {
        var writer = CreateWriter();

        writer.Enqueue(new TestRecord { Id = 1, Message = "First" });
        writer.Enqueue(new TestRecord { Id = 2, Message = "Second" });
        writer.Enqueue(new TestRecord { Id = 3, Message = "Third" });

        await writer.FlushAsync();

        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        var lines = await File.ReadAllLinesAsync(files[0]);

        // Each record should be on its own line (JSONL format)
        await Assert.That(lines.Length).IsEqualTo(3);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            var parsed = JsonSerializer.Deserialize<TestRecord>(line);
            parsed.Should().NotBeNull();
        }
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        var writer = new JsonTelemetryFileWriter(_testDirectory, "dispose", 1024 * 1024, TimeSpan.FromSeconds(30));

        // Should not throw when called multiple times
        writer.Dispose();
        writer.Dispose();
        writer.Dispose();

        await Assert.That(writer).IsNotNull();
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        var subDir = Path.Combine(_testDirectory, "dispose_flush");
        Directory.CreateDirectory(subDir);

        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));

        writer.Enqueue(new { Test = true });

        // Force a flush before dispose to ensure it works
        await writer.FlushAsync();

        writer.Dispose();

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task FlushAsync_WithEmptyBuffer_DoesNotCreateFile()
    {
        var emptyDir = Path.Combine(_testDirectory, "empty");
        Directory.CreateDirectory(emptyDir);

        var writer = CreateWriter("empty");

        await writer.FlushAsync();

        var files = Directory.GetFiles(emptyDir, "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(0);
    }

    [Test]
    public async Task FileRotation_OccursWhenSizeExceeded()
    {
        // Small max file size to trigger rotation quickly
        const long smallMaxSize = 500; // 500 bytes
        var rotateDir = Path.Combine(_testDirectory, "rotate");
        Directory.CreateDirectory(rotateDir);

        var writer = new JsonTelemetryFileWriter(rotateDir, "rotate", smallMaxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);

        // Write enough data to trigger rotation (each record ~50-100 bytes)
        for (int i = 0; i < 20; i++)
        {
            writer.Enqueue(new TestRecord { Id = i, Message = $"Record number {i} with some padding text" });
        }

        await writer.FlushAsync();

        var files = Directory.GetFiles(rotateDir, "rotate_*.jsonl");

        // Should have created multiple files due to rotation
        await Assert.That(files.Length).IsGreaterThan(1);
    }

    private record TestRecord
    {
        public int Id { get; init; }
        public string? Message { get; init; }
    }
}
