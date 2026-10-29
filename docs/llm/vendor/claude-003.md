00
68

Please review our code and see if there is something we can do for errors like this
what are our options here? 

```bash
fail: MyImapDownloader.EmailDownloadService[0]
      Error processing folder Archives
      System.IO.IOException: The read operation failed, see inner exception.
       ---> System.TimeoutException: Operation timed out after 180000 milliseconds
       ---> System.Threading.Tasks.TaskCanceledException: A task was canceled.
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         at System.Net.Security.SslStream.EnsureFullTlsFrameAsync[TIOAdapter](CancellationToken cancellationToken, Int32 estimatedSize)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
      --- End of stack trace from previous location ---
         at MailKit.Net.Imap.ImapStream.ReadAheadAsync(Int32 atleast, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapStream.ReadTokenAsync(String specials, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapCommand.StepAsync()
         at MailKit.Net.Imap.ImapEngine.IterateAsync()
         at MailKit.Net.Imap.ImapEngine.RunAsync(ImapCommand ic)
         at MailKit.Net.Imap.ImapFolder.FetchAsync(IList`1 uids, IFetchRequest request, CancellationToken cancellationToken)
         at MyImapDownloader.EmailDownloadService.DownloadBatchAsync(IMailFolder folder, IList`1 uids, FolderCheckpointTracker checkpoint, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
warn: MyImapDownloader.EmailDownloadService[0]
      Retry 1 in 00:00:02: The read operation failed, see inner exception.
      System.IO.IOException: The read operation failed, see inner exception.
       ---> System.TimeoutException: Operation timed out after 180000 milliseconds
       ---> System.Threading.Tasks.TaskCanceledException: A task was canceled.
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         at System.Net.Security.SslStream.EnsureFullTlsFrameAsync[TIOAdapter](CancellationToken cancellationToken, Int32 estimatedSize)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
      --- End of stack trace from previous location ---
         at MailKit.Net.Imap.ImapStream.ReadAheadAsync(Int32 atleast, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapStream.ReadTokenAsync(String specials, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapCommand.StepAsync()
         at MailKit.Net.Imap.ImapEngine.IterateAsync()
         at MailKit.Net.Imap.ImapEngine.RunAsync(ImapCommand ic)
         at MailKit.Net.Imap.ImapFolder.FetchAsync(IList`1 uids, IFetchRequest request, CancellationToken cancellationToken)
         at MyImapDownloader.EmailDownloadService.DownloadBatchAsync(IMailFolder folder, IList`1 uids, FolderCheckpointTracker checkpoint, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.<>c__DisplayClass8_0.<<DownloadEmailsAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at MyImapDownloader.EmailDownloadService.<>c__DisplayClass8_0.<<DownloadEmailsAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.AsyncPolicy.<>c__DisplayClass44_0.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.CircuitBreaker.AsyncCircuitBreakerPolicy.<>c__DisplayClass8_0`1.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.CircuitBreaker.AsyncCircuitBreakerEngine.ImplementationAsync[TResult](Func`3 action, Context context, Boolean continueOnCapturedContext, ExceptionPredicates shouldHandleExceptionPredicates, ResultPredicates`1 shouldHandleResultPredicates, ICircuitController`1 breakerController, CancellationToken cancellationToken)
         at Polly.CircuitBreaker.AsyncCircuitBreakerPolicy.ImplementationAsync[TResult](Func`3 action, Context context, CancellationToken cancellationToken, Boolean continueOnCapturedContext)
         at Polly.AsyncPolicy.ExecuteInternalAsync(Func`3 action, Context context, Boolean continueOnCapturedContext, CancellationToken cancellationToken)
         at Polly.AsyncPolicy.<>c__DisplayClass44_0.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.Retry.AsyncRetryEngine.ImplementationAsync[TResult](Func`3 action, Context context, ExceptionPredicates shouldRetryExceptionPredicates, ResultPredicates`1 shouldRetryResultPredicates, Func`5 onRetryAsync, CancellationToken cancellationToken, Int32 permittedRetryCount, IEnumerable`1 sleepDurationsEnumerable, Func`4 sleepDurationProvider, Boolean continueOnCapturedContext)
info: MyImapDownloader.EmailDownloadService[0]
      Connecting to imap.gmx.com:993
```




























Claude
/
Please review our code and see if there is something we can do for errors like this
what are our options here? 

fail: MyImapDownloader.EmailDownloadService[0]
      Error processing folder Archives
      System.IO.IOException: The read operation failed, see inner exception.
       ---> System.TimeoutException: Operation timed out after 180000 milliseconds
       ---> System.Threading.Tasks.TaskCanceledException: A task was canceled.
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         at System.Net.Security.SslStream.EnsureFullTlsFrameAsync[TIOAdapter](CancellationToken cancellationToken, Int32 estimatedSize)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
      --- End of stack trace from previous location ---
         at MailKit.Net.Imap.ImapStream.ReadAheadAsync(Int32 atleast, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapStream.ReadTokenAsync(String specials, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapCommand.StepAsync()
         at MailKit.Net.Imap.ImapEngine.IterateAsync()
         at MailKit.Net.Imap.ImapEngine.RunAsync(ImapCommand ic)
         at MailKit.Net.Imap.ImapFolder.FetchAsync(IList`1 uids, IFetchRequest request, CancellationToken cancellationToken)
         at MyImapDownloader.EmailDownloadService.DownloadBatchAsync(IMailFolder folder, IList`1 uids, FolderCheckpointTracker checkpoint, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
warn: MyImapDownloader.EmailDownloadService[0]
      Retry 1 in 00:00:02: The read operation failed, see inner exception.
      System.IO.IOException: The read operation failed, see inner exception.
       ---> System.TimeoutException: Operation timed out after 180000 milliseconds
       ---> System.Threading.Tasks.TaskCanceledException: A task was canceled.
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at MailKit.Net.NetworkStream.ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
         at System.Net.Security.SslStream.EnsureFullTlsFrameAsync[TIOAdapter](CancellationToken cancellationToken, Int32 estimatedSize)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](Memory`1 buffer, CancellationToken cancellationToken)
         at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)
         at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
      --- End of stack trace from previous location ---
         at MailKit.Net.Imap.ImapStream.ReadAheadAsync(Int32 atleast, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapStream.ReadTokenAsync(String specials, CancellationToken cancellationToken)
         at MailKit.Net.Imap.ImapCommand.StepAsync()
         at MailKit.Net.Imap.ImapEngine.IterateAsync()
         at MailKit.Net.Imap.ImapEngine.RunAsync(ImapCommand ic)
         at MailKit.Net.Imap.ImapFolder.FetchAsync(IList`1 uids, IFetchRequest request, CancellationToken cancellationToken)
         at MyImapDownloader.EmailDownloadService.DownloadBatchAsync(IMailFolder folder, IList`1 uids, FolderCheckpointTracker checkpoint, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
         at MyImapDownloader.EmailDownloadService.<>c__DisplayClass8_0.<<DownloadEmailsAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at MyImapDownloader.EmailDownloadService.<>c__DisplayClass8_0.<<DownloadEmailsAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.AsyncPolicy.<>c__DisplayClass44_0.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.CircuitBreaker.AsyncCircuitBreakerPolicy.<>c__DisplayClass8_0`1.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.CircuitBreaker.AsyncCircuitBreakerEngine.ImplementationAsync[TResult](Func`3 action, Context context, Boolean continueOnCapturedContext, ExceptionPredicates shouldHandleExceptionPredicates, ResultPredicates`1 shouldHandleResultPredicates, ICircuitController`1 breakerController, CancellationToken cancellationToken)
         at Polly.CircuitBreaker.AsyncCircuitBreakerPolicy.ImplementationAsync[TResult](Func`3 action, Context context, CancellationToken cancellationToken, Boolean continueOnCapturedContext)
         at Polly.AsyncPolicy.ExecuteInternalAsync(Func`3 action, Context context, Boolean continueOnCapturedContext, CancellationToken cancellationToken)
         at Polly.AsyncPolicy.<>c__DisplayClass44_0.<<ImplementationAsync>b__0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Polly.Retry.AsyncRetryEngine.ImplementationAsync[TResult](Func`3 action, Context context, ExceptionPredicates shouldRetryExceptionPredicates, ResultPredicates`1 shouldRetryResultPredicates, Func`5 onRetryAsync, CancellationToken cancellationToken, Int32 permittedRetryCount, IEnumerable`1 sleepDurationsEnumerable, Func`4 sleepDurationProvider, Boolean continueOnCapturedContext)
info: MyImapDownloader.EmailDownloadService[0]
      Connecting to imap.gmx.com:993
Ran 3 commands, read memory
Reviewing dump.txt alongside the project's memory notes.
I read EmailDownloadService.cs and FolderCheckpointTracker.cs in dump.txt. Here’s what the log shows and what we can change.

What happened
The timeout is in the envelope fetch at the top of DownloadBatchAsync:

csharp
var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | ..., ct);
That request is for metadata on 50 messages, which is only a few KB. A 180-second wait with no bytes arriving means the server stalled or the connection silently died. It is not a large-download problem. MailKit’s Timeout is a per-read inactivity timeout, so it fired after 180s of silence.

Recovery then worked as designed. The exception escaped ProcessFolderAsync, the session-level Polly retry kicked in, a fresh ImapClient was created, and it reconnected. No data was lost. The checkpoint was saved after each completed batch, so the next pass resumes from the last good UID in Archives.

So this particular error is handled. But reading the code, I found one real defect next to it and two weaker spots.

The defect: per-message catch swallows dead connections
In the per-message loop:

csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
    failedUids.Add(uid);
    checkpoint.MarkFailed(uid);
}
Suppose the same kind of stall hits GetStreamAsync for a message body instead of the envelope fetch. MailKit disconnects the client after an IOException. This catch treats that as a single bad message and moves on to the next one. Every remaining message in the batch then fails immediately with ServiceNotConnectedException, which is also caught.

You end up with up to 49 spurious “Failed to download UID” errors. The checkpoint gets pinned back to the first failure, and only the next batch’s FetchAsync finally escapes to trigger a reconnect. No data is lost, because MarkFailed holds the checkpoint correctly. But the log is misleading and the batch is wasted.

The fix is to rethrow connection-level failures, meaning IOException, ImapProtocolException, ServiceNotConnectedException, or anything once client.IsConnected is false. Only genuinely per-message problems, like a parse or storage error on one email, should be caught and skipped. I’d do this one regardless of the options below.

Weaker spots
No progress detection. The retry is WaitAndRetryForeverAsync around the whole session. Suppose GMX reliably stalls on one particular batch in Archives, for example because of a malformed message whose envelope the server chokes on. Then the program will reconnect, re-scan from INBOX, hit the same batch, and stall again, forever, with backoff capped at 5 minutes. Other folders after Archives never get synced in that run. The circuit breaker doesn’t help here: when it opens, BrokenCircuitException passes IsRetryable, so the forever-retry just waits it out.

180s is slow to notice a dead connection. Because it’s an inactivity timeout, lowering it to 60s doesn’t risk cutting off large messages that are still streaming. It just detects dead connections three times faster.

Options, roughly in order of effort
Fix the per-message catch as described above. This is small, contained, and testable on its own.
Make Timeout configurable with a lower default, e.g. 60s, via ImapConfiguration. This is trivial.
Move retry from session level to folder level, with a no-progress guard. Each folder gets its own retry loop that reconnects through an injected connection factory. If N consecutive attempts don’t advance the checkpoint, the run logs it, skips that folder for this run, and continues with the rest. The next run tries again. This also removes the restart-from-INBOX behaviour. It is the most change, but it makes EmailDownloadService unit-testable with NSubstitute without a real server, which it currently isn’t.
Halve the batch size on a fetch timeout. I don’t recommend this. With a 50-envelope fetch stalling, volume isn’t the cause, so smaller batches probably wouldn’t help.
Add a short pause between batches. This is a cheap experiment if GMX turns out to be throttling bulk sessions. I can’t confirm GMX’s actual limits from here, so treat it as a guess to test, not a known fix.
One thing would tell us whether option 3 is urgent. After the reconnect, does the “Syncing Archives. Last UID: N” log line show N advancing across retries? Or does it stall at the same UID every time? If it advances, it’s transient network or server flakiness, and options 1 and 2 are enough. If it’s stuck at the same UID, there’s a poison batch and option 3 matters.

Do you want me to write up options 1 and 2 as a single script now, with tests, and hold option 3 until you’ve checked that log line?




Claude is AI and can make mistakes. Please double-check responses.






Claude finished the response










































































Before I asked you this question, I updated nuget dependencies 
(which I think have nothing to do with this problem)
rebooted 
and updated the project with the latest binaries from github 
(which again I don't think have anything to do with this issue)
then I gave you the latest dump.txt 
and ran the program again... twice 
I didn't see the exception again 
so it makes me think it was transient error for some reason 
in any case I mentioned this so you have a full context 

in this case please only fix 1 for now 

Use your best judgment
If only a few files (under five or there about) need to change, 
give me the full path of the files that need to change and the FULL file 
if there are a lot of files that need to change 
Delivery
- Whole project as one tar.gz, unambiguous file placement
- do not include any file that has not changed 
- Any files to delete on updates called out explicitly
- No bespoke sed/patch scripts — full files only for anything that changes
- Docs updated alongside code changes
- Low verbosity, no code comments

```bash
info: Program[0]
      Starting email archive download...
info: Program[0]
      Output: /home/kushal/Documents/mail/{redacted}_gmx_backup/
info: Program[0]
      Telemetry output: /home/kushal/.local/state/myimapdownloader/telemetry
info: MyImapDownloader.EmailDownloadService[0]
      Connecting to imap.gmx.com:993
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives. Last UID: 13457
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2021. Last UID: 6706
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2021
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2022. Last UID: 68438
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2022
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2023. Last UID: 60403
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2023
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2024. Last UID: 98
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2024
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2025. Last UID: 162
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2025
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Drafts. Last UID: 0
info: MyImapDownloader.EmailDownloadService[0]
      Found 0 new messages in Drafts
info: MyImapDownloader.EmailDownloadService[0]
      Syncing INBOX. Last UID: 377110
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in INBOX
info: MyImapDownloader.EmailDownloadService[0]
      Syncing OUTBOX. Last UID: 0
info: MyImapDownloader.EmailDownloadService[0]
      Found 0 new messages in OUTBOX
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Sent. Last UID: 8196
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Sent
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Spam. Last UID: 4496
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Spam
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Trash. Last UID: 170
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Trash
info: Program[0]
      Archive complete! Session duration: 8660ms

real	0m8.883s
user	0m0.951s
sys	0m0.104s
45G	/home/kushal/Documents/mail
```










Claude
/
Ran 3 commands, read memory
Reviewing dump.txt alongside the project's memory notes.
I read EmailDownloadService.cs and FolderCheckpointTracker.cs in dump.txt. Here’s what the log shows and what we can change.

What happened
The timeout is in the envelope fetch at the top of DownloadBatchAsync:

csharp
var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | ..., ct);
That request is for metadata on 50 messages, which is only a few KB. A 180-second wait with no bytes arriving means the server stalled or the connection silently died. It is not a large-download problem. MailKit’s Timeout is a per-read inactivity timeout, so it fired after 180s of silence.

Recovery then worked as designed. The exception escaped ProcessFolderAsync, the session-level Polly retry kicked in, a fresh ImapClient was created, and it reconnected. No data was lost. The checkpoint was saved after each completed batch, so the next pass resumes from the last good UID in Archives.

So this particular error is handled. But reading the code, I found one real defect next to it and two weaker spots.

The defect: per-message catch swallows dead connections
In the per-message loop:

csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
    failedUids.Add(uid);
    checkpoint.MarkFailed(uid);
}
Suppose the same kind of stall hits GetStreamAsync for a message body instead of the envelope fetch. MailKit disconnects the client after an IOException. This catch treats that as a single bad message and moves on to the next one. Every remaining message in the batch then fails immediately with ServiceNotConnectedException, which is also caught.

You end up with up to 49 spurious “Failed to download UID” errors. The checkpoint gets pinned back to the first failure, and only the next batch’s FetchAsync finally escapes to trigger a reconnect. No data is lost, because MarkFailed holds the checkpoint correctly. But the log is misleading and the batch is wasted.

The fix is to rethrow connection-level failures, meaning IOException, ImapProtocolException, ServiceNotConnectedException, or anything once client.IsConnected is false. Only genuinely per-message problems, like a parse or storage error on one email, should be caught and skipped. I’d do this one regardless of the options below.

Weaker spots
No progress detection. The retry is WaitAndRetryForeverAsync around the whole session. Suppose GMX reliably stalls on one particular batch in Archives, for example because of a malformed message whose envelope the server chokes on. Then the program will reconnect, re-scan from INBOX, hit the same batch, and stall again, forever, with backoff capped at 5 minutes. Other folders after Archives never get synced in that run. The circuit breaker doesn’t help here: when it opens, BrokenCircuitException passes IsRetryable, so the forever-retry just waits it out.

180s is slow to notice a dead connection. Because it’s an inactivity timeout, lowering it to 60s doesn’t risk cutting off large messages that are still streaming. It just detects dead connections three times faster.

Options, roughly in order of effort
Fix the per-message catch as described above. This is small, contained, and testable on its own.
Make Timeout configurable with a lower default, e.g. 60s, via ImapConfiguration. This is trivial.
Move retry from session level to folder level, with a no-progress guard. Each folder gets its own retry loop that reconnects through an injected connection factory. If N consecutive attempts don’t advance the checkpoint, the run logs it, skips that folder for this run, and continues with the rest. The next run tries again. This also removes the restart-from-INBOX behaviour. It is the most change, but it makes EmailDownloadService unit-testable with NSubstitute without a real server, which it currently isn’t.
Halve the batch size on a fetch timeout. I don’t recommend this. With a 50-envelope fetch stalling, volume isn’t the cause, so smaller batches probably wouldn’t help.
Add a short pause between batches. This is a cheap experiment if GMX turns out to be throttling bulk sessions. I can’t confirm GMX’s actual limits from here, so treat it as a guess to test, not a known fix.
One thing would tell us whether option 3 is urgent. After the reconnect, does the “Syncing Archives. Last UID: N” log line show N advancing across retries? Or does it stall at the same UID every time? If it advances, it’s transient network or server flakiness, and options 1 and 2 are enough. If it’s stuck at the same UID, there’s a poison batch and option 3 matters.

Do you want me to write up options 1 and 2 as a single script now, with tests, and hold option 3 until you’ve checked that log line?

Before I asked you this question, I updated nuget dependencies 
(which I think have nothing to do with this problem)
rebooted 
and updated the project with the latest binaries from github 
(which again I don't think have anything to do with this issue)
then I gave you the latest dump.txt 
and ran the program again... twice 
I didn't see the exception again 
so it makes me think it was transient error for some reason 
in any case I mentioned this so you have a full context 

in this case please only fix 1 for now 

Use your best judgment
If only a few files (under five or there about) need to change, 
give me the full path of the files that need to change and the FULL file 
if there are a lot of files that need to change 
Delivery
- Whole project as one tar.gz, unambiguous file placement
- do not include any file that has not changed 
- Any files to delete on updates called out explicitly
- No bespoke sed/patch scripts — full files only for anything that changes
- Docs updated alongside code changes
- Low verbosity, no code comments

info: Program[0]
      Starting email archive download...
info: Program[0]
      Output: /home/kushal/Documents/mail/{redacted}_gmx_backup/
info: Program[0]
      Telemetry output: /home/kushal/.local/state/myimapdownloader/telemetry
info: MyImapDownloader.EmailDownloadService[0]
      Connecting to imap.gmx.com:993
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives. Last UID: 13457
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2021. Last UID: 6706
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2021
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2022. Last UID: 68438
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2022
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2023. Last UID: 60403
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2023
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2024. Last UID: 98
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2024
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Archives/2025. Last UID: 162
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Archives/2025
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Drafts. Last UID: 0
info: MyImapDownloader.EmailDownloadService[0]
      Found 0 new messages in Drafts
info: MyImapDownloader.EmailDownloadService[0]
      Syncing INBOX. Last UID: 377110
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in INBOX
info: MyImapDownloader.EmailDownloadService[0]
      Syncing OUTBOX. Last UID: 0
info: MyImapDownloader.EmailDownloadService[0]
      Found 0 new messages in OUTBOX
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Sent. Last UID: 8196
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Sent
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Spam. Last UID: 4496
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Spam
info: MyImapDownloader.EmailDownloadService[0]
      Syncing Trash. Last UID: 170
info: MyImapDownloader.EmailDownloadService[0]
      Found 1 new messages in Trash
info: Program[0]
      Archive complete! Session duration: 8660ms

real    0m8.883s
user    0m0.951s
sys    0m0.104s
45G    /home/kushal/Documents/mail
Ran 2 commands
Agreed, it looks transient: the stall didn’t come back across two clean runs. Here is fix 1 only.

Network errors while downloading a message body now end the attempt and go to the existing reconnect retry. Previously they were logged as a single failed message and the loop carried on with a dead connection. Errors that really are about one message (it was expunged, the server rejected it, a local write failed) are still recorded and skipped as before.

Four files: two changed, two new. Nothing to delete. I couldn’t build here because nuget.org isn’t reachable from my sandbox, so please run dotnet build && dotnet test.

1. MyImapDownloader/ImapConnectionFailure.cs (new)
csharp
using System.Net.Sockets;

using MailKit;

namespace MyImapDownloader;

/// <summary>
/// Classifies exceptions that indicate the IMAP connection is no longer usable.
/// </summary>
public static class ImapConnectionFailure
{
    /// <summary>
    /// Returns true when the exception means the connection was lost and the session must reconnect,
    /// as opposed to a failure specific to a single message.
    /// </summary>
    public static bool IsConnectionLost(Exception ex) => ex is
        IOException or
        SocketException or
        TimeoutException or
        ProtocolException or
        ServiceNotConnectedException or
        FolderNotOpenException;
}
2. MyImapDownloader/EmailDownloadService.cs (changed)
csharp
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Logging;

using MyImapDownloader.Telemetry;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace MyImapDownloader;

public class EmailDownloadService
{
    private const int BatchSize = 50;

    private readonly ILogger<EmailDownloadService> _logger;
    private readonly ImapConfiguration _config;
    private readonly EmailStorageService _storage;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public EmailDownloadService(
        ILogger<EmailDownloadService> logger,
        ImapConfiguration config,
        EmailStorageService storage)
    {
        _logger = logger;
        _config = config;
        _storage = storage;

        _retryPolicy = Policy
            .Handle<Exception>(IsRetryable)
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 300)),
                (exception, retryCount, timeSpan) =>
                {
                    DiagnosticsConfig.RetryAttempts.Add(1);
                    _logger.LogWarning(exception, "Retry {Count} in {Delay}: {Message}", retryCount, timeSpan, exception.Message);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(IsRetryable)
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(2));
    }

    private static bool IsRetryable(Exception ex) =>
        ex is not AuthenticationException and not OperationCanceledException;

    public async Task DownloadEmailsAsync(DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("DownloadEmails");

        await _storage.InitializeAsync(ct);

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async token =>
        {
            using var client = new ImapClient { Timeout = 180_000 };
            try
            {
                await ConnectAndAuthenticateAsync(client, token);

                var inbox = client.Inbox
                    ?? throw new InvalidOperationException(
                        "IMAP client returned a null Inbox after authentication.");

                var folders = options.AllFolders
                    ? await GetAllFoldersAsync(client, inbox, token)
                    : new List<IMailFolder> { inbox };

                foreach (var folder in folders)
                {
                    await ProcessFolderAsync(folder, options, token);
                }
            }
            finally
            {
                await DisconnectQuietlyAsync(client);
            }
        }, ct);
    }

    private async Task ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessFolder");
        activity?.SetTag("folder", folder.FullName);

        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

            var lastUid = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity, ct);

            _logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, lastUid);

            var query = SearchQuery.All;
            if (lastUid > 0)
            {
                var range = new UniqueIdRange(new UniqueId((uint)lastUid + 1), UniqueId.MaxValue);
                query = SearchQuery.Uids(range);
            }
            if (options.StartDate.HasValue) query = query.And(SearchQuery.DeliveredAfter(options.StartDate.Value));
            if (options.EndDate.HasValue) query = query.And(SearchQuery.DeliveredBefore(options.EndDate.Value));

            var uids = await folder.SearchAsync(query, ct);
            _logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            var checkpoint = new FolderCheckpointTracker();

            for (var i = 0; i < uids.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = uids.Skip(i).Take(BatchSize).ToList();
                var failedUids = await DownloadBatchAsync(folder, batch, checkpoint, ct);

                if (checkpoint.SafeCheckpoint > 0)
                {
                    await _storage.UpdateLastUidAsync(folder.FullName, checkpoint.SafeCheckpoint, folder.UidValidity, ct);
                }

                if (failedUids.Count > 0)
                {
                    _logger.LogWarning("Failed to download {Count} emails in {Folder}: UIDs {Uids}",
                        failedUids.Count, folder.FullName, string.Join(", ", failedUids));
                }
            }

            if (checkpoint.LowestFailedUid.HasValue)
            {
                _logger.LogWarning(
                    "Checkpoint for {Folder} held at UID {Checkpoint} so UID {Failed} is retried next run",
                    folder.FullName, checkpoint.SafeCheckpoint, checkpoint.LowestFailedUid.Value);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing folder {Folder}", folder.FullName);
            throw;
        }
    }

    private async Task<List<uint>> DownloadBatchAsync(
        IMailFolder folder,
        IList<UniqueId> uids,
        FolderCheckpointTracker checkpoint,
        CancellationToken ct)
    {
        var failedUids = new List<uint>();

        var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        foreach (var item in items)
        {
            using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessEmail");

            var uid = item.UniqueId.Id;
            var envelope = item.Envelope;
            var envelopeMessageId = envelope?.MessageId;

            if (!string.IsNullOrWhiteSpace(envelopeMessageId))
            {
                var normalizedId = EmailStorageService.NormalizeMessageId(envelopeMessageId);
                if (await _storage.ExistsAsyncNormalized(normalizedId, ct))
                {
                    _logger.LogDebug("Skipping duplicate {Id}", normalizedId);
                    checkpoint.MarkProcessed(uid);
                    continue;
                }
            }

            Stream stream;
            try
            {
                stream = await folder.GetStreamAsync(item.UniqueId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ImapConnectionFailure.IsConnectionLost(ex))
            {
                RecordFailure(ex, uid, failedUids, checkpoint);
                continue;
            }

            await using (stream)
            {
                try
                {
                    var isNew = await _storage.SaveStreamAsync(
                        stream,
                        envelopeMessageId ?? string.Empty,
                        item.InternalDate ?? DateTimeOffset.UtcNow,
                        folder.FullName,
                        ct);

                    if (isNew)
                    {
                        DiagnosticsConfig.EmailsDownloaded.Add(1);
                        _logger.LogInformation("Downloaded: {Subject}", envelope?.Subject);
                    }

                    checkpoint.MarkProcessed(uid);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RecordFailure(ex, uid, failedUids, checkpoint);
                }
            }
        }

        return failedUids;
    }

    private void RecordFailure(Exception ex, uint uid, List<uint> failedUids, FolderCheckpointTracker checkpoint)
    {
        _logger.LogError(ex, "Failed to download UID {Uid}", uid);
        failedUids.Add(uid);
        checkpoint.MarkFailed(uid);
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);
        await client.ConnectAsync(_config.Server, _config.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_config.Username, _config.Password, ct);
    }

    private async Task DisconnectQuietlyAsync(ImapClient client)
    {
        if (!client.IsConnected) return;

        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IMAP disconnect failed");
        }
    }

    private static async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, IMailFolder inbox, CancellationToken ct)
    {
        var folders = new List<IMailFolder>();

        if (client.PersonalNamespaces.Count > 0)
        {
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            await CollectFoldersRecursiveAsync(personal, folders, ct);
        }

        if (!folders.Contains(inbox))
        {
            folders.Insert(0, inbox);
        }

        return folders;
    }

    private static async Task CollectFoldersRecursiveAsync(IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
    {
        foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
        {
            if (IsSelectable(folder))
            {
                folders.Add(folder);
            }

            await CollectFoldersRecursiveAsync(folder, folders, ct);
        }
    }

    private static bool IsSelectable(IMailFolder folder) =>
        (folder.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0;
}
3. MyImapDownloader.Tests/ImapConnectionFailureTests.cs (new)
csharp
using System.Net.Sockets;

using MailKit;
using MailKit.Net.Imap;

namespace MyImapDownloader.Tests;

public class ImapConnectionFailureTests
{
    [Test]
    public async Task IOException_IsConnectionLost()
    {
        var ex = new IOException("The read operation failed", new TimeoutException());

        await Assert.That(ImapConnectionFailure.IsConnectionLost(ex)).IsTrue();
    }

    [Test]
    public async Task SocketException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new SocketException())).IsTrue();
    }

    [Test]
    public async Task TimeoutException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new TimeoutException())).IsTrue();
    }

    [Test]
    public async Task ImapProtocolException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new ImapProtocolException("Unexpected token"))).IsTrue();
    }

    [Test]
    public async Task ServiceNotConnectedException_IsConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new ServiceNotConnectedException())).IsTrue();
    }

    [Test]
    public async Task FolderNotOpenException_IsConnectionLost()
    {
        var ex = new FolderNotOpenException("Archives", FolderAccess.ReadOnly);

        await Assert.That(ImapConnectionFailure.IsConnectionLost(ex)).IsTrue();
    }

    [Test]
    public async Task MessageNotFoundException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new MessageNotFoundException("Expunged"))).IsFalse();
    }

    [Test]
    public async Task InvalidOperationException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new InvalidOperationException())).IsFalse();
    }

    [Test]
    public async Task FormatException_IsNotConnectionLost()
    {
        await Assert.That(ImapConnectionFailure.IsConnectionLost(new FormatException())).IsFalse();
    }
}
4. README.md (changed: Delta sync section only)
markdown
# MyImapDownloader

[![Build and Test](https://github.com/kusl/MyImapDownloader/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/kusl/MyImapDownloader/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

Two .NET 10 CLI tools:

- **MyImapDownloader** archives IMAP mailboxes to local `.eml` files with delta sync.
- **MyEmailSearch** indexes the archive into SQLite FTS5 and searches it.

> This project contains code generated by LLMs. Treat it as experimental.

## Safety

- IMAP folders are opened with `FolderAccess.ReadOnly`. No delete, move or flag commands exist.
- Archived `.eml` files are never overwritten or deleted. The only local deletion is cleanup of failed writes in `tmp/`.
- The search index is derived data and can be rebuilt at any time.

## Install

Build from source:

```bash
git clone https://github.com/kusl/MyImapDownloader.git
cd MyImapDownloader
dotnet build -c Release
```

Or install the latest self-contained Linux release to `/opt` with wrappers in `/usr/local/bin`:

```bash
bash install.sh
```

`install.sh` preserves an existing `appsettings.json`.

## MyImapDownloader

```bash
myimapdownloader -s imap.example.com -u you@example.com -p "app-password" -o ~/Documents/mail/you_example_com -a
```

| Option | Alias | Default | Description |
|---|---|---|---|
| `--server` | `-s` | required | IMAP server |
| `--username` | `-u` | required | Username |
| `--password` | `-p` | required | Password or app password |
| `--port` | `-r` | `993` | Port (implicit TLS) |
| `--output` | `-o` | `EmailArchive` | Archive directory |
| `--all-folders` | `-a` | `false` | All selectable folders instead of INBOX only |
| `--start-date` | | | Only messages delivered after this date (`yyyy-MM-dd`) |
| `--end-date` | | | Only messages delivered before this date (`yyyy-MM-dd`) |
| `--verbose` | `-v` | `false` | Debug logging |
| `--help`, `--version` | `-h` | | |

Arguments starting with `@` are passed through literally; response files are disabled.

Exit codes: `0` success, `1` failure or invalid arguments, `130` cancelled (Ctrl+C or SIGTERM; up to 30 s is allowed to flush state).

### Archive layout

```
<output>/
├── index.v1.db
└── INBOX/
    ├── cur/
    │   ├── 1702900000.abc123@mail.example.com.myhost.eml
    │   └── 1702900000.abc123@mail.example.com.myhost.eml.meta.json
    ├── new/
    └── tmp/
```

File names are `{internal-date-unix}.{normalized-message-id}.{host}.eml`. Messages are written to `tmp/` and moved to `cur/`.

Messages without a `Message-ID` header are identified by the SHA-256 of their content.

`index.v1.db` (SQLite, WAL):

```sql
CREATE TABLE Messages (MessageId TEXT PRIMARY KEY, Folder TEXT NOT NULL, ImportedAt TEXT NOT NULL);
CREATE TABLE SyncState (Folder TEXT PRIMARY KEY, LastUid INTEGER NOT NULL, UidValidity INTEGER NOT NULL);
```

Sidecar `.meta.json`:

```json
{
  "MessageId": "abc123@mail.example.com",
  "Subject": "Project Update",
  "From": "alice@example.com",
  "To": "bob@example.com",
  "Date": "2025-12-24T10:30:00Z",
  "Folder": "INBOX",
  "ArchivedAt": "2025-12-24T15:45:32Z",
  "HasAttachments": false
}
```

`HasAttachments` is currently always `false`.

### Delta sync

1. Read `LastUid`/`UidValidity` for the folder. A changed `UIDVALIDITY` resets the cursor to 0.
2. Search `UID > LastUid`, plus optional date filters.
3. Fetch envelopes in batches of 50 and skip Message-IDs already in `Messages`.
4. Stream each new message to disk.
5. After each batch, advance `LastUid` to the highest UID below the lowest failed UID in this folder run. Failed messages are retried on the next run.

A message that fails on its own (expunged, rejected by the server, local write error) is recorded as failed and the batch continues. A lost connection (I/O error, timeout, protocol error, disconnected client or closed folder) ends the attempt instead, so the session reconnects rather than failing every remaining message in the batch.

Session attempts are retried with exponential backoff capped at 5 minutes behind a circuit breaker. Each retry opens a new connection and resumes every folder from its saved `LastUid`. Authentication errors and cancellation are not retried.

### Recovery

If `index.v1.db` cannot be opened, it and its `-wal`/`-shm` files are renamed to `*.corrupt.<ticks>`, a new database is created and `Messages` is rebuilt from all `.meta.json` files. `SyncState` restarts at 0; existing messages are skipped by Message-ID.

### Configuration

`appsettings.json` next to the binary; environment variables override it (`Telemetry__EnableMetrics=false`).

```json
{
  "Telemetry": {
    "ServiceName": "MyImapDownloader",
    "ServiceVersion": "1.0.0",
    "MaxFileSizeMB": 25,
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "FlushIntervalSeconds": 5,
    "MetricsExportIntervalSeconds": 15
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning", "System": "Warning" }
  }
}
```

### Telemetry

OpenTelemetry traces, metrics and logs are written as JSONL to the first writable directory of:

1. `$XDG_STATE_HOME/myimapdownloader/telemetry`
2. `$XDG_DATA_HOME/myimapdownloader/telemetry`
3. `~/.local/state/myimapdownloader/telemetry`
4. `~/.local/share/myimapdownloader/telemetry`
5. `./telemetry`

If none is writable, telemetry is disabled.

Files: `{traces|metrics|logs}/{kind}_{yyyyMMdd}_{seq:D4}.jsonl`, rotated daily and at `MaxFileSizeMB`. Properties are PascalCase and null values are omitted; tag and attribute values are strings.

Spans: `EmailArchiveSession`, `DownloadEmails`, `ProcessFolder`, `ProcessEmail`, `SaveStream`.

| Metric | Type | Unit |
|---|---|---|
| `emails.downloaded` | Counter | emails |
| `retry.attempts` | Counter | attempts |
| `storage.files.written` | Counter | files |
| `storage.bytes.written` | Counter | bytes |
| `storage.write.latency` | Histogram | ms |

.NET runtime metrics are also exported.

## MyEmailSearch

The archive root is expected to contain one directory per account, each holding MyImapDownloader output:

```
~/Documents/mail/<account>/<folder>/cur/*.eml
```

```bash
myemailsearch index --content
myemailsearch search "from:alice@example.com subject:report kafka"
myemailsearch search "date:2025-01-01..2025-06-30 invoice" --format json --limit 50
myemailsearch status
myemailsearch rebuild --yes --content
```

Global options, valid before or after the subcommand:

| Option | Alias | Description |
|---|---|---|
| `--archive` | `-a` | Archive root |
| `--database` | `-d` | Index database file |
| `--verbose` | `-v` | Debug logging |

| Command | Options |
|---|---|
| `search <query>` | `--limit/-l` (100), `--format/-f` `table\|json\|csv`, `--open/-o` |
| `index` | `--full/-f`, `--content` |
| `status` | |
| `rebuild` | `--yes/-y`, `--content` |

Without `--content`, only subject, sender and recipients are full-text indexed; a 500-character preview is always stored.

Query syntax:

| Term | Meaning |
|---|---|
| `from:addr` | Exact sender; `*` is a wildcard |
| `to:addr` | Recipient contains |
| `subject:text` or `subject:"two words"` | Subject contains |
| `date:YYYY-MM-DD` or `date:YYYY-MM-DD..YYYY-MM-DD` | Sent date range, end inclusive |
| `after:YYYY-MM-DD`, `before:YYYY-MM-DD` | Bounds, `before` inclusive |
| `account:name`, `folder:name` | Exact account or folder directory |
| remaining text | FTS5 phrase; trailing `*` for prefix match |

Defaults:

- Archive: `$MYIMAPDOWNLOADER_ARCHIVE`, then `$XDG_DATA_HOME/myimapdownloader`, `~/.local/share/myimapdownloader`, `~/Documents/mail`, `~/mail`.
- Database: `$XDG_DATA_HOME/myemailsearch/search.db`, else `~/.local/share/myemailsearch/search.db`.

Indexing is incremental by file path and modification time. Logs go to stderr, so `--format json` and `csv` output on stdout is clean. `search` exits `1` when no index exists.

## Development

```
MyImapDownloader.Core/         Shared telemetry, XDG paths, SQLite helpers
MyImapDownloader/              Downloader
MyEmailSearch/                 Search tool
*.Tests/                       TUnit tests
Directory.Packages.props       Central package versions
```

```bash
dotnet build
dotnet test
```

| Package | Use |
|---|---|
| MailKit / MimeKit | IMAP and MIME parsing |
| Microsoft.Data.Sqlite | SQLite and FTS5 |
| System.CommandLine | CLI parsing |
| Polly | Retry and circuit breaker |
| OpenTelemetry | Traces, metrics, logs |
| TUnit, NSubstitute, AwesomeAssertions | Tests |

JSON serialization uses source-generated `JsonSerializerContext`s and configuration binding uses the binder source generator.

## License

[AGPL-3.0](LICENSE).
One thing I noticed in your log but did not change: every folder reports “Found 1 new messages” even when nothing is new. That’s standard IMAP behaviour. A UID range N+1:* always matches the highest existing UID, even when that UID is ≤ N. That message is then skipped by the Message-ID check, so it’s harmless; it just makes the count misleading.




Claude is AI and can make mistakes. Please double-check responses.






Claude finished the response

04
69



















