using CruzNeryClinic.Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace CruzNeryClinic.Data
{
    public static class DatabaseInitializer
    {
        private static readonly HashSet<string> AllowedSchemaTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "Appointments",
            "BillingTransactionItems",
            "BillingTransactions",
            "Patients",
            "PaymentRecords",
            "TreatmentRecords",
            "Users"
        };

        public static void Initialize()
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            EnableForeignKeys(connection);
            MigrateInventoryTables(connection);
            CreateTables(connection);
            EnsurePatientConsentSchema(connection);
            EnsureUserSchema(connection);
            // Security questions must be seeded before admin accounts,
            // because Users now store SecurityQuestionId1, 2, and 3.
            SeedDefaultSecurityQuestions(connection);

            SeedDefaultAdminAccounts(connection);
            MergeCruzNeryAdminDentistAccount(connection);
            SeedDefaultServices(connection);
            EnsureUpdatedClinicServices(connection);
            EnsureBillingInvoiceSchema(connection);
            EnsureAppointmentSchema(connection);
            CruzNeryClinic.Repositories.PatientMigrationRepository.EnsureSchema(connection);
        }

        private static void EnsurePatientConsentSchema(SqliteConnection connection)
        {
            if (!ColumnExists(connection, "Patients", "HasDataPrivacyConsent"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Patients
        ADD COLUMN HasDataPrivacyConsent INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Patients", "DataPrivacyConsentAt"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Patients
        ADD COLUMN DataPrivacyConsentAt TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Patients", "DataPrivacyConsentVersion"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Patients
        ADD COLUMN DataPrivacyConsentVersion TEXT;";
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureUserSchema(SqliteConnection connection)
        {
            if (!ColumnExists(connection, "Users", "CreatedByUserId"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Users
        ADD COLUMN CreatedByUserId INTEGER;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Users", "IsDentistRole"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Users
        ADD COLUMN IsDentistRole INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Users", "HasEmployeePrivacyAcknowledgement"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Users
        ADD COLUMN HasEmployeePrivacyAcknowledgement INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Users", "EmployeePrivacyAcknowledgedAt"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Users
        ADD COLUMN EmployeePrivacyAcknowledgedAt TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Users", "EmployeePrivacyAcknowledgementVersion"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Users
        ADD COLUMN EmployeePrivacyAcknowledgementVersion TEXT;";
                command.ExecuteNonQuery();
            }

            MergeCruzNeryAdminDentistAccount(connection);
        }

        // Adds the appointment columns/tables introduced after the initial schema:
        // treatment detail columns and the AppointmentImages table that stores
        // uploaded teeth photo paths.
        private static void EnsureAppointmentSchema(SqliteConnection connection)
        {
            if (!ColumnExists(connection, "Appointments", "ServiceStage"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Appointments
        ADD COLUMN ServiceStage TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Appointments", "FollowUpDate"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Appointments
        ADD COLUMN FollowUpDate TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "Appointments", "TreatmentDetails"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE Appointments
        ADD COLUMN TreatmentDetails TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "TreatmentRecords", "ServiceStage"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN ServiceStage TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "TreatmentRecords", "FollowUpDate"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN FollowUpDate TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "TreatmentRecords", "TreatmentDetails"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN TreatmentDetails TEXT;";
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        CREATE TABLE IF NOT EXISTS AppointmentImages (
            AppointmentImageId INTEGER PRIMARY KEY AUTOINCREMENT,
            AppointmentId INTEGER NOT NULL,
            FilePath TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,

            FOREIGN KEY (AppointmentId)
                REFERENCES Appointments(AppointmentId)
        );";
                command.ExecuteNonQuery();
            }
        }

        private static void EnableForeignKeys(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
        }

        // Drops the inventory tables if they use the old schema so CreateTables can
        // recreate them with the correct column layout.
        private static void MigrateInventoryTables(SqliteConnection connection)
        {
            bool hasOldSchema = false;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(InventoryItems);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string col = reader.GetString(1);
                    if (col == "Notes" || col == "CreatedAt" || col == "LastRestockDate")
                    {
                        hasOldSchema = true;
                        break;
                    }
                }
            }

            if (!hasOldSchema) return;

            foreach (string drop in new[]
            {
                "DROP TABLE IF EXISTS InventoryUsage;",
                "DROP TABLE IF EXISTS InventoryRestocks;",
                "DROP TABLE IF EXISTS InventoryItems;"
            })
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = drop;
                cmd.ExecuteNonQuery();
            }
        }

        private static void CreateTables(SqliteConnection connection)
        {
            // This SQL block creates all tables needed by the clinic system.
            // The database is created only if the tables do not exist yet.

            string sql = @"
CREATE TABLE IF NOT EXISTS Users (
    UserId INTEGER PRIMARY KEY AUTOINCREMENT,

    -- UserCode is the visible ID shown in the UI, example: 2026-001.
    UserCode TEXT NOT NULL UNIQUE,

    FirstName TEXT NOT NULL,
    MiddleName TEXT,
    LastName TEXT NOT NULL,

    ContactNumber TEXT,
    Username TEXT NOT NULL UNIQUE,

    -- Password is not stored directly.
    -- Only the SHA-256 hash and salt are stored.
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,

    -- Supported roles based on the system design.
    Role TEXT NOT NULL CHECK(Role IN ('Admin', 'Dentist', 'Secretary', 'Dental Assistant')),
    IsDentistRole INTEGER NOT NULL DEFAULT 0,

    -- Dynamic security question IDs from the SecurityQuestions table.
    SecurityQuestionId1 INTEGER NOT NULL,
    SecurityAnswerHash1 TEXT NOT NULL,
    SecurityAnswerSalt1 TEXT NOT NULL,

    SecurityQuestionId2 INTEGER NOT NULL,
    SecurityAnswerHash2 TEXT NOT NULL,
    SecurityAnswerSalt2 TEXT NOT NULL,

    SecurityQuestionId3 INTEGER NOT NULL,
    SecurityAnswerHash3 TEXT NOT NULL,
    SecurityAnswerSalt3 TEXT NOT NULL,

    -- IsActive is used instead of deleting users permanently.
    -- 1 = active, 0 = archived/deactivated.
    IsActive INTEGER NOT NULL DEFAULT 1,

    CreatedByUserId INTEGER,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    LastLoginAt TEXT,
    HasEmployeePrivacyAcknowledgement INTEGER NOT NULL DEFAULT 0,
    EmployeePrivacyAcknowledgedAt TEXT,
    EmployeePrivacyAcknowledgementVersion TEXT,

    FOREIGN KEY (SecurityQuestionId1) REFERENCES SecurityQuestions(SecurityQuestionId),
    FOREIGN KEY (SecurityQuestionId2) REFERENCES SecurityQuestions(SecurityQuestionId),
    FOREIGN KEY (SecurityQuestionId3) REFERENCES SecurityQuestions(SecurityQuestionId),
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS SecurityQuestions (
    SecurityQuestionId INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Question text shown in registration dropdowns and forgot password screen.
    QuestionText TEXT NOT NULL UNIQUE,

    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Patients (
    PatientId INTEGER PRIMARY KEY AUTOINCREMENT,

    -- PatientCode is the visible ID shown in the UI, example: P001.
    PatientCode TEXT NOT NULL UNIQUE,

    FirstName TEXT NOT NULL,
    MiddleName TEXT,
    LastName TEXT NOT NULL,

    PhoneNumber TEXT NOT NULL,
    BirthDate TEXT NOT NULL,
    Gender TEXT NOT NULL CHECK(Gender IN ('Male', 'Female', 'Other')),

    Address TEXT,

    -- Used for priority and discount computation.
    IsPWD INTEGER NOT NULL DEFAULT 0,
    IsSeniorCitizen INTEGER NOT NULL DEFAULT 0,

    -- Initial service/treatment shown in Patient Management list.
    InitialTreatment TEXT,

    HasDataPrivacyConsent INTEGER NOT NULL DEFAULT 0,
    DataPrivacyConsentAt TEXT,
    DataPrivacyConsentVersion TEXT,

    IsActive INTEGER NOT NULL DEFAULT 1,

    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT
);

CREATE TABLE IF NOT EXISTS PatientHistories (
    PatientHistoryId INTEGER PRIMARY KEY AUTOINCREMENT,

    PatientId INTEGER NOT NULL UNIQUE,

    HasMedicalCondition INTEGER NOT NULL DEFAULT 0,
    MedicalConditionNotes TEXT,
    AllergyNotes TEXT,
    CurrentMedication TEXT,
    RequiresMedicalClearance INTEGER NOT NULL DEFAULT 0,
    ClearanceNotes TEXT,
    InitialTreatmentNotes TEXT,

    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,

    FOREIGN KEY (PatientId) REFERENCES Patients(PatientId)
);

CREATE TABLE IF NOT EXISTS Services (
    ServiceId INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Examples: Prophylaxis, Restoration/Pasta, Extraction, Orthodontics, Dental Crown, Fixed Bridge, Dentures.
    ServiceName TEXT NOT NULL UNIQUE,

    DefaultPrice REAL NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,

    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT
);

CREATE TABLE IF NOT EXISTS Appointments (
    AppointmentId INTEGER PRIMARY KEY AUTOINCREMENT,

    PatientId INTEGER NOT NULL,

    -- Walk-In or Scheduled.
    AppointmentType TEXT NOT NULL CHECK(AppointmentType IN ('Walk-In', 'Scheduled')),

    -- Auto-filled from patient record.
    Category TEXT NOT NULL DEFAULT 'Regular'
        CHECK(Category IN ('Regular', 'PWD', 'Senior')),

    ServiceId INTEGER,
    ServiceName TEXT NOT NULL,

    ServiceStage TEXT,
    FollowUpDate TEXT,
    TreatmentDetails TEXT,

    -- Dentist can be assigned or unassigned.
    DentistUserId INTEGER,
    DentistName TEXT DEFAULT 'Unassigned',

    -- For scheduled patients: reserved date/time.
    -- For walk-ins: today's date and current time.
    AppointmentDate TEXT NOT NULL,
    AppointmentTime TEXT NOT NULL,

    -- Actual arrival time. For walk-ins, this is usually the same as AppointmentTime.
    ArrivalTime TEXT,

    QueueNumber INTEGER,

    -- Urgent is a table action, not part of the add forms.
    IsUrgent INTEGER NOT NULL DEFAULT 0,

    -- Scheduled = scheduled priority, Urgent = emergency/priority override, Normal = regular queue.
    Priority TEXT NOT NULL DEFAULT 'Normal'
        CHECK(Priority IN ('Normal', 'Scheduled', 'Urgent')),

    -- Scheduled appointments stay Scheduled until staff marks them arrived.
    Status TEXT NOT NULL DEFAULT 'Scheduled'
        CHECK(Status IN ('Scheduled', 'Waiting', 'In Treatment', 'Completed', 'Cancelled', 'No Show')),

    Notes TEXT,

    StartedAt TEXT,
    CompletedAt TEXT,
    CancelledAt TEXT,
    CancellationReason TEXT,

    CreatedByUserId INTEGER,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,

    FOREIGN KEY (PatientId) REFERENCES Patients(PatientId),
    FOREIGN KEY (ServiceId) REFERENCES Services(ServiceId),
    FOREIGN KEY (DentistUserId) REFERENCES Users(UserId),
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS TreatmentRecords (
    TreatmentRecordId INTEGER PRIMARY KEY AUTOINCREMENT,

    PatientId INTEGER NOT NULL,
    AppointmentId INTEGER,

    ServiceId INTEGER,
    ServiceName TEXT NOT NULL,

    DentistUserId INTEGER,
    DentistName TEXT DEFAULT 'Unassigned',

    TreatmentDate TEXT NOT NULL,
    TreatmentTime TEXT,

    TreatmentNotes TEXT,
    ServiceStage TEXT,
    FollowUpDate TEXT,
    TreatmentDetails TEXT,

    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,

    FOREIGN KEY (PatientId) REFERENCES Patients(PatientId),
    FOREIGN KEY (AppointmentId) REFERENCES Appointments(AppointmentId),
    FOREIGN KEY (ServiceId) REFERENCES Services(ServiceId),
    FOREIGN KEY (DentistUserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS BillingTransactions (
    BillingId INTEGER PRIMARY KEY AUTOINCREMENT,

    PatientId INTEGER NOT NULL,

    -- Optional links depending on source.
    AppointmentId INTEGER,
    TreatmentRecordId INTEGER,

    -- Appointment = created from completed appointment/treatment record.
    -- Manual = manually created by staff.
    BillingSource TEXT NOT NULL DEFAULT 'Manual'
        CHECK(BillingSource IN ('Appointment', 'Manual')),

    ReceiptNumber TEXT NOT NULL UNIQUE,

    ServiceId INTEGER,
    ServiceName TEXT NOT NULL,
    Description TEXT,

    TotalAmount REAL NOT NULL DEFAULT 0,
    DiscountType TEXT NOT NULL DEFAULT 'None',
    DiscountAmount REAL NOT NULL DEFAULT 0,

    -- SubtotalAfterDiscount = TotalAmount - DiscountAmount.
    SubtotalAfterDiscount REAL NOT NULL DEFAULT 0,

    AmountPaid REAL NOT NULL DEFAULT 0,
    RemainingBalance REAL NOT NULL DEFAULT 0,

    PaymentStatus TEXT NOT NULL DEFAULT 'Unpaid'
        CHECK(PaymentStatus IN ('Unpaid', 'Partial', 'Paid')),

    TransactionDate TEXT NOT NULL,

    CreatedByUserId INTEGER,
    Notes TEXT,

    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    ArchivedAt TEXT,
    RestoredAt TEXT,

    FOREIGN KEY (PatientId) REFERENCES Patients(PatientId),
    FOREIGN KEY (AppointmentId) REFERENCES Appointments(AppointmentId),
    FOREIGN KEY (TreatmentRecordId) REFERENCES TreatmentRecords(TreatmentRecordId),
    FOREIGN KEY (ServiceId) REFERENCES Services(ServiceId),
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS PaymentRecords (
    PaymentRecordId INTEGER PRIMARY KEY AUTOINCREMENT,

    BillingId INTEGER NOT NULL,
    PatientId INTEGER NOT NULL,

    AmountPaid REAL NOT NULL,
    PaymentMethod TEXT NOT NULL DEFAULT 'Cash',
    PaymentDate TEXT NOT NULL,

    ReceivedByUserId INTEGER,
    Notes TEXT,

    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (BillingId) REFERENCES BillingTransactions(BillingId),
    FOREIGN KEY (PatientId) REFERENCES Patients(PatientId),
    FOREIGN KEY (ReceivedByUserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS InventoryItems (
    ItemId INTEGER PRIMARY KEY AUTOINCREMENT,

    ItemName TEXT NOT NULL UNIQUE,

    Quantity INTEGER NOT NULL DEFAULT 0,
    UnitPrice REAL NOT NULL DEFAULT 0,

    -- If Quantity is less than or equal to this value, item is low stock.
    MinimumThreshold INTEGER NOT NULL DEFAULT 0,

    [Stock Status] TEXT NOT NULL DEFAULT 'In Stock',
    IsActive INTEGER NOT NULL DEFAULT 1,

    LastRestock TEXT,
    UpdatedAt TEXT,
    ItemCreated TEXT NOT NULL,
    Note TEXT
);

CREATE TABLE IF NOT EXISTS InventoryUsage (
    UsageId INTEGER PRIMARY KEY AUTOINCREMENT,

    ItemId INTEGER NOT NULL,
    ItemName TEXT NOT NULL,

    QuantityUsed INTEGER NOT NULL,
    UsageDate TEXT NOT NULL,

    Notes TEXT,

    FOREIGN KEY (ItemId) REFERENCES InventoryItems(ItemId)
);

CREATE TABLE IF NOT EXISTS InventoryRestocks (
    RestockId INTEGER PRIMARY KEY AUTOINCREMENT,

    ItemId INTEGER NOT NULL,
    ItemName TEXT NOT NULL,

    QuantityAdded INTEGER NOT NULL,
    RestockedDate TEXT NOT NULL,
    Supplier TEXT,
    UnitPrice REAL NOT NULL DEFAULT 0,
    Note TEXT,

    FOREIGN KEY (ItemId) REFERENCES InventoryItems(ItemId)
);

CREATE TABLE IF NOT EXISTS ActivityLogs (
    LogId INTEGER PRIMARY KEY AUTOINCREMENT,

    UserId INTEGER,
    UserCode TEXT,
    Username TEXT,

    -- Examples: Login, Add, Update, Archive, Print, Backup, Restore.
    Action TEXT NOT NULL,

    -- Examples: Users, Patients, Appointment, Billing, Inventory, Maintenance.
    Module TEXT NOT NULL,

    Description TEXT,
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

CREATE TABLE IF NOT EXISTS BackupRecords (
    BackupId INTEGER PRIMARY KEY AUTOINCREMENT,

    BackupFileName TEXT NOT NULL,
    BackupPath TEXT NOT NULL,

    -- Manual for now. Automatic can be added later.
    BackupType TEXT NOT NULL DEFAULT 'Manual',

    -- Backups will be encrypted using AES-GCM later.
    IsEncrypted INTEGER NOT NULL DEFAULT 1,

    CreatedByUserId INTEGER,
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId)
);

-- Indexes make searching and dashboard loading faster.
CREATE INDEX IF NOT EXISTS idx_users_code ON Users(UserCode);
CREATE INDEX IF NOT EXISTS idx_users_username ON Users(Username);
CREATE INDEX IF NOT EXISTS idx_users_role ON Users(Role);

CREATE INDEX IF NOT EXISTS idx_security_questions_active ON SecurityQuestions(IsActive);

CREATE INDEX IF NOT EXISTS idx_patients_code ON Patients(PatientCode);
CREATE INDEX IF NOT EXISTS idx_patients_name ON Patients(LastName, FirstName);
CREATE INDEX IF NOT EXISTS idx_patients_pwd_senior ON Patients(IsPWD, IsSeniorCitizen);
CREATE INDEX IF NOT EXISTS idx_patients_created_active ON Patients(CreatedAt, IsActive);
CREATE INDEX IF NOT EXISTS idx_patients_contact ON Patients(PhoneNumber);
CREATE INDEX IF NOT EXISTS idx_patient_histories_patient ON PatientHistories(PatientId);


CREATE INDEX IF NOT EXISTS idx_appointments_date ON Appointments(AppointmentDate);
CREATE INDEX IF NOT EXISTS idx_appointments_status ON Appointments(Status);
CREATE INDEX IF NOT EXISTS idx_appointments_patient ON Appointments(PatientId);
CREATE INDEX IF NOT EXISTS idx_appointments_type ON Appointments(AppointmentType);
CREATE INDEX IF NOT EXISTS idx_appointments_datetime ON Appointments(AppointmentDate, AppointmentTime);
CREATE INDEX IF NOT EXISTS idx_appointments_queue ON Appointments(AppointmentDate, Status, IsUrgent, AppointmentType, AppointmentTime);

CREATE INDEX IF NOT EXISTS idx_treatment_records_patient ON TreatmentRecords(PatientId);
CREATE INDEX IF NOT EXISTS idx_treatment_records_appointment ON TreatmentRecords(AppointmentId);
CREATE INDEX IF NOT EXISTS idx_treatment_records_date ON TreatmentRecords(TreatmentDate);

CREATE INDEX IF NOT EXISTS idx_billing_patient ON BillingTransactions(PatientId);
CREATE INDEX IF NOT EXISTS idx_billing_status ON BillingTransactions(PaymentStatus);
CREATE INDEX IF NOT EXISTS idx_billing_receipt ON BillingTransactions(ReceiptNumber);
CREATE INDEX IF NOT EXISTS idx_billing_source ON BillingTransactions(BillingSource);
CREATE INDEX IF NOT EXISTS idx_billing_treatment_record ON BillingTransactions(TreatmentRecordId);
CREATE INDEX IF NOT EXISTS idx_billing_appointment ON BillingTransactions(AppointmentId);

CREATE INDEX IF NOT EXISTS idx_payment_records_billing ON PaymentRecords(BillingId);
CREATE INDEX IF NOT EXISTS idx_payment_records_patient ON PaymentRecords(PatientId);
CREATE INDEX IF NOT EXISTS idx_payment_records_date ON PaymentRecords(PaymentDate);

CREATE INDEX IF NOT EXISTS idx_inventory_item_name ON InventoryItems(ItemName);
CREATE INDEX IF NOT EXISTS idx_activity_created ON ActivityLogs(CreatedAt);
";

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        private static void SeedDefaultSecurityQuestions(SqliteConnection connection)
        {
            // These are the selectable security question options.
            // User Registration will load these into dropdown boxes.

            SeedSecurityQuestion(connection, "What was the name of your first school?");
            SeedSecurityQuestion(connection, "What was the name of your first pet?");
            SeedSecurityQuestion(connection, "What city were you born in?");
            SeedSecurityQuestion(connection, "What is your mother’s maiden name?");
            SeedSecurityQuestion(connection, "What was your childhood nickname?");
            SeedSecurityQuestion(connection, "What is the name of your favorite teacher?");
            SeedSecurityQuestion(connection, "What was your first phone brand?");
            SeedSecurityQuestion(connection, "What is your favorite food?");
            SeedSecurityQuestion(connection, "What is the name of your best friend?");
            SeedSecurityQuestion(connection, "What barangay did you grow up in?");
        }

        private static void SeedSecurityQuestion(SqliteConnection connection, string questionText)
        {
            // Prevent duplicate question records.
            using SqliteCommand checkCommand = connection.CreateCommand();
            checkCommand.CommandText = @"
        SELECT COUNT(*)
        FROM SecurityQuestions
        WHERE QuestionText = @QuestionText;";

            checkCommand.Parameters.AddWithValue("@QuestionText", questionText);

            long existingCount = (long)checkCommand.ExecuteScalar()!;

            if (existingCount > 0)
                return;

            using SqliteCommand insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
        INSERT INTO SecurityQuestions (
            QuestionText,
            IsActive,
            CreatedAt
        )
        VALUES (
            @QuestionText,
            1,
            @CreatedAt
        );";

            insertCommand.Parameters.AddWithValue("@QuestionText", questionText);
            insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            insertCommand.ExecuteNonQuery();
        }

        private static int GetSecurityQuestionId(SqliteConnection connection, string questionText)
        {
            // Gets the ID of a seeded security question.
            // This lets seeded users use the dynamic SecurityQuestions table.
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT SecurityQuestionId
        FROM SecurityQuestions
        WHERE QuestionText = @QuestionText
        LIMIT 1;";

            command.Parameters.AddWithValue("@QuestionText", questionText);

            object? result = command.ExecuteScalar();

            if (result == null)
                throw new InvalidOperationException($"Security question was not found: {questionText}");

            return Convert.ToInt32(result);
        }
        private static void SeedDefaultAdminAccounts(SqliteConnection connection)
        {
            // Get dynamic question IDs from the SecurityQuestions table.
            // This means seeded users no longer store hardcoded question text inside Users.
            int schoolQuestionId = GetSecurityQuestionId(connection, "What was the name of your first school?");
            int petQuestionId = GetSecurityQuestionId(connection, "What was the name of your first pet?");
            int cityQuestionId = GetSecurityQuestionId(connection, "What city were you born in?");

            int motherQuestionId = GetSecurityQuestionId(connection, "What is your mother’s maiden name?");
            int teacherQuestionId = GetSecurityQuestionId(connection, "What is the name of your favorite teacher?");
            int foodQuestionId = GetSecurityQuestionId(connection, "What is your favorite food?");

            // Clinic admin accounts still need temporary security answers,
            // because forgot password cannot work without stored answer hashes.
            // These are unique temporary answers and should be changed before deployment.

            SeedUser(
                connection,
                userCode: "2026-001",
                firstName: "Nery",
                middleName: "",
                lastName: "Cruz",
                username: "CNAdmin",
                plainPassword: "Admin@2026",
                role: "Admin",
                contactNumber: "N/A",

                securityQuestionId1: motherQuestionId,
                securityAnswer1: "mother",

                securityQuestionId2: teacherQuestionId,
                securityAnswer2: "teacher",

                securityQuestionId3: foodQuestionId,
                securityAnswer3: "food",
                isDentistRole: true
            );

            // Dev account keeps simple known answers for testing only.
            SeedUser(
                connection,
                userCode: "DEV-001",
                firstName: "Augustine",
                middleName: "",
                lastName: "Barredo",
                username: "devAB",
                plainPassword: "PASS#12",
                role: "Admin",
                contactNumber: "N/A",

                securityQuestionId1: schoolQuestionId,
                securityAnswer1: "school",

                securityQuestionId2: petQuestionId,
                securityAnswer2: "pet",

                securityQuestionId3: cityQuestionId,
                securityAnswer3: "city"
            );

            SeedUser(
                connection,
                userCode: "DEV-002",
                firstName: "Lawrence",
                middleName: "",
                lastName: "Malaga",
                username: "devLM",
                plainPassword: "PASS#12",
                role: "Admin",
                contactNumber: "N/A",

                securityQuestionId1: schoolQuestionId,
                securityAnswer1: "school",

                securityQuestionId2: petQuestionId,
                securityAnswer2: "pet",

                securityQuestionId3: cityQuestionId,
                securityAnswer3: "city"
            );

            SeedUser(
                connection,
                userCode: "DEV-003",
                firstName: "Wenifredo",
                middleName: "",
                lastName: "De Lemos",
                username: "devWDL",
                plainPassword: "PASS#12",
                role: "Admin",
                contactNumber: "N/A",

                securityQuestionId1: schoolQuestionId,
                securityAnswer1: "school",

                securityQuestionId2: petQuestionId,
                securityAnswer2: "pet",

                securityQuestionId3: cityQuestionId,
                securityAnswer3: "city"
            );
        }

        private static void SeedUser(
            SqliteConnection connection,
            string userCode,
            string firstName,
            string middleName,
            string lastName,
            string username,
            string plainPassword,
            string role,
            string contactNumber,
            int securityQuestionId1,
            string securityAnswer1,
            int securityQuestionId2,
            string securityAnswer2,
            int securityQuestionId3,
            string securityAnswer3,
            bool isDentistRole = false)
        {
            // Check first if the username already exists.
            // This prevents duplicate admin accounts every time the app starts.
            using SqliteCommand checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @Username;";
            checkCommand.Parameters.AddWithValue("@Username", username);

            long existingCount = (long)checkCommand.ExecuteScalar()!;

            if (existingCount > 0)
            {
                if (isDentistRole)
                    MarkUserAsDentistRole(connection, username);

                return;
            }

            // Generate password salt and hash.
            // The plain password is never stored in the database.
            string passwordSalt = PasswordService.GenerateSalt();
            string passwordHash = PasswordService.HashPassword(plainPassword, passwordSalt);

            // Generate separate salts for security answers.
            string answerSalt1 = PasswordService.GenerateSalt();
            string answerSalt2 = PasswordService.GenerateSalt();
            string answerSalt3 = PasswordService.GenerateSalt();

            // Store hashed answers only.
            // The plain security answers are never stored in the database.
            string answerHash1 = PasswordService.HashSecurityAnswer(securityAnswer1, answerSalt1);
            string answerHash2 = PasswordService.HashSecurityAnswer(securityAnswer2, answerSalt2);
            string answerHash3 = PasswordService.HashSecurityAnswer(securityAnswer3, answerSalt3);

            using SqliteCommand insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
        INSERT INTO Users (
            UserCode,
            FirstName,
            MiddleName,
            LastName,
            ContactNumber,
            Username,
            PasswordHash,
            PasswordSalt,
            Role,
            IsDentistRole,

            SecurityQuestionId1,
            SecurityAnswerHash1,
            SecurityAnswerSalt1,

            SecurityQuestionId2,
            SecurityAnswerHash2,
            SecurityAnswerSalt2,

            SecurityQuestionId3,
            SecurityAnswerHash3,
            SecurityAnswerSalt3,

            IsActive,
            CreatedAt
        )
        VALUES (
            @UserCode,
            @FirstName,
            @MiddleName,
            @LastName,
            @ContactNumber,
            @Username,
            @PasswordHash,
            @PasswordSalt,
            @Role,
            @IsDentistRole,

            @SecurityQuestionId1,
            @SecurityAnswerHash1,
            @SecurityAnswerSalt1,

            @SecurityQuestionId2,
            @SecurityAnswerHash2,
            @SecurityAnswerSalt2,

            @SecurityQuestionId3,
            @SecurityAnswerHash3,
            @SecurityAnswerSalt3,

            1,
            @CreatedAt
        );";

            insertCommand.Parameters.AddWithValue("@UserCode", userCode);
            insertCommand.Parameters.AddWithValue("@FirstName", firstName);
            insertCommand.Parameters.AddWithValue("@MiddleName", middleName);
            insertCommand.Parameters.AddWithValue("@LastName", lastName);
            insertCommand.Parameters.AddWithValue("@ContactNumber", contactNumber);
            insertCommand.Parameters.AddWithValue("@Username", username);
            insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
            insertCommand.Parameters.AddWithValue("@PasswordSalt", passwordSalt);
            insertCommand.Parameters.AddWithValue("@Role", role);
            insertCommand.Parameters.AddWithValue("@IsDentistRole", isDentistRole ? 1 : 0);

            insertCommand.Parameters.AddWithValue("@SecurityQuestionId1", securityQuestionId1);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerHash1", answerHash1);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerSalt1", answerSalt1);

            insertCommand.Parameters.AddWithValue("@SecurityQuestionId2", securityQuestionId2);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerHash2", answerHash2);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerSalt2", answerSalt2);

            insertCommand.Parameters.AddWithValue("@SecurityQuestionId3", securityQuestionId3);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerHash3", answerHash3);
            insertCommand.Parameters.AddWithValue("@SecurityAnswerSalt3", answerSalt3);

            insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            insertCommand.ExecuteNonQuery();
        }

        private static void MarkUserAsDentistRole(SqliteConnection connection, string username)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE Users
        SET IsDentistRole = 1
        WHERE Username = @Username;";
            command.Parameters.AddWithValue("@Username", username);
            command.ExecuteNonQuery();
        }

        private static void MergeCruzNeryAdminDentistAccount(SqliteConnection connection)
        {
            int? adminUserId = GetUserIdByUsername(connection, "CNAdmin");
            int? dentistUserId = GetUserIdByUsername(connection, "CNDentist");

            if (!adminUserId.HasValue)
                return;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        UPDATE Users
        SET IsDentistRole = 1,
            Role = 'Admin'
        WHERE UserId = @AdminUserId;";
                command.Parameters.AddWithValue("@AdminUserId", adminUserId.Value);
                command.ExecuteNonQuery();
            }

            if (!dentistUserId.HasValue)
                return;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        UPDATE Appointments
        SET DentistUserId = @AdminUserId,
            DentistName = 'Nery Cruz'
        WHERE DentistUserId = @DentistUserId;";
                command.Parameters.AddWithValue("@AdminUserId", adminUserId.Value);
                command.Parameters.AddWithValue("@DentistUserId", dentistUserId.Value);
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        UPDATE TreatmentRecords
        SET DentistUserId = @AdminUserId,
            DentistName = 'Nery Cruz'
        WHERE DentistUserId = @DentistUserId;";
                command.Parameters.AddWithValue("@AdminUserId", adminUserId.Value);
                command.Parameters.AddWithValue("@DentistUserId", dentistUserId.Value);
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        UPDATE Users
        SET IsActive = 0,
            IsDentistRole = 0,
            UpdatedAt = @UpdatedAt
        WHERE UserId = @DentistUserId;";
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@DentistUserId", dentistUserId.Value);
                command.ExecuteNonQuery();
            }
        }

        private static int? GetUserIdByUsername(SqliteConnection connection, string username)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT UserId
        FROM Users
        WHERE Username = @Username
        LIMIT 1;";
            command.Parameters.AddWithValue("@Username", username);

            object? result = command.ExecuteScalar();
            return result == null ? null : Convert.ToInt32(result);
        }
        private static void SeedDefaultServices(SqliteConnection connection)
        {
            // Prices can be updated later through the system if needed.
            SeedService(connection, "Prophylaxis", 0);
            SeedService(connection, "Restoration / Pasta", 0);
            SeedService(connection, "Extraction", 0);
            SeedService(connection, "Orthodontics", 0);
            SeedService(connection, "Dental Crown", 0);
            SeedService(connection, "Fixed Bridge", 0);
            SeedService(connection, "Dentures", 0);
            SeedService(connection, "Consultation", 0);
        }

        private static void EnsureUpdatedClinicServices(SqliteConnection connection)
        {
            SeedService(connection, "Dental Crown", 0);
            SeedService(connection, "Fixed Bridge", 0);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Services
SET IsActive = 0,
    UpdatedAt = @UpdatedAt
WHERE ServiceName = 'TMJ';";
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.ExecuteNonQuery();
        }

        private static void SeedService(SqliteConnection connection, string serviceName, double defaultPrice)
        {
            // Prevent duplicate service names.
            using SqliteCommand checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM Services WHERE ServiceName = @ServiceName;";
            checkCommand.Parameters.AddWithValue("@ServiceName", serviceName);

            long existingCount = (long)checkCommand.ExecuteScalar()!;

            if (existingCount > 0)
                return;

            using SqliteCommand insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
INSERT INTO Services (
    ServiceName,
    DefaultPrice,
    IsActive,
    CreatedAt
)
VALUES (
    @ServiceName,
    @DefaultPrice,
    1,
    @CreatedAt
);";

            insertCommand.Parameters.AddWithValue("@ServiceName", serviceName);
            insertCommand.Parameters.AddWithValue("@DefaultPrice", defaultPrice);
            insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            insertCommand.ExecuteNonQuery();
        }

        private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
        {
            if (!AllowedSchemaTables.Contains(tableName) || !IsSafeIdentifier(columnName))
                throw new ArgumentException("Only approved schema identifiers can be inspected.");

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = BuildPragmaTableInfoSql(tableName);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string existingColumnName = reader["name"]?.ToString() ?? string.Empty;

                if (string.Equals(existingColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string BuildPragmaTableInfoSql(string tableName)
            => "PRAGMA table_info(" + tableName + ");";

        private static bool IsSafeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            foreach (char character in identifier)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                    return false;
            }

            return true;
        }

        private static void EnsureBillingInvoiceSchema(SqliteConnection connection)
        {
            // BillingTransactions invoice/header extensions

            if (!ColumnExists(connection, "BillingTransactions", "InvoiceTitle"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE BillingTransactions
        ADD COLUMN InvoiceTitle TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "BillingTransactions", "IsInvoiceOpen"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE BillingTransactions
        ADD COLUMN IsInvoiceOpen INTEGER NOT NULL DEFAULT 1;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "BillingTransactions", "IsArchived"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE BillingTransactions
        ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "BillingTransactions", "ArchivedAt"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE BillingTransactions
        ADD COLUMN ArchivedAt TEXT;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "BillingTransactions", "RestoredAt"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE BillingTransactions
        ADD COLUMN RestoredAt TEXT;";
                command.ExecuteNonQuery();
            }

            // =====================================================
            // BillingTransactionItems table
            // This stores all itemized treatments/procedures under one invoice.
            // Sensitive fields such as ServiceName, ItemDescription, and Amount
            // will be encrypted by BillingRepository later.
            // =====================================================

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
        CREATE TABLE IF NOT EXISTS BillingTransactionItems (
            BillingItemId INTEGER PRIMARY KEY AUTOINCREMENT,
            BillingId INTEGER NOT NULL,
            AppointmentId INTEGER NULL,
            TreatmentRecordId INTEGER NULL,
            ServiceId INTEGER NULL,
            ServiceName TEXT NOT NULL,
            ItemDescription TEXT,
            TreatmentDate TEXT NULL,
            Amount TEXT NOT NULL,
            IsIncluded INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,

            FOREIGN KEY (BillingId)
                REFERENCES BillingTransactions(BillingId),

            FOREIGN KEY (AppointmentId)
                REFERENCES Appointments(AppointmentId),

            FOREIGN KEY (TreatmentRecordId)
                REFERENCES TreatmentRecords(TreatmentRecordId)
        );";
                command.ExecuteNonQuery();
            }

            EnsureBillingSensitiveDataEncrypted(connection);

            // TreatmentRecords billing tracking fields
            // These prevent the same completed treatment from being billed again.

            if (!ColumnExists(connection, "TreatmentRecords", "BillingStatus"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN BillingStatus TEXT NOT NULL DEFAULT 'Unbilled'
        CHECK (BillingStatus IN ('Unbilled', 'AddedToInvoice', 'NoCharge'));";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "TreatmentRecords", "BillingId"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN BillingId INTEGER NULL;";
                command.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "TreatmentRecords", "BillingItemId"))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
        ALTER TABLE TreatmentRecords
        ADD COLUMN BillingItemId INTEGER NULL;";
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
            CREATE TABLE IF NOT EXISTS InvoiceNumberSettings (
                SettingsId INTEGER PRIMARY KEY CHECK (SettingsId = 1),
                InvoicePrefix TEXT NOT NULL DEFAULT '',
                NextInvoiceNumber INTEGER NOT NULL DEFAULT 1,
                MinimumDigits INTEGER NOT NULL DEFAULT 6,
                ApprovedSeriesStart INTEGER NULL,
                ApprovedSeriesEnd INTEGER NULL,
                IsBirOfficialSeries INTEGER NOT NULL DEFAULT 0,
                AtpOrPermitNumber TEXT,
                BirPermitNumber TEXT,
                DateIssued TEXT
            );";
                command.ExecuteNonQuery();
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
            INSERT OR IGNORE INTO InvoiceNumberSettings (
                SettingsId,
                InvoicePrefix,
                NextInvoiceNumber,
                MinimumDigits,
                IsBirOfficialSeries
            )
            VALUES (
                1,
                '',
                1,
                6,
                0
            );";
                command.ExecuteNonQuery();
            }        


        }

        private static void EnsureBillingSensitiveDataEncrypted(SqliteConnection connection)
        {
            EncryptTextColumns(
                connection,
                "BillingTransactions",
                "BillingId",
                "ServiceName",
                "Description",
                "InvoiceTitle",
                "Notes");

            EncryptDecimalColumns(
                connection,
                "BillingTransactions",
                "BillingId",
                "TotalAmount",
                "DiscountAmount",
                "SubtotalAfterDiscount",
                "AmountPaid",
                "RemainingBalance");

            EncryptTextColumns(
                connection,
                "PaymentRecords",
                "PaymentRecordId",
                "PaymentMethod",
                "Notes");

            EncryptDecimalColumns(
                connection,
                "PaymentRecords",
                "PaymentRecordId",
                "AmountPaid");

            EncryptTextColumns(
                connection,
                "BillingTransactionItems",
                "BillingItemId",
                "ServiceName",
                "ItemDescription");

            EncryptDecimalColumns(
                connection,
                "BillingTransactionItems",
                "BillingItemId",
                "Amount");
        }

        private static void EncryptTextColumns(SqliteConnection connection, string tableName, string keyColumn, params string[] columnNames)
        {
            foreach (string columnName in columnNames)
            {
                if (!ColumnExists(connection, tableName, columnName))
                    continue;

                EncryptColumnValues(connection, tableName, keyColumn, columnName, value => CryptoService.EncryptString(value));
            }
        }

        private static void EncryptDecimalColumns(SqliteConnection connection, string tableName, string keyColumn, params string[] columnNames)
        {
            foreach (string columnName in columnNames)
            {
                if (!ColumnExists(connection, tableName, columnName))
                    continue;

                EncryptColumnValues(connection, tableName, keyColumn, columnName, value =>
                {
                    if (!decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                        amount = 0m;

                    return CryptoService.EncryptDecimal(amount);
                });
            }
        }

        private static void EncryptColumnValues(
            SqliteConnection connection,
            string tableName,
            string keyColumn,
            string columnName,
            Func<string, string> encrypt)
        {
            List<(long Id, string Value)> valuesToEncrypt = new();

            using (SqliteCommand selectCommand = connection.CreateCommand())
            {
                selectCommand.CommandText = $@"
        SELECT {keyColumn}, {columnName}
        FROM {tableName}
        WHERE {columnName} IS NOT NULL;";

                using SqliteDataReader reader = selectCommand.ExecuteReader();

                while (reader.Read())
                {
                    string rawValue = reader[columnName]?.ToString() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(rawValue) ||
                        rawValue.StartsWith("ENC:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    valuesToEncrypt.Add((Convert.ToInt64(reader[keyColumn]), rawValue));
                }
            }

            foreach ((long id, string value) in valuesToEncrypt)
            {
                using SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = $@"
        UPDATE {tableName}
        SET {columnName} = @EncryptedValue
        WHERE {keyColumn} = @Id;";

                updateCommand.Parameters.AddWithValue("@EncryptedValue", encrypt(value));
                updateCommand.Parameters.AddWithValue("@Id", id);
                updateCommand.ExecuteNonQuery();
            }
        }
    
    }
}
