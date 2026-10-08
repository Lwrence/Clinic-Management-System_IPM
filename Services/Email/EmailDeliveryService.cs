using System;
using System.Threading;
using System.Threading.Tasks;
using CruzNeryClinic.Repositories;
using MailKit.Security;

namespace CruzNeryClinic.Services.Email;

public sealed class EmailDeliveryService : IDisposable
{
    public static EmailDeliveryService Current { get; } = new(new(), new EmailCredentialStore(), new GmailEmailTransport(), Environment.MachineName);
    private readonly EmailNotificationRepository repository;
    private readonly IEmailCredentialStore credentials;
    private readonly IEmailTransport transport;
    private readonly string machine;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private Task? loop;
    public string LastStatus { get; private set; } = "Email delivery has not started.";
    public EmailDeliveryService(EmailNotificationRepository repository, IEmailCredentialStore credentials, IEmailTransport transport, string machine)
    { this.repository = repository; this.credentials = credentials; this.transport = transport; this.machine = machine; }

    public void Start() => loop ??= Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try { await ProcessPendingAsync(stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception) { LastStatus = "The email queue is temporarily unavailable. Delivery will retry in one minute."; }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    });

    public async Task<string> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        var token = linked.Token;
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return "Delivery is already running.";
        try
        {
            var settings = repository.GetSettings();
            if (!settings.Enabled) return LastStatus = "Email delivery is disabled. An administrator can configure Gmail.";
            if (!string.Equals(settings.SenderMachine, machine, StringComparison.OrdinalIgnoreCase))
                return LastStatus = $"Delivery runs on {settings.SenderMachine} while the clinic app is open.";
            string? password = credentials.Read(settings.SenderEmail);
            if (string.IsNullOrEmpty(password)) return LastStatus = "Gmail app password is missing on this Windows account. Ask an administrator to configure it.";
            repository.QueueDueReminders(DateTimeOffset.UtcNow, settings.ReminderHours);
            int sent = 0, failed = 0, skipped = 0;
            for (int count = 0; count < 25; count++)
            {
                token.ThrowIfCancellationRequested();
                var current = repository.GetSettings();
                if (!current.Enabled || current.SenderMachine != settings.SenderMachine || current.SenderEmail != settings.SenderEmail) break;
                var message = repository.ClaimNext(DateTimeOffset.UtcNow);
                if (message == null) break;
                try
                {
                    string? reason = repository.GetObsoleteReason(message, DateTimeOffset.UtcNow);
                    if (reason != null) { repository.Finish(message, "Skipped", reason, DateTimeOffset.UtcNow); skipped++; continue; }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    await transport.SendAsync(settings, password, message, timeout.Token).ConfigureAwait(false);
                    repository.Finish(message, "Sent", "", DateTimeOffset.UtcNow); sent++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    repository.Finish(message, "Pending", "Delivery stopped when the application closed. It will retry later.", DateTimeOffset.UtcNow);
                    throw;
                }
                catch (Exception ex)
                {
                    bool authentication = ex is AuthenticationException;
                    repository.Finish(message, authentication || message.AttemptCount >= 5 ? "Failed" : "Pending",
                        authentication ? "Gmail rejected authentication. Check the app password and 2-Step Verification, then retry." :
                        message.AttemptCount >= 5 ? "Delivery failed after five attempts. Check the internet connection and Gmail settings, then retry." :
                        "Could not send through Gmail. Check the internet connection; another attempt is scheduled automatically.", DateTimeOffset.UtcNow);
                    failed++;
                    // An unavailable sender affects the whole queue; avoid exhausting every record at once.
                    break;
                }
            }
            return LastStatus = $"Delivery checked: {sent} sent, {failed} unsuccessful, {skipped} skipped. Pending messages are retried automatically.";
        }
        finally { gate.Release(); }
    }
    public void Dispose() => stop.Cancel();
}
