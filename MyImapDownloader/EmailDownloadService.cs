using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace MyImapDownloader;

public class EmailDownloadService
{
    private readonly ILogger<EmailDownloadService> _logger;
    private readonly ImapConfiguration _config;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public EmailDownloadService(ILogger<EmailDownloadService> logger, ImapConfiguration config)
    {
        _logger = logger;
        _config = config;
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Retry {RetryCount} with delay {Delay}",
                        retryCount,
                        timeSpan
                    );
                }
            );

        _circuitBreakerPolicy = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromMinutes(1)
            );
    }

    public async Task DownloadEmailsAsync(
        DownloadOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Polly.Wrap.AsyncPolicyWrap policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async () =>
        {
            // Define a batch size to process emails in smaller groups
            const int batchSize = 100;
            int processedCount = 0;
            bool hasMoreMessages = true;

            while (hasMoreMessages && !cancellationToken.IsCancellationRequested)
            {
                using ImapClient client = new();
                client.Timeout = 180_000; // Increased timeout to 3 minutes

                try
                {
                    await ConnectToImapServerAsync(client, cancellationToken);
                    IMailFolder inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                    _logger.LogInformation("Total messages in inbox: {count}", inbox.Count);

                    Directory.CreateDirectory(options.OutputDirectory);

                    if (processedCount >= inbox.Count)
                    {
                        hasMoreMessages = false;
                        break;
                    }

                    // Process a batch of messages
                    int endIndex = Math.Min(processedCount + batchSize, inbox.Count);
                    await DownloadMessageBatchAsync(
                        inbox,
                        options.OutputDirectory,
                        options.StartDate,
                        options.EndDate,
                        processedCount,
                        endIndex,
                        cancellationToken
                    );

                    processedCount = endIndex;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during email download batch operation");
                    // Wait before retrying to reconnect
                    await Task.Delay(5000, cancellationToken);
                }
                finally
                {
                    // Ensure we properly disconnect even if there was an error
                    await DisconnectSafelyAsync(client);
                }
            }
        });
    }

    private async Task ConnectToImapServerAsync(ImapClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to IMAP server {Server}:{Port}", _config.Server, _config.Port);
        
        await client.ConnectAsync(
            _config.Server,
            _config.Port,
            SecureSocketOptions.SslOnConnect,
            cancellationToken
        );
        
        await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);
        _logger.LogInformation("Successfully connected to IMAP server");
    }

    private async Task DisconnectSafelyAsync(ImapClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true);
                _logger.LogInformation("Disconnected from IMAP server");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during IMAP client disconnect");
        }
    }

    private async Task DownloadMessageBatchAsync(
        IMailFolder inbox,
        string outputDirectory,
        DateTime? startDate,
        DateTime? endDate,
        int startIndex,
        int endIndex,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation("Processing messages {Start} to {End}", startIndex, endIndex - 1);

        for (int i = startIndex; i < endIndex; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Use a separate cancellation token for each message download with a timeout
                using var messageTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    messageTimeoutCts.Token, 
                    cancellationToken
                );

                using MimeMessage message = await inbox.GetMessageAsync(i, linkedCts.Token);

                if (ShouldDownloadMessage(message, startDate, endDate))
                {
                    await SaveEmailToDiskAsync(message, outputDirectory, i, linkedCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Message download timed out for message {MessageIndex}", i);
                // Continue with the next message rather than failing the entire batch
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading email {MessageIndex}", i);
                // Continue with next message even if this one failed
            }
        }
    }

    private static bool ShouldDownloadMessage(
        MimeMessage message,
        DateTime? startDate,
        DateTime? endDate
    )
    {
        if (!startDate.HasValue && !endDate.HasValue)
            return true;

        DateTime messageDate = message.Date.DateTime;
        return (!startDate.HasValue || messageDate >= startDate.Value)
            && (!endDate.HasValue || messageDate <= endDate.Value);
    }

    private async Task SaveEmailToDiskAsync(
        MimeMessage message,
        string outputDirectory,
        int messageIndex,
        CancellationToken cancellationToken
    )
    {
        string safeSubject = SanitizeFileName(message.Subject ?? "No Subject");

        string subjectPrefix = safeSubject.Length > 10 ? safeSubject[..10] : safeSubject;

        string subjectHash = GenerateSubjectHash(message.Subject);

        string filename = Path.Combine(
            outputDirectory,
            $"{messageIndex}_{subjectPrefix}_{subjectHash}.eml"
        );

        int maxSafePath = 240;

        if (filename.Length > maxSafePath)
        {
            filename = Path.Combine(outputDirectory, $"{messageIndex}_{subjectHash}.eml");
        }

        try
        {
            await using FileStream stream = File.Create(filename);
            await message.WriteToAsync(stream, cancellationToken);
            _logger.LogInformation("Downloaded: {Filename}", filename);
        }
        catch (PathTooLongException)
        {
            string emergencyFilename = Path.Combine(outputDirectory, $"email_{messageIndex}.eml");

            _logger.LogWarning(
                "Path too long for {OriginalPath}, using emergency fallback {EmergencyPath}",
                filename,
                emergencyFilename
            );

            await using FileStream stream = File.Create(emergencyFilename);
            await message.WriteToAsync(stream, cancellationToken);
            _logger.LogInformation(
                "Downloaded with emergency fallback: {Filename}",
                emergencyFilename
            );
        }
    }

    private static string GenerateSubjectHash(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return "empty";

        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(subject)
        );

        return BitConverter.ToString(hashBytes).Replace("-", "")[..8];
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Unnamed_Email";
        }
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = $@"([{invalidChars}]*\.+$)|([{invalidChars}]+)";
        string sanitizedFileName = Regex.Replace(fileName, invalidRegStr, "_").Trim();
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            sanitizedFileName = "Unnamed_Email";
        }
        return sanitizedFileName.Length > 255 ? sanitizedFileName[..255] : sanitizedFileName;
    }
}