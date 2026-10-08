using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using CruzNeryClinic.Data;
using CruzNeryClinic.Models;
using CruzNeryClinic.Models.Email;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using CruzNeryClinic.Services.Email;
using CruzNeryClinic.ViewModels;
using Microsoft.Data.Sqlite;
using MimeKit;

internal static class Program
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ClinicEmailChecks_" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static string ConnectionString => $"Data Source={Path.Combine(Root, "test.db")};Foreign Keys=True;Default Timeout=5";
    private static SqliteConnection Connection() => new(ConnectionString);
    private static int passed;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAILED: " + label); passed++; Console.WriteLine("PASS: " + label); }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
    private static bool Unauthorized(Action action) { try { action(); return false; } catch (UnauthorizedAccessException) { return true; } }
    private static void Sql(string sql)
    { using var db = Connection(); db.Open(); using var command = db.CreateCommand(sql); command.ExecuteNonQuery(); }
    private static object Value(string sql)
    { using var db = Connection(); db.Open(); using var command = db.CreateCommand(sql); return command.ExecuteScalar()!; }
    private static string Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        byte[] bytes = Encoding.UTF8.GetBytes(value), nonce = RandomNumberGenerator.GetBytes(12), cipher = new byte[bytes.Length], tag = new byte[16];
        using var aes = new AesGcm(Key, 16); aes.Encrypt(nonce, bytes, cipher, tag);
        return "ENC:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    private static string Decrypt(string? value)
    {
        if (value == null) return "";
        if (!value.StartsWith("ENC:")) return value;
        byte[] bytes = Convert.FromBase64String(value[4..]), plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(Key, 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
    private static void NoAudit(string action, string module, string description) { }
    private static Appointment Appointment(int patientId, int hours = 4)
    {
        DateTime time = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddHours(hours).DateTime;
        return new() { PatientId = patientId, AppointmentType = "Scheduled", AppointmentDate = time.Date,
            AppointmentTime = new(time.Hour, time.Minute, 0), ServiceName = "Consultation", Priority = "Scheduled", Status = "Scheduled" };
    }
    private static EmailSenderSettings Settings(bool enabled = true, string machine = "TEST-SENDER") => new()
    { Enabled = enabled, SenderEmail = "clinic.test@gmail.com", SenderName = "Test Clinic", SenderMachine = machine, ReminderHours = 24 };

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            using (var db = Connection())
            {
                db.Open();
                // Run production schema migrations only on this unique, empty fixture.
                foreach (string method in new[] { "CreateTables", "EnsurePatientConsentSchema", "EnsureBillingInvoiceSchema", "EnsureAppointmentSchema" })
                    typeof(DatabaseInitializer).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { db });
                EmailNotificationRepository.EnsureSchema(db); EmailNotificationRepository.EnsureSchema(db);
            }
            var repository = new EmailNotificationRepository(Connection, Encrypt, Decrypt);
            var appointments = new AppointmentRepository(Connection, Encrypt, Decrypt, NoAudit);
            var billing = new BillingRepository(Connection, Encrypt, Decrypt, NoAudit);
            var patients = new PatientRepository(Connection, Encrypt, Decrypt);
            Check(!repository.GetSettings().Enabled && Convert.ToInt32(Value("SELECT COUNT(*) FROM EmailSenderSettings")) == 1,
                "Schema migration is repeatable and Gmail delivery starts disabled");
            Check(EmailAddressValidation.IsValid("patient@example.com") && !EmailAddressValidation.IsValid("Jane <patient@example.com>") &&
                !EmailAddressValidation.IsValid("invalid") && !EmailAddressValidation.IsValid("x@example.com\r\nBcc: x@example.com"),
                "Reject incomplete email addresses, display-name input, and header injection");
            SessionService.Logout();
            Check(Unauthorized(() => repository.GetRecent()) && Unauthorized(() => repository.RetryFailed(1)) && Unauthorized(() => repository.SaveSettings(Settings())),
                "Signed-out users cannot inspect, retry, or configure notifications");
            SessionService.Login(new User { Role = "Secretary", UserId = 1 });
            Check(repository.GetRecent().Count == 0 && Unauthorized(() => repository.SaveSettings(Settings())),
                "Staff can monitor email; Gmail configuration is restricted to administrators");
            SessionService.Login(new User { Role = "Admin", UserId = 1 });
            Check(Throws(() => repository.SaveSettings(new() { Enabled = true, SenderEmail = "bad", SenderMachine = "TEST-SENDER" })),
                "Reject enabling an invalid email sender");
            repository.SaveSettings(Settings(false));

            var patient = new Patient { FirstName = "Test", LastName = "Patient", EmailAddress = "patient@example.com", EmailNotificationsEnabled = true,
                PhoneNumber = "09171234567", BirthDate = new(1990, 4, 9), Gender = "Female", HasDataPrivacyConsent = true };
            patient.PatientId = patients.AddPatient(patient);
            var restored = patients.GetPatientById(patient.PatientId)!;
            Check(restored.EmailAddress == patient.EmailAddress && restored.EmailNotificationsEnabled &&
                Value("SELECT EmailAddress FROM Patients LIMIT 1").ToString()!.StartsWith("ENC:"), "Persist patient email encrypted and load the email preference");
            patient.EmailNotificationsEnabled = false; patients.UpdatePatient(patient);
            Check(!patients.GetPatientById(patient.PatientId)!.EmailNotificationsEnabled, "Persist an updated patient email preference");
            var withoutPreference = Appointment(patient.PatientId);
            appointments.AddAppointment(withoutPreference);
            Check(repository.GetRecent().Count == 0, "Do not email a patient without email authorization");
            patient.EmailNotificationsEnabled = true; patients.UpdatePatient(patient);
            var appointment = Appointment(patient.PatientId);
            appointment.AppointmentId = appointments.AddAppointment(appointment);
            Check(repository.GetRecent().Single().Kind == "Confirmation" && repository.GetRecent().Single().Status == "Pending", "Scheduling queues a confirmation even while delivery is disabled");
            using (var db = Connection())
            { db.Open(); EmailNotificationRepository.QueueAppointment(db, null, appointment.AppointmentId, "Confirmation", Encrypt, Decrypt); }
            Check(repository.GetRecent().Count == 1, "Repeated confirmation requests cannot duplicate the event");
            var confirmation = repository.GetRecent().Single();
            Check(Value("SELECT Recipient FROM EmailOutbox LIMIT 1").ToString()!.StartsWith("ENC:") &&
                Value("SELECT Payload FROM EmailOutbox LIMIT 1").ToString()!.StartsWith("ENC:") &&
                confirmation.Payload.Body.Contains("Philippine time") && !confirmation.Payload.Body.Contains("Consultation"), "Protect the queue recipient and payload and omit treatment details from appointment emails");
            var transport = new FakeTransport(); var secrets = new MemoryCredentials();
            using var delivery = new EmailDeliveryService(repository, secrets, transport, "TEST-SENDER");
            delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(transport.Sent.Count == 0 && repository.GetRecent().Single().Status == "Pending", "Disabled Gmail leaves local work and queued messages intact");
            repository.SaveSettings(Settings(machine: "OTHER-PC")); delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(transport.Sent.Count == 0, "Only the designated LAN computer processes messages");
            repository.SaveSettings(Settings()); secrets.Password = null; delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(transport.Sent.Count == 0 && delivery.LastStatus.Contains("password"), "Missing local Gmail credentials do not discard pending notifications");
            secrets.Password = "test-password";
            repository.QueueDueReminders(DateTimeOffset.UtcNow, 24); repository.QueueDueReminders(DateTimeOffset.UtcNow, 24);
            // The first opted-out appointment now has authorization; both future appointments qualify.
            Check(repository.GetRecent().Count(x => x.Kind == "Reminder") == 2, "Future appointments receive one reminder per appointment revision");
            Sql("DELETE FROM EmailOutbox WHERE Kind='Reminder'");
            var firstClaim = repository.ClaimNext(DateTimeOffset.UtcNow)!;
            Check(firstClaim.NotificationId == confirmation.NotificationId && repository.ClaimNext(DateTimeOffset.UtcNow) == null, "Exclusive SQLite claims prevent two senders from taking the same message");
            string lease = firstClaim.LeaseToken; firstClaim.LeaseToken = "wrong";
            repository.Finish(firstClaim, "Sent", "", DateTimeOffset.UtcNow);
            Check(repository.GetRecent().Single().Status == "Sending", "An obsolete sender lease cannot overwrite the current result");
            firstClaim.LeaseToken = lease;
            var recovered = repository.ClaimNext(DateTimeOffset.UtcNow.AddMinutes(6))!;
            Check(recovered.NotificationId == firstClaim.NotificationId && recovered.LeaseToken != lease && recovered.AttemptCount == 2,
                "An interrupted application releases its expired sending lease on the next run");
            repository.Finish(recovered, "Pending", "Recovered", DateTimeOffset.UtcNow);
            Sql("UPDATE EmailOutbox SET NextAttemptAtUtc='2000-01-01T00:00:00.000Z'");
            transport.Fail = true;
            delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            var offline = repository.GetRecent().First(x => x.NotificationId == confirmation.NotificationId);
            Check(offline.Status == "Pending" && offline.LastError.Contains("automatically") &&
                DateTimeOffset.Parse(Value($"SELECT NextAttemptAtUtc FROM EmailOutbox WHERE NotificationId={offline.NotificationId}").ToString()!) > DateTimeOffset.UtcNow,
                "Offline sends retain their message and schedule a delayed retry");
            Sql("DELETE FROM EmailOutbox WHERE Kind='Reminder'; UPDATE EmailOutbox SET AttemptCount=4, NextAttemptAtUtc='2000-01-01T00:00:00.000Z'");
            delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(repository.GetRecent().First(x => x.NotificationId == confirmation.NotificationId).Status == "Failed", "Five unsuccessful delivery attempts become a tracked failure");
            repository.RetryFailed(confirmation.NotificationId);
            Check(repository.GetRecent().First(x => x.NotificationId == confirmation.NotificationId).AttemptCount == 0, "Staff retry resets the attempt count without creating another message");
            transport.Fail = false; delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(repository.GetRecent().First(x => x.NotificationId == confirmation.NotificationId).Status == "Sent" && transport.Sent.Count > 0,
                "A successful retry records Gmail acceptance and a sent timestamp");
            int accepted = transport.Sent.Count;
            delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(transport.Sent.Count == accepted, "Previously sent notices and reminders are not resent");

            Sql("DELETE FROM EmailOutbox");
            var revised = Appointment(patient.PatientId, 28); revised.AppointmentId = appointments.AddAppointment(revised);
            revised.AppointmentDate = revised.AppointmentDate.AddDays(1); appointments.RescheduleAppointment(revised);
            Check(repository.GetRecent().Count(x => x.Kind == "Confirmation" && x.Status == "Skipped") == 1 &&
                repository.GetRecent().Count(x => x.Kind == "Rescheduled" && x.Status == "Pending") == 1,
                "Rescheduling replaces an unsent confirmation with the new appointment date");
            appointments.CancelAppointment(revised.AppointmentId, "Patient requested"); appointments.CancelAppointment(revised.AppointmentId);
            Check(repository.GetRecent().Count(x => x.Kind == "Cancellation") == 1 &&
                repository.GetRecent().Single(x => x.Kind == "Rescheduled").Status == "Skipped", "Cancellation replaces older unsent notices and repeated cancellation is idempotent");
            var cancellation = repository.GetRecent().Single(x => x.Kind == "Cancellation");
            Check(repository.GetObsoleteReason(cancellation, DateTimeOffset.UtcNow) == null, "A current cancellation notice remains deliverable");
            patient.EmailAddress = "new.patient@example.com"; patients.UpdatePatient(patient);
            Check(repository.GetObsoleteReason(cancellation, DateTimeOffset.UtcNow) != null, "An email address change prevents sending an older queued recipient");
            patient.EmailAddress = "patient@example.com"; patients.UpdatePatient(patient);
            Sql("DELETE FROM EmailOutbox");
            var near = Appointment(patient.PatientId, 4); near.AppointmentId = appointments.AddAppointment(near);
            var old = repository.GetRecent().Single();
            Sql($"UPDATE Appointments SET EmailRevision=EmailRevision+1 WHERE AppointmentId={near.AppointmentId}");
            Check(repository.GetObsoleteReason(old, DateTimeOffset.UtcNow) != null, "A claimed old revision is checked again before delivery");
            Sql($"UPDATE Appointments SET EmailRevision=EmailRevision-1 WHERE AppointmentId={near.AppointmentId}");
            Check(repository.GetObsoleteReason(old, DateTimeOffset.UtcNow.AddDays(2)) != null, "Expired appointment confirmations and reminders are skipped");
            patient.HasDataPrivacyConsent = false; patients.UpdatePatient(patient);
            Check(repository.GetObsoleteReason(old, DateTimeOffset.UtcNow) != null, "Revoked privacy consent suppresses pending delivery");
            patient.HasDataPrivacyConsent = true; patients.UpdatePatient(patient);
            var walkIn = Appointment(patient.PatientId); walkIn.AppointmentType = "Walk-In"; walkIn.Status = "Waiting";
            int before = repository.GetRecent().Count; appointments.AddAppointment(walkIn);
            Check(repository.GetRecent().Count == before, "Walk-in patients do not receive scheduled appointment confirmations");

            Sql("DELETE FROM EmailOutbox");
            int invoice = billing.CreateBillingTransaction(new() { PatientId = patient.PatientId, ReceiptNumber = "TEST-0001", ServiceName = "Consultation",
                TotalAmount = 1000, SubtotalAfterDiscount = 1000, RemainingBalance = 1000 });
            billing.AddPaymentRecord(new() { PatientId = patient.PatientId, BillingId = invoice, AmountPaid = 400 });
            var receiptMail = repository.GetRecent().Single();
            Check(receiptMail.Kind == "Receipt" && receiptMail.Payload.Receipt!.AmountPaid == 400 && receiptMail.Payload.Receipt.RemainingBalance == 600 &&
                receiptMail.Payload.Receipt.PaymentHistory.Single().AmountPaid == 400, "Payment queues a PDF receipt snapshot that includes the newly committed payment");
            billing.AddPaymentRecord(new() { PatientId = patient.PatientId, BillingId = invoice, AmountPaid = 600 });
            Check(repository.GetRecent().Count == 2 && repository.GetRecent().Single(x => x.NotificationId == receiptMail.NotificationId).Payload.Receipt!.AmountPaid == 400 &&
                repository.GetRecent().First().Payload.Receipt!.AmountPaid == 1000, "Each payment has its own immutable receipt snapshot");
            using (var mime = GmailEmailTransport.CreateMessage(Settings(), receiptMail))
            {
                var attachment = (MimePart)mime.Attachments.Single();
                using var pdf = new MemoryStream(); attachment.Content!.DecodeTo(pdf);
                Check(mime.To.Mailboxes.Single().Address == "patient@example.com" && attachment.ContentType.MimeType == "application/pdf" &&
                    Encoding.ASCII.GetString(pdf.ToArray(), 0, 5) == "%PDF-", "Digital receipt email contains an actual PDF attachment from the existing receipt layout");
                using var retryMime = GmailEmailTransport.CreateMessage(Settings(), receiptMail);
                Check(mime.MessageId == retryMime.MessageId, "Retries reuse a stable email Message-ID");
            }
            int paymentCount = Convert.ToInt32(Value("SELECT COUNT(*) FROM PaymentRecords"));
            int appointmentCount = Convert.ToInt32(Value("SELECT COUNT(*) FROM Appointments"));
            Sql("CREATE TRIGGER block_email BEFORE INSERT ON EmailOutbox BEGIN SELECT RAISE(ABORT, 'Simulated queue failure'); END;");
            Check(Throws(() => appointments.AddAppointment(Appointment(patient.PatientId))) &&
                Convert.ToInt32(Value("SELECT COUNT(*) FROM Appointments")) == appointmentCount, "A queue-write failure rolls back the appointment so no notice event is lost");
            Check(Throws(() => billing.AddPaymentRecord(new() { PatientId = patient.PatientId, BillingId = invoice, AmountPaid = 100 })) &&
                Convert.ToInt32(Value("SELECT COUNT(*) FROM PaymentRecords")) == paymentCount && billing.GetBillingReceiptDetail(invoice)!.AmountPaid == 1000,
                "A queue-write failure rolls back the payment and billing totals together");
            Sql("DROP TRIGGER block_email");

            var protectedStore = new EmailCredentialStore(Path.Combine(Root, "credentials.bin"));
            Check(Throws(() => protectedStore.Save("clinic.test@gmail.com", "normal-password")), "Reject ordinary Gmail passwords");
            protectedStore.Save("clinic.test@gmail.com", "abcd efgh ijkl mnop");
            Check(protectedStore.Read("clinic.test@gmail.com") == "abcdefghijklmnop" && protectedStore.Read("other@gmail.com") == null &&
                !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Root, "credentials.bin"))).Contains("abcdefghijklmnop"),
                "Gmail app password is protected by Windows DPAPI and bound to its sending address");
            Check(!new Patient().EmailNotificationsEnabled && !new CruzNeryClinic.Models.Migration.PatientMigrationDraft().EmailNotificationsEnabled,
                "New and imported records require staff to explicitly confirm email authorization");
            var draft = new CruzNeryClinic.Models.Migration.PatientMigrationDraft { FirstName = "Test", LastName = "Patient", PhoneNumber = "09171234567",
                BirthDateText = "1990-04-09", Gender = "Female", HasConsent = true, InitialTreatment = "Consultation", EmailAddress = "bad-email" };
            var invalid = PatientMigrationValidationService.Validate(draft, Array.Empty<MigrationIdentity>(), new[] { draft }, DateTime.Today);
            Check(invalid.Errors.Any(x => x.Contains("Email address")), "Migration validation rejects an invalid extracted email address");
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string wordPath = Path.Combine(Root, "sample.docx");
            using (var zip = ZipFile.Open(wordPath, ZipArchiveMode.Create))
            using (var stream = zip.CreateEntry("word/document.xml").Open())
                new XDocument(new XElement(w + "document", new XElement(w + "body", new XElement(w + "tbl",
                    new[] { new[] { "First Name", "Last Name", "Email Address" }, new[] { "Test", "Patient", "patient@example.com" } }.Select(row =>
                        new XElement(w + "tr", row.Select(cell => new XElement(w + "tc", new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", cell))))))))))).Save(stream);
            Check(new WordPatientExtractionService().Extract(wordPath).Single().EmailAddress == "patient@example.com", "Word table extraction maps patient email headers");
            Sql("UPDATE EmailOutbox SET Payload='invalid JSON' WHERE NotificationId=(SELECT MIN(NotificationId) FROM EmailOutbox)");
            Check(repository.GetRecent().Any(x => x.PayloadUnreadable), "An unreadable payload remains visible without blocking notification history");
            delivery.ProcessPendingAsync().GetAwaiter().GetResult();
            Check(repository.GetRecent().Any(x => x.PayloadUnreadable && x.Status == "Skipped"), "Unreadable stored messages are tracked and do not block the delivery queue");

            var app = new CruzNeryClinic.App(); app.InitializeComponent();
            var viewModel = new EmailNotificationsViewModel(repository, secrets, delivery);
            var view = new CruzNeryClinic.Views.EmailNotificationsView { DataContext = viewModel };
            Render(view, "email-notifications.png", 1100, 820);
            Check(viewModel.Notifications.Count == repository.GetRecent().Count && viewModel.CanConfigure, "Notification view loads actual tracked results for an administrator");
            viewModel.OpenSettingsCommand.Execute(null); Render(view, "email-gmail-settings.png", 960, 640);
            Check(viewModel.IsSettingsOpen && viewModel.SenderEmail == Settings().SenderEmail, "Gmail settings show the existing sender and reminder lead time");
            var saveButton = (System.Windows.Controls.Button)view.FindName("SaveGmailSettingsButton");
            double bottom = saveButton.TransformToAncestor(view).Transform(new System.Windows.Point(0, 0)).Y + saveButton.ActualHeight;
            Check(saveButton.ActualHeight > 0 && bottom <= 616, "Gmail settings keep Save and Cancel accessible in a smaller window");
            var emailInput = (System.Windows.Controls.TextBox)view.FindName("SenderEmailInput");
            var contentHost = (System.Windows.Controls.ScrollViewer)emailInput.Template.FindName("PART_ContentHost", emailInput);
            Check(emailInput.Text == Settings().SenderEmail && contentHost.ActualHeight >= emailInput.FontSize,
                "The Gmail address renders inside the styled settings input without clipping");
            viewModel.SenderEmail = "invalid-email"; viewModel.SaveSettingsCommand.Execute(null);
            Check(viewModel.IsSettingsOpen && viewModel.StatusMessage.Contains("valid") && repository.GetSettings().SenderEmail == Settings().SenderEmail,
                "Invalid Gmail settings stay open with feedback and leave the configured sender intact");
            viewModel.SenderEmail = Settings().SenderEmail; viewModel.Enabled = false; viewModel.SaveSettingsCommand.Execute(null);
            Check(!viewModel.IsSettingsOpen && !repository.GetSettings().Enabled && repository.GetSettings().SenderMachine == Environment.MachineName,
                "An administrator can save settings and designate this computer without sending an email");
            viewModel.CloseSettingsCommand.Execute(null);
            SessionService.Login(new User { Role = "Secretary" });
            var staffViewModel = new EmailNotificationsViewModel(repository, secrets, delivery);
            staffViewModel.OpenSettingsCommand.Execute(null);
            Check(!staffViewModel.CanConfigure && !staffViewModel.IsSettingsOpen && !staffViewModel.OpenSettingsCommand.CanExecute(null), "Staff settings are hidden and protected even when a command is invoked directly");
            Render(new CruzNeryClinic.Views.EmailNotificationsView { DataContext = staffViewModel }, "email-staff-view.png", 960, 640);
            var sidebar = new CruzNeryClinic.ViewModels.Shared.SidebarViewModel(); string target = "";
            sidebar.NavigationRequested += module => target = module; sidebar.EmailNotificationsCommand.Execute(null);
            Check(target == "EmailNotifications", "Sidebar navigation exposes the email notification module to signed-in staff");
            Console.WriteLine($"All {passed} email notification checks passed. No real email was sent and no clinic database was used.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            SessionService.Logout(); SqliteConnection.ClearAllPools();
            string resolved = Path.GetFullPath(Root);
            if (Path.GetDirectoryName(resolved) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(resolved).StartsWith("ClinicEmailChecks_", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
    private static void Render(System.Windows.FrameworkElement view, string name, int width, int height)
    {
        view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("buildcheck"); using var file = File.Create(Path.Combine("buildcheck", name)); png.Save(file);
    }
    private sealed class MemoryCredentials : IEmailCredentialStore
    {
        public string? Password = "test-password";
        public string? Read(string email) => Password;
        public void Save(string email, string password) => Password = password;
    }
    private sealed class FakeTransport : IEmailTransport
    {
        public bool Fail;
        public List<EmailNotification> Sent { get; } = new();
        public Task SendAsync(EmailSenderSettings settings, string password, EmailNotification message, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Fail) throw new IOException("Offline fixture"); Sent.Add(message); return Task.CompletedTask; }
    }
}
