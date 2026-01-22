using Microsoft.Extensions.Logging;

namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Factory for creating test loggers.
/// </summary>
public static class TestLogger
{
    /// <summary>
    /// Creates a logger that writes to the console.
    /// </summary>
    public static ILogger<T> Create<T>()
    {
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        return factory.CreateLogger<T>();
    }

    /// <summary>
    /// Creates a null logger that discards all output.
    /// </summary>
    public static ILogger<T> CreateNull<T>()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
    }
}
