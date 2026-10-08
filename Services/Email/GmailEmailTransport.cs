using System;
using System.Threading;
using System.Threading.Tasks;
using CruzNeryClinic.Models.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CruzNeryClinic.Services.Email;

public interface IEmailTransport
{
    Task SendAsync(EmailSenderSettings settings, string password, EmailNotification notification, CancellationToken cancellationToken);
}

public sealed class GmailEmailTransport : IEmailTransport
{
    public async Task SendAsync(EmailSenderSettings settings, string password, EmailNotification notification, CancellationToken cancellationToken)
    {
        using var message = CreateMessage(settings, notification);
        using var client = new SmtpClient { Timeout = 30000 };
        await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(settings.SenderEmail, password, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        // A disconnect error after Gmail accepted the message must not trigger another send.
        try { await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false); }
        catch (Exception) { }
    }

    public static MimeMessage CreateMessage(EmailSenderSettings settings, EmailNotification notification)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(notification.Recipient));
        message.Subject = notification.Subject;
        // Stable across retries; SMTP acceptance and the database commit cannot be atomic.
        message.MessageId = $"clinic-{notification.NotificationId}-{notification.CreatedAtUtc.UtcTicks}@{settings.SenderEmail.Split('@')[1]}";
        var builder = new BodyBuilder { TextBody = notification.Payload.Body };
        if (notification.Payload.Receipt is { } receipt)
            builder.Attachments.Add($"Receipt-{receipt.BillingId}.pdf", ReceiptPDFService.GenerateReceiptBytes(receipt), new ContentType("application", "pdf"));
        message.Body = builder.ToMessageBody();
        return message;
    }
}
