using System.Text.Json;
using FluentAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class JsonTelemetryFileWriterTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private JsonTelemetryFileWriter? _writer;

    public JsonTelemetryFileWriterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        _writer?.Dispose();
        
        // Small delay to ensure file handles are released
        await Task.Delay(100);
        
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Cleanup failure is not a test failure
        }
    }

    [Test]
    public async Task Constructor_CreatesDirectory_WhenItDoesNotExist()
    {
        var newDir = Path.Combine(_testDirectory, "new_subdir");
        
        _writer = new JsonTelemetryFileWriter(newDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        await Assert.That(Directory.Exists(newDir)).IsTrue();
    }

    [Test]
    public async Task Enqueue_DoesNotThrow_WhenCalled()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        var record = new { Message = "Test", Timestamp = DateTime.UtcNow };
        
        // Should not throw
        _writer.Enqueue(record);
        
        await Assert.That(true).IsTrue(); // Test passes if no exception
    }

    [Test]
    public async Task FlushAsync_WritesEnqueuedRecords_ToFile()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        var record = new TestRecord { Id = 1, Message = "Hello" };
        _writer.Enqueue(record);
        
        await _writer.FlushAsync();
        
        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
        
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("Hello");
        content.Should().Contain("\"id\":1");
    }

    [Test]
    public async Task FlushAsync_WritesMultipleRecords_InJsonlFormat()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        _writer.Enqueue(new TestRecord { Id = 1, Message = "First" });
        _writer.Enqueue(new TestRecord { Id = 2, Message = "Second" });
        _writer.Enqueue(new TestRecord { Id = 3, Message = "Third" });
        
        await _writer.FlushAsync();
        
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
    public async Task FileNaming_IncludesDateAndSequence()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "traces", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        _writer.Enqueue(new { Test = true });
        await _writer.FlushAsync();
        
        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        var fileName = Path.GetFileName(files[0]);
        
        // Should match pattern: traces_YYYY-MM-DD_NNNN.jsonl
        fileName.Should().StartWith("traces_");
        fileName.Should().Contain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        fileName.Should().EndWith(".jsonl");
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        _writer.Enqueue(new TestRecord { Id = 99, Message = "Final" });
        
        _writer.Dispose();
        _writer = null; // Prevent double dispose
        
        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
        
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("Final");
    }

    [Test]
    public async Task Enqueue_AfterDispose_DoesNotThrow()
    {
        _writer = new JsonTelemetryFileWriter(_testDirectory, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        _writer.Dispose();
        
        // Should not throw - silently ignores
        _writer.Enqueue(new { Test = true });
        
        _writer = null; // Already disposed
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task FlushAsync_WithEmptyBuffer_DoesNotCreateFile()
    {
        var emptyDir = Path.Combine(_testDirectory, "empty");
        Directory.CreateDirectory(emptyDir);
        
        _writer = new JsonTelemetryFileWriter(emptyDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        
        await _writer.FlushAsync();
        
        var files = Directory.GetFiles(emptyDir, "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(0);
    }

    [Test]
    public async Task FileRotation_OccursWhenSizeExceeded()
    {
        // Small max file size to trigger rotation quickly
        const long smallMaxSize = 500; // 500 bytes
        
        _writer = new JsonTelemetryFileWriter(_testDirectory, "rotate", smallMaxSize, TimeSpan.FromSeconds(30));
        
        // Write enough data to trigger rotation (each record ~50-100 bytes)
        for (int i = 0; i < 20; i++)
        {
            _writer.Enqueue(new TestRecord { Id = i, Message = $"Record number {i} with some padding text" });
        }
        
        await _writer.FlushAsync();
        
        var files = Directory.GetFiles(_testDirectory, "rotate_*.jsonl");
        
        // Should have created multiple files due to rotation
        await Assert.That(files.Length).IsGreaterThan(1);
    }

    private record TestRecord
    {
        public int Id { get; init; }
        public string? Message { get; init; }
    }
}
