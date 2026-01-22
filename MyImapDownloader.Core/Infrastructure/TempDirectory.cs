namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Creates a temporary directory that is automatically cleaned up on disposal.
/// Useful for tests and temporary file operations.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string? prefix = null)
    {
        var name = prefix ?? "temp";
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
