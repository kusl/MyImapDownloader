using System.CommandLine;

using MyImapDownloader;

var invocation = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(30) };
return await DownloadCommand.Parse(DownloadCommand.Create(DownloadSession.RunAsync), args).InvokeAsync(invocation);
