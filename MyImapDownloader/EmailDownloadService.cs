using System.Text.RegularExpressions;

// Dependency Injection
using Microsoft.Extensions.Logging;

// Resilience and Retry
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

// IMAP and Email
using MailKit.Net.Imap;
using MailKit;
using MimeKit;
using MailKit.Security;

// Command Line Parsing

namespace MyImapDownloader
{
    // Main Downloader Service
    public class EmailDownloadService
    {
        private readonly ILogger<EmailDownloadService> _logger;
        private readonly ImapConfiguration _config;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

        public EmailDownloadService(
            ILogger<EmailDownloadService> logger,
            ImapConfiguration config)
        {
            _logger = logger;
            _config = config;

            // Retry Policy with exponential backoff
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

            // Circuit Breaker Policy
            _circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromMinutes(1)
                );
        }

        public async Task DownloadEmailsAsync(
            DownloadOptions options,
            CancellationToken cancellationToken = default)
        {
            // Combine retry and circuit breaker policies
            Polly.Wrap.AsyncPolicyWrap policy = Policy.WrapAsync(
                _retryPolicy,
                _circuitBreakerPolicy
            );

            await policy.ExecuteAsync(async () =>
            {
                using ImapClient client = new();
                // Timeout configuration
                client.Timeout = 120_000;

                await client.ConnectAsync(
                    _config.Server,
                    _config.Port,
                    SecureSocketOptions.SslOnConnect,
                    cancellationToken
                );
                using CancellationTokenSource connectionCts = new(TimeSpan.FromMinutes(2));
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    connectionCts.Token,
                    cancellationToken
                );
                await client.AuthenticateAsync(
                    _config.Username,
                    _config.Password,
                    linkedCts.Token
                );

                IMailFolder inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                _logger.LogInformation("Total messages in inbox: {count}", inbox.Count);

                // Create output directory
                Directory.CreateDirectory(options.OutputDirectory);

                // Parallel download with semaphore for controlled concurrency
                await DownloadMessagesSequentiallyAsync(
                    inbox,
                    options.OutputDirectory,
                    options.StartDate,
                    options.EndDate,
                    cancellationToken
                );
            });
        }
        private async Task DownloadMessagesSequentiallyAsync(
            IMailFolder inbox,
            string outputDirectory,
            DateTime? startDate,
            DateTime? endDate,
            CancellationToken cancellationToken)
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
                            _logger.LogError(
                                ex,
                                "Error downloading email {MessageIndex}",
                                i
                            );
                        }
                    }
                }

        private static bool ShouldDownloadMessage(
            MimeMessage message,
            DateTime? startDate,
            DateTime? endDate)
        {
            if (!startDate.HasValue && !endDate.HasValue) return true;

            DateTime messageDate = message.Date.DateTime;
            return (!startDate.HasValue || messageDate >= startDate.Value) &&
                   (!endDate.HasValue || messageDate <= endDate.Value);
        }

    private async Task SaveEmailToDiskAsync(
        MimeMessage message,
        string outputDirectory,
        int messageIndex,
        CancellationToken cancellationToken)
    {
        // Always use the hash-based approach for all files to be safe
        string safeSubject = SanitizeFileName(message.Subject ?? "No Subject");
        
        // Get first 10 chars of subject (or less if subject is shorter)
        string subjectPrefix = safeSubject.Length > 10 
            ? safeSubject.Substring(0, 10) 
            : safeSubject;
            
        // Create a hash of the full subject for uniqueness
        string subjectHash = GenerateSubjectHash(message.Subject);
        
        // Create filename with messageIndex, short subject prefix and hash
        string filename = Path.Combine(
            outputDirectory, 
            $"{messageIndex}_{subjectPrefix}_{subjectHash}.eml"
        );
        
        // Double-check the path length to be extra safe
        int maxSafePath = 240; // Very conservative limit that should work everywhere
        
        if (filename.Length > maxSafePath)
        {
            // Ultra-safe fallback - just use the message index and hash
            filename = Path.Combine(
                outputDirectory, 
                $"{messageIndex}_{subjectHash}.eml"
            );
        }

        try
        {
            await using FileStream stream = File.Create(filename);
            await message.WriteToAsync(stream, cancellationToken);
            _logger.LogInformation("Downloaded: {Filename}", filename);
        }
        catch (PathTooLongException)
        {
            // Last resort fallback if we still have path issues
            string emergencyFilename = Path.Combine(
                outputDirectory,
                $"email_{messageIndex}.eml"
            );
            
            _logger.LogWarning(
                "Path too long for {OriginalPath}, using emergency fallback {EmergencyPath}",
                filename,
                emergencyFilename
            );
            
            await using FileStream stream = File.Create(emergencyFilename);
            await message.WriteToAsync(stream, cancellationToken);
            _logger.LogInformation("Downloaded with emergency fallback: {Filename}", emergencyFilename);
        }
    }

    private static string GenerateSubjectHash(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return "empty";
            
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(subject)
        );
        
        return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
    }
    
    private static string SanitizeFileName(string fileName)
    {
        // Handle null or empty input
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Unnamed_Email";
        }
        // Escape invalid filename characters
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = $@"([{invalidChars}]*\.+$)|([{invalidChars}]+)";
        // Replace invalid characters with underscores
        string sanitizedFileName = Regex.Replace(fileName, invalidRegStr, "_")
            .Trim();
        // Ensure the filename is not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            sanitizedFileName = "Unnamed_Email";
        }
        // Truncate filename to a safe length
        return sanitizedFileName.Length > 255
            ? sanitizedFileName.Substring(0, 255)
            : sanitizedFileName;
    }
    }
}