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
            using ImapClient client = new();
            client.Timeout = 120_000;

            await client.ConnectAsync(
                _config.Server,
                _config.Port,
                SecureSocketOptions.SslOnConnect,
                cancellationToken
            );
            using CancellationTokenSource connectionCts = new(TimeSpan.FromMinutes(2));
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    connectionCts.Token,
                    cancellationToken
                );
            await client.AuthenticateAsync(_config.Username, _config.Password, linkedCts.Token);

            IMailFolder inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, linkedCts.Token);

            _logger.LogInformation("Total messages in inbox: {count}", inbox.Count);

            Directory.CreateDirectory(options.OutputDirectory);

            await DownloadMessagesSequentiallyAsync(
                inbox,
                options.OutputDirectory,
                options.StartDate,
                options.EndDate,
                linkedCts.Token
            );
        });
    }

    private async Task DownloadMessagesSequentiallyAsync(
        IMailFolder inbox,
        string outputDirectory,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken
    )
    {
        for (int i = 0; i < inbox.Count; i++)
        {
            try
            {
                using MimeMessage message = await inbox.GetMessageAsync(i, cancellationToken);

                if (ShouldDownloadMessage(message, startDate, endDate))
                {
                    await SaveEmailToDiskAsync(message, outputDirectory, i, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading email {MessageIndex}", i);
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
