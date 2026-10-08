using System;
using CruzNeryClinic.Services;

namespace CruzNeryClinic.Models.Email;

public class EmailSenderSettings
{
    public bool Enabled { get; set; }
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "Cruz-Nery Dental Clinic";
    public string SenderMachine { get; set; } = "";
    public int ReminderHours { get; set; } = 24;
}

public class EmailNotificationPayload
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public BillingReceiptDetail? Receipt { get; set; }
}

public class EmailNotification
{
    public long NotificationId { get; set; }
    public int PatientId { get; set; }
    public int? AppointmentId { get; set; }
    public int AppointmentRevision { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Recipient { get; set; } = "";
    public EmailNotificationPayload Payload { get; set; } = new();
    public bool PayloadUnreadable { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public string LastError { get; set; } = "";
    public string LeaseToken { get; set; } = "";
    public string Subject => Payload.Subject;
    public string PatientName { get; set; } = "";
    public string KindDisplay => Kind switch
    {
        "Confirmation" => "Appointment confirmation", "Reminder" => "Appointment reminder",
        "Rescheduled" => "Rescheduling notice", "Cancellation" => "Cancellation notice",
        "Receipt" => "Digital receipt", _ => Kind
    };
    public string CreatedAtDisplay => CreatedAtUtc.ToOffset(TimeSpan.FromHours(8)).ToString("MMM dd, yyyy h:mm tt");
    public string SentAtDisplay => SentAtUtc?.ToOffset(TimeSpan.FromHours(8)).ToString("MMM dd, yyyy h:mm tt") ?? "";
    public string Details => Status == "Sent"
        ? $"Accepted by Gmail for delivery on {SentAtDisplay}. Delivery to the patient's inbox is not guaranteed."
        : string.IsNullOrWhiteSpace(LastError) ? "Waiting for the clinic's email sender." : LastError;
}
