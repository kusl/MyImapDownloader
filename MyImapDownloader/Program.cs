using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CommandLine;
using Microsoft.Extensions.Hosting;
using MyImapDownloader;
HostApplicationBuilder host = Host.CreateApplicationBuilder(args);
host.Services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.SetMinimumLevel(LogLevel.Information);
});
host.Services.AddSingleton(sp =>
{
    return Parser.Default.ParseArguments<DownloadOptions>(args)
        .MapResult(
            opts => opts,
            errors => throw new ArgumentException("Invalid command line arguments")
        );
});
host.Services.AddSingleton(sp =>
{
    DownloadOptions parsedOptions = sp.GetRequiredService<DownloadOptions>();
    return new ImapConfiguration
    {
        Server = parsedOptions.Server,
        Username = parsedOptions.Username,
        Password = parsedOptions.Password,
        Port = parsedOptions.Port
    };
});
host.Services.AddTransient<EmailDownloadService>();
IHost builtHost = host.Build();
EmailDownloadService downloadService = builtHost.Services.GetRequiredService<EmailDownloadService>();
DownloadOptions options = builtHost.Services.GetRequiredService<DownloadOptions>();

try
{
    await downloadService.DownloadEmailsAsync(options);
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}
