using CruzNeryClinic.Models;
using CruzNeryClinic.Models.Migration;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using Microsoft.Data.Sqlite;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

internal static class Program
{
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "CruzNeryMigrationChecks_" + Guid.NewGuid().ToString("N"));
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static int passed;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAILED: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(TestRoot);
        try
        {
            var extractor = new WordPatientExtractionService();
            string tableFile = Word("patients.docx", Table(
                new[] { "First Name", "Middle Name", "Last Name", "Contact Number", "Date of Birth", "Gender", "Medical History", "Unknown Column" },
                new[] { "Maria", "Santos", "Reyes", "+63 917 123 4567", "1985-06-20", "Female", "Asthma", "Review this value" },
                new[] { "Juan", "", "Cruz", "09181234567", "1970-12-31", "Male", "None", "" }));
            var rows = extractor.Extract(tableFile);
            Check(rows.Count == 2 && rows[0].FirstName == "Maria" && rows[1].LastName == "Cruz", "Extract multiple patients from Word table rows");
            Check(rows[0].HasMedicalCondition && rows[0].MedicalConditionNotes == "Asthma" && !rows[0].HasConsent,
                "Preserve medical notes and never infer consent");
            Check(rows[0].SourceText.Contains("Review this value") && rows[0].ExtractionWarnings.Count > 0,
                "Retain unmapped source values for staff review");
            using (var wordWriter = new FileStream(tableFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                var openWordRows = extractor.Extract(tableFile);
                Check(openWordRows.Count == 2 && openWordRows[0].SourceHash == rows[0].SourceHash,
                    "Extract a saved Word document while another program has it open for writing");
            }
            using (var exclusiveLock = new FileStream(tableFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool explainsLock = false;
                try { extractor.Extract(tableFile); }
                catch (IOException ex) { explainsLock = ex.Message.Contains("Save and close it in Word"); }
                Check(explainsLock, "Explain how to retry when another program exclusively locks the Word document");
            }
            using (var releasedFile = new FileStream(tableFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(releasedFile.Length > 0, "Release all importer file handles after extraction and failed attempts");
            string invalidArchive = Path.Combine(TestRoot, "invalid-archive.docx");
            File.WriteAllText(invalidArchive, "Not a Word archive");
            Check(ThrowsInvalidData(() => extractor.Extract(invalidArchive)), "Reject an invalid archive read from a document snapshot");
            using (var releasedInvalid = new FileStream(invalidArchive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(releasedInvalid.Length > 0, "Release the source file even when document parsing fails");
            string emptyTable = Word("empty.docx", Table(new[] { "First Name", "Last Name", "DOB" }, new[] { "", "", "" }));
            Check(Throws(() => extractor.Extract(emptyTable)), "Reject empty patient tables without treating headers as values");
            var mergedTable = Table(new[] { "First Name", "Last Name", "DOB" }, new[] { "Maria", "Reyes", "1985-06-20" });
            mergedTable.Elements(W + "tr").Last().Elements(W + "tc").First()
                .AddFirst(new XElement(W + "tcPr", new XElement(W + "gridSpan", new XAttribute(W + "val", "2"))));
            var merged = extractor.Extract(Word("merged.docx", mergedTable))[0];
            Check(merged.FirstName == "" && merged.SourceText.Contains("Maria") && merged.ExtractionWarnings.Count > 0,
                "Retain merged-row source and require manual mapping");
            var extra = extractor.Extract(Word("extra.docx", Table(new[] { "First Name", "Last Name" },
                new[] { "Maria", "Reyes", "Extra important information" })))[0];
            Check(extra.SourceText.Contains("Extra important information") && extra.ExtractionWarnings.Count > 0,
                "Retain extra cells instead of dropping source content");
            var abbreviated = extractor.Extract(Word("gender.docx", Table(new[] { "First Name", "Last Name", "Sex" },
                new[] { "Maria", "Reyes", "f" })))[0];
            Check(abbreviated.Gender == "Female", "Normalize common gender abbreviations");
            string fullNameFile = Word("full-name.docx", Table(new[] { "Patient Name", "DOB", "Contact Number" },
                new[] { "Reyes, Maria Santos", "1985-06-20", "09171234567" }));
            var fullName = extractor.Extract(fullNameFile)[0];
            Check(fullName.UnparsedName == "Reyes, Maria Santos" && fullName.FirstName == "" &&
                fullName.ExtractionWarnings.Count > 0, "Full-name columns require verified name separation");
            string narrative = Word("narrative.docx", new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "Unstructured clinic notes."))));
            Check(Throws(() => extractor.Extract(narrative)) && Throws(() => extractor.Extract("old.doc")), "Reject unsupported documents");
            string formFile = Word("form.docx",
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "First ")), new XElement(W + "r", new XElement(W + "t", "Name: Pedro"))),
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "Last Name: Ramos"))));
            Check(extractor.Extract(formFile)[0].FirstName == "Pedro", "Extract labeled text split across Word runs");
            string proposalFile = Word("concept-proposal.docx", Table(
                new[] { "(1) Proponents" },
                new[] { "Name", "Roles (Programmer, System Analyst, Designer, etc., please specify)" },
                new[] { "Barredo, Augustine L.", "Programmer" },
                new[] { "De Lemos, Wenifredo Jr, C.", "Project Manager" },
                new[] { "Malaga, Lawrence", "System Designer" },
                new[] { "Niez, John Alces", "System Analyst" }));
            Check(ThrowsInvalidData(() => extractor.Extract(proposalFile)),
                "Reject proposal Name/Roles tables instead of creating a patient named Roles");
            string genericNameFile = Word("name-only.docx", Table(new[] { "Name", "Project Designer" }));
            Check(ThrowsInvalidData(() => extractor.Extract(genericNameFile)),
                "Reject a generic Name field without patient identity information");
            string patientFormFile = Word("patient-form.docx", Table(
                new[] { "Patient Name", "Maria Santos Reyes" },
                new[] { "Date of Birth", "1985-06-20" }));
            var patientForm = extractor.Extract(patientFormFile)[0];
            Check(patientForm.UnparsedName == "Maria Santos Reyes" && patientForm.BirthDateText == "1985-06-20",
                "Keep labeled patient forms with full name and birth date available for staff completion");
            Check(!PatientMigrationValidationService.TryBirthDate("04/05/1985", out _) &&
                PatientMigrationValidationService.TryBirthDate("20/06/1985", out var date) && date == new DateTime(1985, 6, 20),
                "Reject ambiguous dates and accept unambiguous dates");

            var good = Draft("Maria", 1);
            good.PhoneNumber = "+63 917 123 4567";
            var validation = Validate(good);
            Check(validation.CanImport && validation.Patient!.PhoneNumber == "09171234567", "Normalize +63 mobile numbers without truncation");
            var malformedPhone = Draft("Malformed", 9);
            malformedPhone.PhoneNumber = "wrong text 09171234567";
            Check(!Validate(malformedPhone).CanImport, "Reject arbitrary text around mobile numbers");
            var invalid = Draft("Invalid", 2);
            invalid.BirthDateText = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            invalid.HasConsent = false;
            invalid.HasMedicalCondition = true;
            invalid.MedicalConditionNotes = "";
            Check(Validate(invalid).Errors.Count >= 3, "Block future birth dates, absent consent, and incomplete medical notes");
            var existing = new MigrationIdentity(42, "P042", "Maria", "", "Reyes", new DateTime(1985, 6, 20), "09171234567");
            Check(!PatientMigrationValidationService.Validate(good, new[] { existing }, new[] { good }, DateTime.Today).CanImport,
                "Block existing duplicate identity");
            var possible = existing with { FirstName = "Other", LastName = "Person" };
            good.AcknowledgeWarnings = false;
            Check(!PatientMigrationValidationService.Validate(good, new[] { possible }, new[] { good }, DateTime.Today).CanImport,
                "Require explicit acknowledgement of possible duplicates");
            good.AcknowledgeWarnings = true;
            Check(PatientMigrationValidationService.Validate(good, new[] { possible }, new[] { good }, DateTime.Today).CanImport,
                "Allow reviewed shared-contact warnings");
            var duplicate = Draft("Maria", 3);
            Check(!PatientMigrationValidationService.Validate(good, Array.Empty<MigrationIdentity>(), new[] { good, duplicate }, DateTime.Today).CanImport,
                "Block duplicate identity in a selected batch");
            good.IsReviewed = true;
            good.Address = "Corrected address";
            Check(!good.IsReviewed, "Editing an approved record clears staff approval");

            string dbPath = Path.Combine(TestRoot, "test.db");
            string connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false, ForeignKeys = true }.ToString();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE Users (UserId INTEGER PRIMARY KEY, Username TEXT);
INSERT INTO Users VALUES (1, 'test');
CREATE TABLE Services (
 ServiceId INTEGER PRIMARY KEY, ServiceName TEXT NOT NULL, DefaultPrice REAL NOT NULL, IsActive INTEGER NOT NULL);
INSERT INTO Services VALUES (1, 'Consultation', 300, 1), (2, 'Extraction', 500, 1),
 (3, 'Prophylaxis', 800, 1), (4, 'Inactive Service', 100, 0);
CREATE TABLE Patients (
 PatientId INTEGER PRIMARY KEY AUTOINCREMENT, PatientCode TEXT UNIQUE, FirstName TEXT, MiddleName TEXT,
 LastName TEXT, PhoneNumber TEXT, BirthDate TEXT, Gender TEXT, Address TEXT, IsPWD INTEGER,
 IsSeniorCitizen INTEGER, InitialTreatment TEXT, HasDataPrivacyConsent INTEGER, DataPrivacyConsentAt TEXT,
 DataPrivacyConsentVersion TEXT, IsActive INTEGER, CreatedAt TEXT);
CREATE TABLE PatientHistories (
 PatientHistoryId INTEGER PRIMARY KEY AUTOINCREMENT, PatientId INTEGER, HasMedicalCondition INTEGER,
 MedicalConditionNotes TEXT, AllergyNotes TEXT, CurrentMedication TEXT, RequiresMedicalClearance INTEGER,
 ClearanceNotes TEXT, InitialTreatmentNotes TEXT, CreatedAt TEXT,
 FOREIGN KEY(PatientId) REFERENCES Patients(PatientId));
CREATE TABLE ActivityLogs (
 LogId INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER, UserCode TEXT, Username TEXT,
 Action TEXT, Module TEXT, Description TEXT, CreatedAt TEXT);";
                command.ExecuteNonQuery();
                PatientMigrationRepository.EnsureSchema(connection);
            }
            var repository = new PatientMigrationRepository(() => new SqliteConnection(connectionString), value => value ?? "");
            SessionService.Logout();
            Check(Throws(() => repository.ImportReviewed(new[] { Draft("Ana", 4) })), "Reject imports without a signed-in user");
            Check(!SessionService.CanAccessModule("DataMigration") && ThrowsUnauthorized(() => repository.GetSnapshot()),
                "Block signed-out users from migration validation data");
            foreach (string role in new[] { "Dentist", "Secretary", "Dental Assistant", "Staff" })
            {
                SessionService.Login(new User { UserId = 1, Username = "test", UserCode = "TEST-001", Role = role });
                Check(SessionService.CanAccessModule("DataMigration") && repository.GetSnapshot().Patients.Count == 0,
                    $"Allow {role} to access migration validation");
                var sidebar = new CruzNeryClinic.ViewModels.Shared.SidebarViewModel();
                bool navigated = false;
                sidebar.NavigationRequested += _ => navigated = true;
                sidebar.DataMigrationCommand.Execute(null);
                Check(sidebar.CanAccessDataMigration && !sidebar.CanAccessAdminOnlyModules && navigated,
                    $"Allow {role} migration navigation while preserving other admin restrictions");
                var migration = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
                migration.Drafts.Add(Draft("Pending", 100));
                Check(migration.SelectDocumentsCommand.CanExecute(null) && migration.ValidateCommand.CanExecute(null) &&
                    !migration.ImportCommand.CanExecute(null), $"Allow {role} review commands but require approval and final confirmation to save");
            }
            SessionService.Login(new User { UserId = 1, Username = "test", UserCode = "TEST-001", Role = "Admin" });
            Check(SessionService.CanAccessModule("DataMigration") && repository.GetSnapshot().Patients.Count == 0, "Allow administrators to validate migration records");
            var first = Draft("Ana", 4); first.IsReviewed = true;
            var second = Draft("Luz", 5); second.IsReviewed = true;
            var ids = repository.ImportReviewed(new[] { first, second });
            Check(ids.Count == 2 && first.IsImported && Count(connectionString, "Patients") == 2 &&
                Count(connectionString, "PatientHistories") == 2 && Count(connectionString, "PatientMigrationRecords") == 2 &&
                Count(connectionString, "ActivityLogs") == 2, "Commit patients, histories, provenance, and audit records together");
            var rerun = Draft("Different", 4); rerun.IsReviewed = true;
            Check(Throws(() => repository.ImportReviewed(new[] { rerun })) && Count(connectionString, "Patients") == 2,
                "Block repeat import by document digest and record number even after edits");
            var third = Draft("Cora", 6); third.IsReviewed = true;
            var bad = Draft("Mia", 7); bad.IsReviewed = true;
            bad.HasConsent = false; // edit deliberately invalidates approval
            Check(Throws(() => repository.ImportReviewed(new[] { third, bad })) && Count(connectionString, "Patients") == 2 &&
                Count(connectionString, "PatientMigrationRecords") == 2 && Count(connectionString, "ActivityLogs") == 2 &&
                !third.IsImported, "Roll back a whole batch if a later record fails");
            var lateDuplicate = Draft("Ana", 8); lateDuplicate.IsReviewed = true;
            Check(Throws(() => repository.ImportReviewed(new[] { lateDuplicate })) && Count(connectionString, "Patients") == 2,
                "Recheck database duplicates inside the import transaction");

            SessionService.Login(new User { UserId = 1, Username = "test", UserCode = "TEST-001", Role = "Secretary" });
            var staffDraft = Draft("StaffPatient", 41); staffDraft.IsReviewed = true;
            Check(repository.ImportReviewed(new[] { staffDraft }).Count == 1, "Allow authorized non-admin staff to save approved migration records");
            Check(repository.GetHistory().Count == 3 && repository.GetHistory().All(x => x.Result == "Saved"),
                "Read durable saved migration history including staff and patient references");
            repository.RecordFailedAttempt("failed.docx", 1, "Failed", "Validation changed before saving.");
            repository.RecordFailedAttempt("unsupported.docx", 0, "Rejected", "Unsupported document layout.");
            Check(repository.GetHistory().Any(x => x.Result == "Failed") && repository.GetHistory().Any(x => x.Result == "Rejected"),
                "Track failed and rejected migration outcomes without patient inserts");

            var app = new CruzNeryClinic.App();
            app.InitializeComponent();
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            var rejectedWorkflow = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            Complete(rejectedWorkflow.LoadDocumentsAsync(new[] { proposalFile }));
            Check(rejectedWorkflow.Drafts.Count == 0 && rejectedWorkflow.CurrentStage == 1 &&
                rejectedWorkflow.FileIssues.Contains("No supported patient record") && Count(connectionString, "Patients") == 3 &&
                repository.GetHistory().Any(x => x.SourceFileName == "concept-proposal.docx" && x.Result == "Rejected"),
                "Do not display or save proposal entries; report and track the rejected upload");
            string fivePatientFile = Word("five-patients.docx", Table(
                new[] { "First Name", "Last Name", "Contact Number", "Date of Birth", "Gender", "Initial Treatment" },
                new[] { "Anika", "SampleOne", "09000000011", "1990-01-15", "Female", "Consultation" },
                new[] { "Bruno", "SampleTwo", "09000000012", "1991-02-16", "Male", "Consultation" },
                new[] { "Clara", "SampleThree", "09000000013", "1992-03-17", "Female", "Consultation" },
                new[] { "Diego", "SampleFour", "09000000014", "1993-04-18", "Male", "Consultation" },
                new[] { "Elena", "SampleFive", "09000000015", "1994-05-19", "Female", "Consultation" }));
            var fivePatientWorkflow = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            var fivePatientView = new CruzNeryClinic.Views.DataMigrationView { DataContext = fivePatientWorkflow };
            Complete(fivePatientWorkflow.LoadDocumentsAsync(new[] { fivePatientFile }));
            Render(fivePatientView, "migration-five-patients.png", 960, 640);
            var extractedList = (System.Windows.Controls.ListBox)fivePatientView.FindName("ExtractedRecordsList");
            Check(fivePatientWorkflow.ExtractedCount == 5 && extractedList.Items.Count == 5,
                "Keep all five extracted patients in the review panel");
            var patientScroll = VisualDescendants<System.Windows.Controls.ScrollViewer>(extractedList).First();
            var patientBar = VisualDescendants<System.Windows.Controls.Primitives.ScrollBar>(extractedList)
                .First(bar => bar.Orientation == System.Windows.Controls.Orientation.Vertical);
            var patientThumb = VisualDescendants<System.Windows.Controls.Primitives.Thumb>(patientBar).First();
            var visibleThumb = VisualDescendants<System.Windows.Controls.Border>(patientThumb).First();
            Check(patientScroll.ComputedVerticalScrollBarVisibility == System.Windows.Visibility.Visible &&
                visibleThumb.ActualWidth >= 6 && visibleThumb.ActualHeight > 0,
                "Display a usable scrollbar thumb when the patient list exceeds the panel height");
            patientScroll.ScrollToEnd();
            extractedList.SelectedItem = fivePatientWorkflow.Drafts[4];
            extractedList.ScrollIntoView(fivePatientWorkflow.Drafts[4]);
            Render(fivePatientView, "migration-five-patients-last.png", 960, 640);
            var lastPatientContainer = (System.Windows.Controls.ListBoxItem)extractedList.ItemContainerGenerator
                .ContainerFromItem(fivePatientWorkflow.Drafts[4]);
            var lastPatientBounds = lastPatientContainer.TransformToAncestor(extractedList).TransformBounds(
                new System.Windows.Rect(0, 0, lastPatientContainer.ActualWidth, lastPatientContainer.ActualHeight));
            Check(patientScroll.VerticalOffset > 0 && lastPatientBounds.Top >= -1 &&
                lastPatientBounds.Bottom <= extractedList.ActualHeight + 1 &&
                fivePatientWorkflow.SelectedDraft == fivePatientWorkflow.Drafts[4],
                "Scroll to and select the fifth patient in a smaller window");

            fivePatientWorkflow.Drafts[4].HasConsent = true;
            var checkInformationButton = (System.Windows.Controls.Button)fivePatientView.FindName("CheckInformationButton");
            Check(checkInformationButton.Command.CanExecute(null), "Wire Check Information to an enabled review command");
            checkInformationButton.Command.Execute(null);
            Render(fivePatientView, "migration-information-checked.png", 960, 640);
            var validationDetails = (System.Windows.Controls.Expander)fivePatientView.FindName("ValidationDetails");
            Check(fivePatientWorkflow.SelectedDraft == fivePatientWorkflow.Drafts[0] && validationDetails.IsExpanded &&
                fivePatientWorkflow.StatusMessage.Contains("4 need correction") && Count(connectionString, "Patients") == 3,
                "Show check results, open validation details, and select a patient needing correction without saving");
            var checkDialog = (System.Windows.Controls.Grid)fivePatientView.FindName("InformationCheckDialog");
            var workspace = (System.Windows.Controls.Grid)fivePatientView.FindName("MigrationWorkspace");
            Check(checkDialog.Visibility == System.Windows.Visibility.Visible && !workspace.IsEnabled &&
                fivePatientWorkflow.CheckResults.Count == 4 &&
                fivePatientWorkflow.CheckResults.Any(x => x.PatientName == "Anika SampleOne" &&
                    x.Issues.Any(issue => issue.Contains("consent"))) &&
                fivePatientWorkflow.CheckResults.All(x => x.PatientName != "Elena SampleFive"),
                "Show a popup identifying each incomplete patient and their issues while excluding records that passed");
            fivePatientWorkflow.DismissCheckResultCommand.Execute(null);
            Render(fivePatientView, "migration-check-dismissed.png", 960, 640);
            Check(!fivePatientWorkflow.IsCheckResultOpen && workspace.IsEnabled &&
                fivePatientWorkflow.SelectedDraft == fivePatientWorkflow.Drafts[0],
                "Dismiss the check popup and return to the selected patient for corrections");
            foreach (var draft in fivePatientWorkflow.Drafts) draft.HasConsent = true;
            checkInformationButton.Command.Execute(null);
            Check(fivePatientWorkflow.StatusMessage.Contains("0 need correction") &&
                fivePatientWorkflow.StatusMessage.Contains("5 await approval") && !fivePatientWorkflow.CanContinueToConfirmation,
                "Report successful information checks separately from the required staff approvals");
            Render(fivePatientView, "migration-check-passed-dialog.png", 960, 640);
            Check(fivePatientWorkflow.IsCheckResultOpen && fivePatientWorkflow.CheckPassed &&
                fivePatientWorkflow.CheckResultTitle == "Information check passed" &&
                fivePatientWorkflow.CheckResultMessage.Contains("All 5 selected patient record(s)") &&
                fivePatientWorkflow.CheckResultMessage.Contains("Review and approve") &&
                fivePatientWorkflow.CheckResults.Count == 0 && Count(connectionString, "Patients") == 3,
                "Confirm all records passed in a success popup without approving or saving them");
            var sharedPhone = fivePatientWorkflow.Drafts[4];
            sharedPhone.PhoneNumber = "09171234567";
            sharedPhone.AcknowledgeWarnings = true;
            checkInformationButton.Command.Execute(null);
            Check(fivePatientWorkflow.IsCheckResultOpen && !fivePatientWorkflow.CheckPassed &&
                fivePatientWorkflow.CheckResultTitle == "Required information is complete" &&
                fivePatientWorkflow.CheckResults.Single().PatientName == "Elena SampleFive" &&
                fivePatientWorkflow.CheckResults.Single().Issues.Any(x => x.Contains("Possible duplicate")),
                "Distinguish complete required information from possible-duplicate warnings in the check popup");
            sharedPhone.PhoneNumber = "09000000015";
            foreach (var draft in fivePatientWorkflow.Drafts) draft.IsReviewed = true;
            checkInformationButton.Command.Execute(null);
            Check(fivePatientWorkflow.StatusMessage.Contains("All selected patients are approved") &&
                fivePatientWorkflow.Drafts.All(x => x.IsReviewed) && fivePatientWorkflow.CanContinueToConfirmation &&
                Count(connectionString, "Patients") == 3,
                "Preserve valid approvals during checks and direct staff to confirmation without saving");
            foreach (var draft in fivePatientWorkflow.Drafts) draft.IncludeInImport = false;
            checkInformationButton.Command.Execute(null);
            Check(fivePatientWorkflow.StatusMessage.StartsWith("No patient records selected.") &&
                !fivePatientWorkflow.IsValidationExpanded && !fivePatientWorkflow.CanContinueToConfirmation,
                "Explain how to proceed when no extracted patient is selected for saving");
            var duplicateCheck = fivePatientWorkflow.Drafts[0];
            duplicateCheck.IncludeInImport = true;
            duplicateCheck.FirstName = "Ana";
            duplicateCheck.LastName = "Reyes";
            duplicateCheck.BirthDateText = "1985-06-20";
            checkInformationButton.Command.Execute(null);
            Check(duplicateCheck.DuplicateErrors.Count > 0 && fivePatientWorkflow.IsValidationExpanded &&
                fivePatientWorkflow.StatusMessage.Contains("1 need correction") && !duplicateCheck.IsReviewed,
                "Expose duplicate validation failures and keep approval blocked after checking");

            var missingDetailsWorkflow = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            var missingDetailsView = new CruzNeryClinic.Views.DataMigrationView { DataContext = missingDetailsWorkflow };
            Complete(missingDetailsWorkflow.LoadDocumentsAsync(new[] { fivePatientFile }));
            foreach (var draft in missingDetailsWorkflow.Drafts) draft.HasConsent = true;
            missingDetailsWorkflow.Drafts[0].PhoneNumber = "";
            missingDetailsWorkflow.Drafts[0].BirthDateText = "";
            missingDetailsWorkflow.Drafts[1].BirthDateText = "invalid";
            missingDetailsWorkflow.Drafts[2].PhoneNumber = "";
            missingDetailsWorkflow.Drafts[2].IncludeInImport = false;
            missingDetailsWorkflow.ValidateCommand.Execute(null);
            Render(missingDetailsView, "migration-missing-information-dialog.png", 960, 640);
            Check(missingDetailsWorkflow.CheckResults.Count == 2 &&
                missingDetailsWorkflow.CheckResults[0].PatientName == "Anika SampleOne" &&
                missingDetailsWorkflow.CheckResults[0].SourceDisplay.Contains("record 1") &&
                missingDetailsWorkflow.CheckResults[0].Issues.Any(x => x.Contains("Contact number")) &&
                missingDetailsWorkflow.CheckResults[0].Issues.Any(x => x.Contains("Date of birth")) &&
                missingDetailsWorkflow.CheckResults[1].PatientName == "Bruno SampleTwo" &&
                missingDetailsWorkflow.CheckResultMessage.Contains("2 of 4 selected") &&
                Count(connectionString, "Patients") == 3,
                "List specific missing or invalid fields by patient and source row, excluding unchecked records");
            missingDetailsWorkflow.DismissCheckResultCommand.Execute(null);
            missingDetailsWorkflow.ValidateCommand.Execute(null);
            missingDetailsWorkflow.ResetSession();
            Check(!missingDetailsWorkflow.IsCheckResultOpen && missingDetailsWorkflow.CheckResults.Count == 0 &&
                missingDetailsWorkflow.CheckResultTitle == "" && !missingDetailsWorkflow.CheckPassed,
                "Clear check popups and patient issue summaries when the migration session resets");

            var selectorWorkflow = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            var selectorView = new CruzNeryClinic.Views.DataMigrationView { DataContext = selectorWorkflow };
            Complete(selectorWorkflow.LoadDocumentsAsync(new[] { fivePatientFile }));
            Render(selectorView, "migration-calendar-service-fields.png");
            var birthDatePicker = (System.Windows.Controls.DatePicker)selectorView.FindName("MigrationBirthDatePicker");
            var servicePicker = (System.Windows.Controls.ComboBox)selectorView.FindName("MigrationServicePicker");
            var selectorDraft = selectorWorkflow.SelectedDraft!;
            Check(birthDatePicker.SelectedDate == new DateTime(1990, 1, 15) &&
                ReferenceEquals(birthDatePicker.Style, app.Resources["AppointmentDatePickerStyle"]) &&
                birthDatePicker.Template.FindName("PART_Button", birthDatePicker) is System.Windows.Controls.Button,
                "Use the appointment calendar picker and display the extracted birth date");
            Check(!servicePicker.IsEditable && selectorWorkflow.ServiceOptions.Select(x => x.ServiceName)
                .SequenceEqual(new[] { "Consultation", "Extraction", "Prophylaxis" }) &&
                ((AppointmentServiceOption?)servicePicker.SelectedItem)?.ServiceName == "Consultation",
                "Use a selection-only dropdown populated from the shared active appointment services");
            var medicalSection = (System.Windows.Controls.Expander)
                ((System.Windows.FrameworkElement)servicePicker.Parent).Parent;
            medicalSection.IsExpanded = true;
            Render(selectorView, "migration-service-display-layout.png");
            servicePicker.BringIntoView();
            Render(selectorView, "migration-service-display.png");
            Check(VisualDescendants<System.Windows.Controls.TextBlock>(servicePicker).Any(x => x.Text == "Consultation") &&
                !VisualDescendants<System.Windows.Controls.TextBlock>(servicePicker).Any(x =>
                    x.Text.Contains("CruzNeryClinic.Models.AppointmentServiceOption")),
                "Display the selected service name instead of the model class name in the dropdown");
            selectorDraft.HasConsent = true;
            selectorDraft.IsReviewed = true;
            birthDatePicker.SelectedDate = new DateTime(1990, 6, 4);
            Check(selectorDraft.BirthDateText == "1990-06-04" && !selectorDraft.IsReviewed &&
                selectorDraft.ValidatedPatient?.BirthDate == new DateTime(1990, 6, 4),
                "Write calendar selections in an unambiguous format and require fresh approval after a date change");
            selectorDraft.BirthDateText = "04/05/1995";
            Render(selectorView, "migration-ambiguous-birth-date.png");
            Check(birthDatePicker.SelectedDate == null && selectorDraft.BirthDateText == "04/05/1995" &&
                selectorDraft.HasUnresolvedBirthDate && selectorDraft.HasValidationErrors,
                "Keep an ambiguous Word date visible without guessing or clearing it when the calendar is blank");
            birthDatePicker.SelectedDate = new DateTime(1995, 5, 4);
            Check(selectorDraft.BirthDateText == "1995-05-04" && !selectorDraft.HasUnresolvedBirthDate &&
                !selectorDraft.HasValidationErrors,
                "Resolve an ambiguous source date using the calendar");
            birthDatePicker.SelectedDate = null;
            Check(selectorDraft.BirthDateText == "" && selectorDraft.HasValidationErrors,
                "Treat clearing a valid calendar date as missing required information");
            selectorDraft.BirthDateText = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");
            Render(selectorView, "migration-invalid-age-calendar.png");
            Check(selectorDraft.HasValidationErrors && birthDatePicker.SelectedDate == DateTime.Today.AddYears(1),
                "Retain invalid extracted ages for review and block approval using the existing date validation");
            selectorDraft.BirthDateText = "1990-01-15";
            selectorDraft.IsReviewed = true;
            servicePicker.SelectedItem = selectorWorkflow.ServiceOptions.Single(x => x.ServiceName == "Extraction");
            Check(selectorDraft.InitialTreatment == "Extraction" && !selectorDraft.IsReviewed &&
                selectorDraft.SourceText.Contains("Consultation"),
                "Update the initial service from the dropdown, clear approval, and preserve the original Word information");
            Render(selectorView, "migration-selected-service-display.png");
            Check(VisualDescendants<System.Windows.Controls.TextBlock>(servicePicker).Any(x => x.Text == "Extraction") &&
                ((AppointmentServiceOption)servicePicker.SelectedItem).ServiceName == selectorDraft.InitialTreatment,
                "Refresh the visible service name when staff choose a different treatment");
            selectorDraft.InitialTreatment = "Legacy denture fitting";
            Render(selectorView, "migration-legacy-service.png");
            Check(servicePicker.SelectedItem == null && selectorWorkflow.HasUnlistedInitialTreatment &&
                selectorDraft.InitialTreatment == "Legacy denture fitting" &&
                selectorWorkflow.UnlistedInitialTreatmentMessage.Contains("Legacy denture fitting"),
                "Display and retain a historical service that is absent from the active appointment options");
            selectorWorkflow.SelectedDraft = selectorWorkflow.Drafts[1];
            Render(selectorView, "migration-patient-service-switch.png");
            Check(((AppointmentServiceOption?)servicePicker.SelectedItem)?.ServiceName == "Consultation" &&
                birthDatePicker.SelectedDate == new DateTime(1991, 2, 16),
                "Update both selectors to the selected patient's own extracted values");
            selectorWorkflow.SelectedDraft = selectorDraft;
            selectorWorkflow.ValidateCommand.Execute(null);
            Check(selectorDraft.InitialTreatment == "Legacy denture fitting" &&
                selectorWorkflow.ServiceOptions.Count == 3 && Count(connectionString, "Patients") == 3,
                "Keep unmatched extracted services during catalog refreshes without saving any patient");
            selectorWorkflow.DismissCheckResultCommand.Execute(null);
            selectorWorkflow.SelectedDraft = selectorDraft;
            Render(selectorView, "migration-service-retained-after-check.png");
            Check(selectorWorkflow.HasUnlistedInitialTreatment && servicePicker.SelectedItem == null &&
                selectorWorkflow.UnlistedInitialTreatmentMessage.Contains("Legacy denture fitting"),
                "Show the preserved historical service when returning to the patient after information checks");
            selectorWorkflow.ResetSession();
            Check(selectorWorkflow.ServiceOptions.Count == 0 && selectorWorkflow.SelectedInitialService == null &&
                !selectorWorkflow.HasUnlistedInitialTreatment,
                "Clear migration selector state with the rest of the temporary session");

            var workflow = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            var view = new CruzNeryClinic.Views.DataMigrationView { DataContext = workflow };
            Check(view.Content != null && workflow.IsUploadStage && !workflow.ImportCommand.CanExecute(null),
                "Start the guided process at upload with saving disabled");
            Check(!workflow.ExtractDocumentsCommand.CanExecute(null), "Disable extraction until a Word file is selected");
            Render(view, "migration-upload.png");
            workflow.SelectDocuments(new[] { tableFile });
            Check(workflow.IsUploadStage && workflow.Drafts.Count == 0 && workflow.SelectedFileNames.Contains("patients.docx") &&
                workflow.ExtractDocumentsCommand.CanExecute(null) && Count(connectionString, "Patients") == 3,
                "Show the selected filename without extracting or saving records");
            Render(view, "migration-selected-file.png");
            workflow.ViewHistoryCommand.Execute(null);
            Check(workflow.IsHistoryStage && workflow.DisplayedHistoryItems.Any(x => x.Result == "Saved") &&
                !workflow.ExtractDocumentsCommand.CanExecute(null), "Open history separately from the upload workflow");
            workflow.BackCommand.Execute(null);
            Check(workflow.IsUploadStage && workflow.SelectedFileNames.Contains("patients.docx") &&
                workflow.ExtractDocumentsCommand.CanExecute(null), "Return from history with the selected Word file preserved");
            Complete(workflow.ExtractDocumentsCommand.ExecuteAsync(null));
            Check(workflow.IsReviewStage && workflow.Drafts.Count == 2 && Count(connectionString, "Patients") == 3,
                "Extract patient records into review only after the explicit extraction action");
            Check(workflow.Drafts.All(x => x.ValidationErrors.Count > 0) && !workflow.ContinueCommand.CanExecute(null),
                "Display missing information and block progression until staff completes review");
            var draftBeforeHistory = workflow.SelectedDraft;
            workflow.ViewHistoryCommand.Execute(null);
            workflow.BackCommand.Execute(null);
            Check(workflow.IsReviewStage && workflow.SelectedDraft == draftBeforeHistory && workflow.Drafts.Count == 2,
                "Preserve patient drafts and selection when returning from migration history");
            Render(view, "migration-review.png");
            var approved = workflow.Drafts[0];
            var excluded = workflow.Drafts[1];
            excluded.IncludeInImport = false;
            approved.HasConsent = true;
            approved.AcknowledgeWarnings = true;
            approved.IsReviewed = true;
            Check(workflow.CanContinueToConfirmation, "Allow corrected and approved records while excluding incomplete drafts");
            workflow.ContinueCommand.Execute(null);
            Check(workflow.IsConfirmStage && workflow.ConfirmationRecords.Count == 1 &&
                !workflow.HasConfirmedSave && !workflow.ImportCommand.CanExecute(null),
                "Require explicit staff confirmation before saving the approved selection");
            approved.Address = "Corrected after preview";
            Check(workflow.IsReviewStage && !approved.IsReviewed && !workflow.HasConfirmedSave,
                "Return to review and clear approval when confirmed information is edited");
            approved.IsReviewed = true;
            workflow.ContinueCommand.Execute(null);
            workflow.HasConfirmedSave = true;
            Check(workflow.ImportCommand.CanExecute(null), "Enable save only after record approval and final confirmation");
            workflow.ViewHistoryCommand.Execute(null);
            workflow.BackCommand.Execute(null);
            Check(workflow.IsConfirmStage && approved.IsReviewed && !workflow.HasConfirmedSave && !workflow.ImportCommand.CanExecute(null),
                "Require fresh final confirmation after returning from history");
            workflow.HasConfirmedSave = true;
            Render(view, "migration-confirmation.png");
            Complete(workflow.ImportCommand.ExecuteAsync(null));
            Check(workflow.IsCompletionStage && approved.IsImported && !excluded.IsImported && Count(connectionString, "Patients") == 4,
                "Save only the approved patient and display a separate completion screen");
            Check(workflow.HistoryItems.Any(x => x.PatientName == "Maria Santos Reyes" && x.Result == "Saved") &&
                workflow.HistoryItems.Any(x => x.Result == "Rejected"), "Track success, rejection, and failure persistently");
            Check(workflow.ViewResultsCommand.CanExecute(null), "Offer View Results after successful migration");
            Render(view, "migration-complete.png");
            workflow.ViewResultsCommand.Execute(null);
            Check(workflow.IsHistoryStage && workflow.DisplayedHistoryItems.Count() == 1 &&
                workflow.DisplayedHistoryItems.All(x => x.Result == "Saved" && x.PatientName == "Maria Santos Reyes"),
                "Show the completed migration results without unrelated earlier outcomes");
            Render(view, "migration-results.png");
            workflow.BackCommand.Execute(null);
            Check(workflow.IsCompletionStage, "Return from results to the completion screen");
            workflow.ViewHistoryCommand.Execute(null);
            Check(workflow.DisplayedHistoryItems.Any(x => x.Result == "Rejected") && workflow.HistoryTitle == "Migration History",
                "Keep complete history available separately from the completed batch results");
            Render(view, "migration-history.png");
            var newSession = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            newSession.ViewHistoryCommand.Execute(null);
            Check(newSession.IsHistoryStage && newSession.HistoryItems.Count == workflow.HistoryItems.Count,
                "Keep migration results available in a new module session");
            newSession.BackCommand.Execute(null);
            Check(newSession.IsUploadStage && !newSession.HasSelectedDocuments, "Return a fresh history session to Upload");
            int savedPatientsBeforeReset = (int)Count(connectionString, "Patients");
            int trackedResultsBeforeReset = repository.GetHistory().Count;
            workflow.ResetSession();
            Check(workflow.IsUploadStage && workflow.Drafts.Count == 0 && workflow.ConfirmationRecords.Count == 0 &&
                workflow.HistoryItems.Count == 0 && !workflow.HasSelectedDocuments && workflow.SelectedDraft == null &&
                workflow.SelectedHistoryItem == null && !workflow.HasConfirmedSave && !workflow.IsValidationExpanded &&
                !workflow.IsBusy && workflow.FileIssues == "" && workflow.ResultMessage == "" &&
                !workflow.ImportCommand.CanExecute(null),
                "Reset filenames, drafts, approvals, errors, and results to a fresh upload screen");
            Check(Count(connectionString, "Patients") == savedPatientsBeforeReset &&
                repository.GetHistory().Count == trackedResultsBeforeReset,
                "Preserve saved patients and durable migration tracking when the session is reset");
            workflow.ViewHistoryCommand.Execute(null);
            Check(workflow.HistoryItems.Count == trackedResultsBeforeReset && workflow.HistoryTitle == "Migration History",
                "Reload durable migration history after clearing the temporary session");
            var resetInFlight = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            resetInFlight.SelectDocuments(new[] { fivePatientFile });
            Task pendingExtraction = resetInFlight.LoadDocumentsAsync(new[] { fivePatientFile });
            resetInFlight.ResetSession();
            Complete(pendingExtraction);
            Check(resetInFlight.IsUploadStage && resetInFlight.Drafts.Count == 0 && !resetInFlight.HasSelectedDocuments &&
                !resetInFlight.IsBusy && resetInFlight.StatusMessage == "Select a Word patient record to begin.",
                "Prevent a pending extraction from restoring drafts or errors after session cleanup");
            var lockedUpload = new CruzNeryClinic.ViewModels.DataMigrationViewModel(repository);
            lockedUpload.SelectDocuments(new[] { tableFile });
            using (var exclusiveLock = new FileStream(tableFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Complete(lockedUpload.ExtractDocumentsCommand.ExecuteAsync(null));
            Check(lockedUpload.IsUploadStage && lockedUpload.Drafts.Count == 0 &&
                lockedUpload.FileIssues.Contains("Save and close it in Word") && !lockedUpload.IsBusy,
                "Display actionable file-lock feedback and leave the migration ready for retry");
            lockedUpload.ResetSession();
            lockedUpload.SelectDocuments(new[] { tableFile });
            Complete(lockedUpload.ExtractDocumentsCommand.ExecuteAsync(null));
            Check(lockedUpload.IsReviewStage && lockedUpload.Drafts.Count == 2 && lockedUpload.FileIssues == "" &&
                Count(connectionString, "Patients") == savedPatientsBeforeReset,
                "Clear the previous file-lock error and extract successfully after the document is unlocked");
            var detachedDraft = lockedUpload.Drafts[0];
            lockedUpload.ResetSession();
            detachedDraft.Address = "Edit on an old session draft";
            Check(lockedUpload.Drafts.Count == 0 && lockedUpload.StatusMessage == "Select a Word patient record to begin.",
                "Detach draft change handlers when the migration session ends");

            var brokenHistory = new CruzNeryClinic.ViewModels.DataMigrationViewModel(
                new PatientMigrationRepository(() => throw new IOException("Database unavailable."), value => value ?? ""));
            brokenHistory.ViewHistoryCommand.Execute(null);
            Check(brokenHistory.IsHistoryStage && brokenHistory.StatusMessage.Contains("tracking is unavailable"),
                "Keep a history connectivity error visible instead of reporting an empty history");
            var brokenValidation = new CruzNeryClinic.ViewModels.DataMigrationViewModel(
                new PatientMigrationRepository(() => throw new IOException("Database unavailable."), value => value ?? ""));
            brokenValidation.Drafts.Add(Draft("ValidationFailure", 100));
            brokenValidation.ContinueCommand.Execute(null);
            brokenValidation.ValidateCommand.Execute(null);
            Check(brokenValidation.StatusMessage.Contains("Patient validation is unavailable:") &&
                !brokenValidation.CanContinueToConfirmation && !brokenValidation.Drafts[0].IsReviewed,
                "Report database validation failures without successful-check feedback or approval");
            Check(brokenValidation.IsCheckResultOpen && !brokenValidation.CheckPassed &&
                brokenValidation.CheckResultTitle == "Information check unavailable" &&
                brokenValidation.CheckResultMessage.Contains("Database unavailable.") && !brokenValidation.HasCheckResults,
                "Show database validation failures in the popup without reporting success");
            SessionService.Logout();
            Check(ThrowsUnauthorized(() => newSession.SelectDocuments(new[] { tableFile })) &&
                !newSession.SelectDocumentsCommand.CanExecute(null) && !newSession.ExtractDocumentsCommand.CanExecute(null),
                "Protect both file selection and extraction from signed-out users");
            Check(ThrowsUnauthorized(() => repository.GetActiveServices()),
                "Protect migration service options from unauthenticated access");
            Check(ThrowsUnauthorized(() => repository.GetHistory()) &&
                ThrowsUnauthorized(() => repository.RecordFailedAttempt("blocked.docx", 0, "Rejected", "blocked")),
                "Protect history and tracking from unauthenticated users");

            Console.WriteLine($"All {passed} migration checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            SessionService.Logout();
            SqliteConnection.ClearAllPools();
            // Only remove the unique directory created by this test process.
            if (Path.GetFileName(TestRoot).StartsWith("CruzNeryMigrationChecks_", StringComparison.Ordinal))
                Directory.Delete(TestRoot, recursive: true);
        }
    }

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static IEnumerable<T> VisualDescendants<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }


    private static void Render(System.Windows.FrameworkElement view, string fileName, int width = 1100, int height = 820)
    {
        string previewDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "buildcheck"));
        Directory.CreateDirectory(previewDirectory);
        view.Measure(new System.Windows.Size(width, height));
        view.Arrange(new System.Windows.Rect(0, 0, width, height));
        view.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(previewDirectory, fileName));
        encoder.Save(stream);
    }
    private static PatientMigrationDraft Draft(string name, int record) => new()
    {
        FirstName = name, LastName = "Reyes", PhoneNumber = "09171234567",
        BirthDateText = "1985-06-20", Gender = "Female", InitialTreatment = "Consultation",
        SourceFileName = "test.docx", SourceHash = new string('A', 64), SourceRecordNumber = record,
        HasConsent = true, AcknowledgeWarnings = true, IncludeInImport = true
    };
    private static MigrationValidation Validate(PatientMigrationDraft draft) =>
        PatientMigrationValidationService.Validate(draft, Array.Empty<MigrationIdentity>(), new[] { draft }, DateTime.Today);
    private static bool ThrowsInvalidData(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static bool ThrowsUnauthorized(Action action) { try { action(); return false; } catch (UnauthorizedAccessException) { return true; } }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
    private static long Count(string cs, string table)
    {
        using var connection = new SqliteConnection(cs);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table; // test-only internal table names
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static XElement Table(params string[][] rows) => new(W + "tbl", rows.Select(row =>
        new XElement(W + "tr", row.Select(cell => new XElement(W + "tc",
            new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", cell))))))));
    private static string Word(string name, params XElement[] body)
    {
        string path = Path.Combine(TestRoot, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry("word/document.xml").Open();
        new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "body", body))).Save(stream);
        return path;
    }
}
