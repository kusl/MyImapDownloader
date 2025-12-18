using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyImapDownloader;

var parseResult = Parser.Default.ParseArguments<DownloadOptions>(args);

await parseResult.WithParsedAsync(async options =>
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
        })
        .ConfigureServices(services =>
        {
            services.AddSingleton(options);
            services.AddSingleton(new ImapConfiguration
            {
                Server = options.Server,
                Username = options.Username,
                Password = options.Password,
                Port = options.Port
            });
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EmailStorageService>>();
                return new EmailStorageService(logger, options.OutputDirectory);
            });
            services.AddTransient<EmailDownloadService>();
        })
        .Build();

    var downloadService = host.Services.GetRequiredService<EmailDownloadService>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Starting email archive download...");
        logger.LogInformation("Output: {Output}", Path.GetFullPath(options.OutputDirectory));
        
        await downloadService.DownloadEmailsAsync(options);
        
        logger.LogInformation("Archive complete!");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Fatal error during download");
        Environment.ExitCode = 1;
    }
});

parseResult.WithNotParsed(errors =>
{
    Environment.ExitCode = 1;
});
