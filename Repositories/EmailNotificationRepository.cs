using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CruzNeryClinic.Data;
using CruzNeryClinic.Models;
using CruzNeryClinic.Models.Email;
using CruzNeryClinic.Services;
using Microsoft.Data.Sqlite;

namespace CruzNeryClinic.Repositories;

public class EmailNotificationRepository
{
    private readonly Func<SqliteConnection> getConnection;
    private readonly Func<string?, string> encrypt, decrypt;
    public EmailNotificationRepository() : this(DatabaseService.GetConnection, CryptoService.EncryptString, CryptoService.DecryptString) { }
    public EmailNotificationRepository(Func<SqliteConnection> connection, Func<string?, string> encrypt, Func<string?, string> decrypt)
    { getConnection = connection; this.encrypt = encrypt; this.decrypt = decrypt; }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql)
    { var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql; return command; }
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static void EnsureSchema(SqliteConnection db)
    {
        void AddColumn(string table, string name, string definition)
        {
            using var inspect = db.CreateCommand();
            inspect.CommandText = $"PRAGMA table_info({table});";
            bool found = false;
            using (var reader = inspect.ExecuteReader())
                while (reader.Read()) if (reader.GetString(1) == name) found = true;
            if (found) return;
            using var alter = db.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {definition};";
            alter.ExecuteNonQuery();
        }
        AddColumn("Patients", "EmailAddress", "TEXT NOT NULL DEFAULT ''");
        AddColumn("Patients", "EmailNotificationsEnabled", "INTEGER NOT NULL DEFAULT 0");
        AddColumn("Appointments", "EmailRevision", "INTEGER NOT NULL DEFAULT 1");
        using var command = db.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS EmailSenderSettings (
 SettingsId INTEGER PRIMARY KEY CHECK(SettingsId = 1), Enabled INTEGER NOT NULL DEFAULT 0,
 SenderEmail TEXT NOT NULL DEFAULT '', SenderName TEXT NOT NULL DEFAULT 'Cruz-Nery Dental Clinic',
 SenderMachine TEXT NOT NULL DEFAULT '', ReminderHours INTEGER NOT NULL DEFAULT 24);
INSERT OR IGNORE INTO EmailSenderSettings(SettingsId) VALUES(1);
CREATE TABLE IF NOT EXISTS EmailOutbox (
 NotificationId INTEGER PRIMARY KEY AUTOINCREMENT, EventKey TEXT NOT NULL UNIQUE,
 PatientId INTEGER NOT NULL, AppointmentId INTEGER, AppointmentRevision INTEGER NOT NULL DEFAULT 0,
 Kind TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'Pending'
 CHECK(Status IN ('Pending','Sending','Sent','Failed','Skipped')),
 Recipient TEXT NOT NULL, Payload TEXT NOT NULL, AttemptCount INTEGER NOT NULL DEFAULT 0,
 CreatedAtUtc TEXT NOT NULL, NextAttemptAtUtc TEXT NOT NULL, SentAtUtc TEXT,
 LeaseToken TEXT, LeaseUntilUtc TEXT, LastError TEXT NOT NULL DEFAULT '',
 FOREIGN KEY(PatientId) REFERENCES Patients(PatientId), FOREIGN KEY(AppointmentId) REFERENCES Appointments(AppointmentId));
CREATE INDEX IF NOT EXISTS idx_email_outbox_due ON EmailOutbox(Status, NextAttemptAtUtc);";
        command.ExecuteNonQuery();
    }

    private static (string Address, string Name)? Recipient(SqliteConnection db, SqliteTransaction? tx,
        int patientId, Func<string?, string> decrypt)
    {
        using var command = Command(db, tx, @"SELECT FirstName, LastName, EmailAddress,
 EmailNotificationsEnabled, IsActive, HasDataPrivacyConsent FROM Patients WHERE PatientId=@Patient;");
        command.AddIntParameter("@Patient", patientId);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || Convert.ToInt32(reader["EmailNotificationsEnabled"]) != 1 ||
            Convert.ToInt32(reader["IsActive"]) != 1 || Convert.ToInt32(reader["HasDataPrivacyConsent"]) != 1) return null;
        string address = decrypt(reader["EmailAddress"].ToString()).Trim();
        return EmailAddressValidation.IsValid(address)
            ? (address, $"{reader["FirstName"]} {reader["LastName"]}".Trim()) : null;
    }

    private static void Insert(SqliteConnection db, SqliteTransaction? tx, string key, int patientId,
        int? appointmentId, int revision, string kind, string address, EmailNotificationPayload payload,
        Func<string?, string> encrypt, DateTimeOffset now)
    {
        using var command = Command(db, tx, @"INSERT OR IGNORE INTO EmailOutbox
 (EventKey, PatientId, AppointmentId, AppointmentRevision, Kind, Recipient, Payload, CreatedAtUtc, NextAttemptAtUtc)
 VALUES(@Key,@Patient,@Appointment,@Revision,@Kind,@Recipient,@Payload,@Now,@Now);");
        command.AddTextParameter("@Key", key).AddIntParameter("@Patient", patientId)
            .AddNullableParameter("@Appointment", appointmentId).AddIntParameter("@Revision", revision)
            .AddTextParameter("@Kind", kind).AddTextParameter("@Recipient", encrypt(address))
            .AddTextParameter("@Payload", encrypt(JsonSerializer.Serialize(payload))).AddTextParameter("@Now", Stamp(now));
        command.ExecuteNonQuery();
    }

    public static void QueueAppointment(SqliteConnection db, SqliteTransaction? tx, int appointmentId, string kind,
        Func<string?, string>? encrypt = null, Func<string?, string>? decrypt = null, DateTimeOffset? clock = null)
    {
        encrypt ??= CryptoService.EncryptString; decrypt ??= CryptoService.DecryptString;
        int patientId, revision; string date, time, type, status;
        using (var command = Command(db, tx, @"SELECT PatientId, EmailRevision, AppointmentDate,
 AppointmentTime, AppointmentType, Status FROM Appointments WHERE AppointmentId=@Id;"))
        {
            command.AddIntParameter("@Id", appointmentId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return;
            patientId = Convert.ToInt32(reader["PatientId"]); revision = Convert.ToInt32(reader["EmailRevision"]);
            date = reader["AppointmentDate"].ToString()!; time = reader["AppointmentTime"].ToString()!;
            type = reader["AppointmentType"].ToString()!; status = reader["Status"].ToString()!;
        }
        if (kind is "Rescheduled" or "Cancellation")
        {
            using var obsolete = Command(db, tx, @"UPDATE EmailOutbox SET Status='Skipped', LastError=@Reason,
 LeaseToken=NULL, LeaseUntilUtc=NULL WHERE AppointmentId=@Id AND Kind<>'Receipt' AND Status IN ('Pending','Failed');");
            obsolete.AddIntParameter("@Id", appointmentId).AddTextParameter("@Reason", encrypt("Replaced by a newer appointment update."));
            obsolete.ExecuteNonQuery();
        }
        if (type != "Scheduled" || (kind == "Cancellation" ? status != "Cancelled" : status != "Scheduled")) return;
        var recipient = Recipient(db, tx, patientId, decrypt);
        if (recipient == null) return;
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            !TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var at)) return;
        DateTime appointment = day.Add(at);
        string heading = kind switch { "Confirmation" => "Appointment confirmed", "Reminder" => "Appointment reminder",
            "Rescheduled" => "Appointment rescheduled", "Cancellation" => "Appointment cancelled", _ => throw new ArgumentException("Unknown appointment email kind.") };
        string body = $"Hello {recipient.Value.Name},\n\n" +
            (kind == "Cancellation" ? "Your appointment has been cancelled." : kind == "Rescheduled" ? "Your appointment has been rescheduled." :
             kind == "Reminder" ? "This is a reminder of your upcoming appointment." : "Your appointment has been confirmed.") +
            $"\n\nDate: {appointment:dddd, MMMM d, yyyy}\nTime: {appointment:h:mm tt} (Philippine time)\n" +
            "\nCruz-Nery Dental Clinic\n33 B Rodriguez Highway, San Jose, Rodriguez, Rizal\n" +
            "\nPlease contact the clinic if you have questions about this appointment.";
        Insert(db, tx, $"Appointment:{appointmentId}:{revision}:{kind}", patientId, appointmentId, revision, kind,
            recipient.Value.Address, new() { Subject = $"{heading} | Cruz-Nery Dental Clinic", Body = body }, encrypt, clock ?? DateTimeOffset.UtcNow);
    }

    public static void QueueReceipt(SqliteConnection db, SqliteTransaction? tx, int paymentId, BillingReceiptDetail receipt,
        Func<string?, string>? encrypt = null, Func<string?, string>? decrypt = null, DateTimeOffset? clock = null)
    {
        encrypt ??= CryptoService.EncryptString; decrypt ??= CryptoService.DecryptString;
        var recipient = Recipient(db, tx, receipt.PatientId, decrypt);
        if (recipient == null) return;
        Insert(db, tx, $"Receipt:Payment:{paymentId}", receipt.PatientId, null, 0, "Receipt", recipient.Value.Address,
            new() { Subject = $"Digital receipt {receipt.ReceiptNumber} | Cruz-Nery Dental Clinic",
                Body = $"Hello {recipient.Value.Name},\n\nThank you for your payment. Your digital receipt is attached as a PDF.\n\nCruz-Nery Dental Clinic",
                Receipt = receipt }, encrypt, clock ?? DateTimeOffset.UtcNow);
    }

    public EmailSenderSettings GetSettings()
    {
        using var db = getConnection(); db.Open();
        using var command = db.CreateCommand("SELECT * FROM EmailSenderSettings WHERE SettingsId=1;");
        using var reader = command.ExecuteReader();
        return reader.Read() ? new() { Enabled = Convert.ToInt32(reader["Enabled"]) == 1,
            SenderEmail = decrypt(reader["SenderEmail"].ToString()), SenderName = reader["SenderName"].ToString()!,
            SenderMachine = reader["SenderMachine"].ToString()!, ReminderHours = Convert.ToInt32(reader["ReminderHours"]) } : new();
    }

    public void SaveSettings(EmailSenderSettings settings)
    {
        if (!SessionService.IsAdmin) throw new UnauthorizedAccessException("Only administrators can configure the clinic email sender.");
        if (settings.Enabled && (!EmailAddressValidation.IsValid(settings.SenderEmail) ||
            string.IsNullOrWhiteSpace(settings.SenderMachine))) throw new ArgumentException("Enter a valid Gmail address and designate the sending computer.");
        if (settings.ReminderHours is < 1 or > 168) throw new ArgumentException("Reminder lead time must be between 1 and 168 hours.");
        using var db = getConnection(); db.Open();
        using var command = db.CreateCommand(@"UPDATE EmailSenderSettings SET Enabled=@Enabled, SenderEmail=@Email,
 SenderName=@Name, SenderMachine=@Machine, ReminderHours=@Hours WHERE SettingsId=1;");
        command.AddBoolParameter("@Enabled", settings.Enabled).AddTextParameter("@Email", encrypt(settings.SenderEmail.Trim()))
            .AddTextParameter("@Name", settings.SenderName.Trim()).AddTextParameter("@Machine", settings.SenderMachine)
            .AddIntParameter("@Hours", settings.ReminderHours);
        command.ExecuteNonQuery();
    }

    public void QueueDueReminders(DateTimeOffset now, int hours)
    {
        using var db = getConnection(); db.Open();
        using var tx = db.BeginTransaction(deferred: false);
        var ids = new List<int>();
        using (var command = Command(db, tx, @"SELECT AppointmentId, AppointmentDate, AppointmentTime
 FROM Appointments WHERE Status='Scheduled' AND AppointmentType='Scheduled'
 AND AppointmentDate BETWEEN @Start AND @End;"))
        {
            command.AddTextParameter("@Start", now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .AddTextParameter("@End", now.AddHours(hours).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    if (!DateTime.TryParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                        !TimeSpan.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, out var time)) continue;
                    DateTime date = day.Add(time);
                    var at = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
                    if (at > now && at <= now.AddHours(hours)) ids.Add(reader.GetInt32(0));
                }
        }
        foreach (int id in ids) QueueAppointment(db, tx, id, "Reminder", encrypt, decrypt, now);
        tx.Commit();
    }

    public IReadOnlyList<EmailNotification> GetRecent()
    {
        if (!SessionService.CanAccessModule("EmailNotifications")) throw new UnauthorizedAccessException("Sign in to view email notifications.");
        using var db = getConnection(); db.Open();
        using var command = db.CreateCommand(@"SELECT e.*, p.FirstName || ' ' || p.LastName AS PatientName
 FROM EmailOutbox e LEFT JOIN Patients p ON p.PatientId=e.PatientId ORDER BY e.NotificationId DESC LIMIT 200;");
        using var reader = command.ExecuteReader(); var result = new List<EmailNotification>();
        while (reader.Read()) result.Add(Read(reader));
        return result;
    }

    private EmailNotification Read(SqliteDataReader reader)
    {
        EmailNotificationPayload payload;
        bool unreadable = false;
        try { payload = JsonSerializer.Deserialize<EmailNotificationPayload>(decrypt(reader["Payload"].ToString())) ?? throw new JsonException(); }
        catch (JsonException) { payload = new() { Subject = "Unreadable stored message" }; unreadable = true; }
        return new()
        {
            NotificationId = reader.GetInt64(reader.GetOrdinal("NotificationId")), PatientId = Convert.ToInt32(reader["PatientId"]),
            AppointmentId = reader["AppointmentId"] == DBNull.Value ? null : Convert.ToInt32(reader["AppointmentId"]),
            AppointmentRevision = Convert.ToInt32(reader["AppointmentRevision"]), Kind = reader["Kind"].ToString()!,
            Status = reader["Status"].ToString()!, Recipient = decrypt(reader["Recipient"].ToString()),
            Payload = payload, PayloadUnreadable = unreadable,
            AttemptCount = Convert.ToInt32(reader["AttemptCount"]), CreatedAtUtc = DateTimeOffset.Parse(reader["CreatedAtUtc"].ToString()!, CultureInfo.InvariantCulture),
            SentAtUtc = reader["SentAtUtc"] == DBNull.Value ? null : DateTimeOffset.Parse(reader["SentAtUtc"].ToString()!, CultureInfo.InvariantCulture),
            LastError = unreadable ? "The stored message could not be decrypted or read. Check the database encryption key and backup." : decrypt(reader["LastError"].ToString()), LeaseToken = reader["LeaseToken"].ToString() ?? "",
            PatientName = reader["PatientName"].ToString() ?? ""
        };
    }

    public EmailNotification? ClaimNext(DateTimeOffset now)
    {
        using var db = getConnection(); db.Open();
        using var tx = db.BeginTransaction(deferred: false);
        using (var recover = Command(db, tx, @"UPDATE EmailOutbox SET Status='Pending', LeaseToken=NULL, LeaseUntilUtc=NULL
 WHERE Status='Sending' AND LeaseUntilUtc<@Now;"))
        { recover.AddTextParameter("@Now", Stamp(now)); recover.ExecuteNonQuery(); }
        long? id;
        using (var select = Command(db, tx, @"SELECT NotificationId FROM EmailOutbox
 WHERE Status='Pending' AND NextAttemptAtUtc<=@Now ORDER BY NotificationId LIMIT 1;"))
        { select.AddTextParameter("@Now", Stamp(now)); object? value = select.ExecuteScalar(); id = value == null ? null : Convert.ToInt64(value); }
        if (id == null) { tx.Commit(); return null; }
        string token = Guid.NewGuid().ToString("N");
        using (var claim = Command(db, tx, @"UPDATE EmailOutbox SET Status='Sending', AttemptCount=AttemptCount+1,
 LeaseToken=@Token, LeaseUntilUtc=@Until WHERE NotificationId=@Id AND Status='Pending';"))
        { claim.AddParameter("@Id", id).AddTextParameter("@Token", token).AddTextParameter("@Until", Stamp(now.AddMinutes(5))); claim.ExecuteNonQuery(); }
        EmailNotification result;
        using (var select = Command(db, tx, @"SELECT e.*, p.FirstName || ' ' || p.LastName AS PatientName
 FROM EmailOutbox e LEFT JOIN Patients p ON p.PatientId=e.PatientId WHERE e.NotificationId=@Id;"))
        { select.AddParameter("@Id", id); using var reader = select.ExecuteReader(); reader.Read(); result = Read(reader); }
        tx.Commit(); return result;
    }

    public string? GetObsoleteReason(EmailNotification message, DateTimeOffset now)
    {
        if (message.PayloadUnreadable) return "The stored email is unreadable. Check the database encryption key and backup.";
        if (string.IsNullOrWhiteSpace(message.Subject) || string.IsNullOrWhiteSpace(message.Payload.Body) ||
            (message.Kind == "Receipt" && message.Payload.Receipt == null))
            return "The stored email is incomplete. Review the source record and backup.";
        using var db = getConnection(); db.Open();
        var recipient = Recipient(db, null, message.PatientId, decrypt);
        if (recipient == null || !string.Equals(recipient.Value.Address, message.Recipient, StringComparison.OrdinalIgnoreCase))
            return "The patient email address, email preference, consent, or active status changed. Review the patient record.";
        if (message.AppointmentId == null) return null;
        using var command = db.CreateCommand("SELECT EmailRevision, Status, AppointmentDate, AppointmentTime FROM Appointments WHERE AppointmentId=@Id;");
        command.AddIntParameter("@Id", message.AppointmentId.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || Convert.ToInt32(reader["EmailRevision"]) != message.AppointmentRevision)
            return "Replaced by a newer appointment update.";
        string status = reader["Status"].ToString()!;
        if (message.Kind == "Cancellation") return status == "Cancelled" ? null : "The appointment is no longer cancelled.";
        if (!DateTime.TryParseExact(reader["AppointmentDate"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            !TimeSpan.TryParse(reader["AppointmentTime"].ToString(), CultureInfo.InvariantCulture, out var time))
            return "The appointment date or time is invalid. Review the appointment record.";
        DateTime date = day.Add(time);
        var at = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
        return status != "Scheduled" || at <= now ? "The appointment is no longer scheduled in the future." : null;
    }

    public void Finish(EmailNotification message, string status, string error, DateTimeOffset now)
    {
        using var db = getConnection(); db.Open();
        using var command = db.CreateCommand(@"UPDATE EmailOutbox SET Status=@Status, LastError=@Error,
 SentAtUtc=@Sent, NextAttemptAtUtc=@Next, LeaseToken=NULL, LeaseUntilUtc=NULL
 WHERE NotificationId=@Id AND Status='Sending' AND LeaseToken=@Token;");
        command.AddTextParameter("@Status", status).AddTextParameter("@Error", encrypt(error))
            .AddNullableParameter("@Sent", status == "Sent" ? Stamp(now) : null)
            .AddTextParameter("@Next", Stamp(now.AddMinutes(Math.Min(60, Math.Pow(2, message.AttemptCount)))))
            .AddParameter("@Id", message.NotificationId).AddTextParameter("@Token", message.LeaseToken);
        command.ExecuteNonQuery();
    }

    public void RetryFailed(long id)
    {
        if (!SessionService.CanAccessModule("EmailNotifications")) throw new UnauthorizedAccessException("Sign in to retry a failed email.");
        using var db = getConnection(); db.Open();
        using var command = db.CreateCommand(@"UPDATE EmailOutbox SET Status='Pending', NextAttemptAtUtc=@Now,
 AttemptCount=0, LastError='' WHERE NotificationId=@Id AND Status='Failed';");
        command.AddParameter("@Id", id).AddTextParameter("@Now", Stamp(DateTimeOffset.UtcNow)); command.ExecuteNonQuery();
    }
}
