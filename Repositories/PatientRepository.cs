using CruzNeryClinic.Data;
using CruzNeryClinic.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using CruzNeryClinic.Services;

namespace CruzNeryClinic.Repositories
{
    public class PatientRepository
    {
        #region Patient List and Summary

        public List<PatientListItem> GetPatientListItems()
        {
            List<PatientListItem> patients = new();

            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    p.PatientId,
    p.PatientCode,
    p.FirstName,
    p.MiddleName,
    p.LastName,
    p.PhoneNumber,
    p.BirthDate,
    p.Gender,
    p.InitialTreatment,
    p.IsPWD,
    p.IsSeniorCitizen,
    p.HasDataPrivacyConsent,
    p.DataPrivacyConsentAt,
    p.IsActive,
    p.CreatedAt,

    CASE
        WHEN ph.PatientHistoryId IS NULL THEN 0
        ELSE 1
    END AS HasPatientHistory,

    CASE
        WHEN EXISTS (
            SELECT 1
            FROM BillingTransactions bt
            WHERE bt.PatientId = p.PatientId
              AND bt.RemainingBalance > 0
              AND bt.PaymentStatus <> 'Paid'
        ) THEN 1
        ELSE 0
    END AS HasBalance

FROM Patients p
LEFT JOIN PatientHistories ph
    ON p.PatientId = ph.PatientId
ORDER BY p.PatientId ASC;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                patients.Add(MapReaderToPatientListItem(reader));
            }

            return patients;
        }

        public int GetNewPatientsThisMonthCount()
        {
            DateTime now = DateTime.Now;
            DateTime monthStart = new(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*)
FROM Patients
WHERE IsActive = 1
  AND CreatedAt >= @MonthStart
  AND CreatedAt < @NextMonthStart;";

            command.Parameters.AddWithValue("@MonthStart", monthStart.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@NextMonthStart", nextMonthStart.ToString("yyyy-MM-dd HH:mm:ss"));

            return Convert.ToInt32(command.ExecuteScalar());
        }

        #endregion

        #region Get Single Patient

        public Patient? GetPatientById(int patientId)
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    p.PatientId,
    p.PatientCode,
    p.FirstName,
    p.MiddleName,
    p.LastName,
    p.PhoneNumber,
    p.BirthDate,
    p.Gender,
    p.Address,
    p.IsPWD,
    p.IsSeniorCitizen,
    p.InitialTreatment,
    p.HasDataPrivacyConsent,
    p.DataPrivacyConsentAt,
    p.DataPrivacyConsentVersion,
    p.IsActive,
    p.CreatedAt,
    p.UpdatedAt,

    ph.HasMedicalCondition,
    ph.MedicalConditionNotes,
    ph.AllergyNotes,
    ph.CurrentMedication,
    ph.RequiresMedicalClearance,
    ph.ClearanceNotes,
    ph.InitialTreatmentNotes

FROM Patients p
LEFT JOIN PatientHistories ph
    ON p.PatientId = ph.PatientId
WHERE p.PatientId = @PatientId
LIMIT 1;";

            command.Parameters.AddWithValue("@PatientId", patientId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (reader.Read())
                return MapReaderToPatient(reader);

            return null;
        }

        #endregion

        #region Add Patient

        public int AddPatient(Patient patient)
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            try
            {
                int patientId = InsertPatient(connection, transaction, patient, CryptoService.EncryptString);

                transaction.Commit();
                return patientId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        internal static int InsertPatient(SqliteConnection connection, SqliteTransaction transaction, Patient patient, Func<string?, string> encrypt)
        {
            string patientCode = GenerateNextPatientCode(connection, transaction);
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using SqliteCommand insertPatientCommand = connection.CreateCommand();
            insertPatientCommand.Transaction = transaction;
            insertPatientCommand.CommandText = @"
        INSERT INTO Patients (
            PatientCode,
            FirstName,
            MiddleName,
            LastName,
            PhoneNumber,
            BirthDate,
            Gender,
            Address,
            IsPWD,
            IsSeniorCitizen,
            InitialTreatment,
            HasDataPrivacyConsent,
            DataPrivacyConsentAt,
            DataPrivacyConsentVersion,
            IsActive,
            CreatedAt
        )
        VALUES (
            @PatientCode,
            @FirstName,
            @MiddleName,
            @LastName,
            @PhoneNumber,
            @BirthDate,
            @Gender,
            @Address,
            @IsPWD,
            @IsSeniorCitizen,
            @InitialTreatment,
            @HasDataPrivacyConsent,
            @DataPrivacyConsentAt,
            @DataPrivacyConsentVersion,
            1,
            @CreatedAt
        );

        SELECT last_insert_rowid();";

            insertPatientCommand.Parameters.AddWithValue("@PatientCode", patientCode);
            insertPatientCommand.Parameters.AddWithValue("@FirstName", patient.FirstName.Trim());
            insertPatientCommand.Parameters.AddWithValue("@MiddleName", patient.MiddleName.Trim());
            insertPatientCommand.Parameters.AddWithValue("@LastName", patient.LastName.Trim());
            insertPatientCommand.Parameters.AddWithValue("@PhoneNumber", encrypt(patient.PhoneNumber.Trim()));
            insertPatientCommand.Parameters.AddWithValue("@BirthDate", patient.BirthDate.ToString("yyyy-MM-dd"));
            insertPatientCommand.Parameters.AddWithValue("@Gender", patient.Gender.Trim());
            insertPatientCommand.Parameters.AddWithValue("@Address", encrypt(patient.Address.Trim()));
            insertPatientCommand.Parameters.AddWithValue("@IsPWD", patient.IsPwd ? 1 : 0);
            insertPatientCommand.Parameters.AddWithValue("@IsSeniorCitizen", patient.IsSeniorCitizen ? 1 : 0);
            insertPatientCommand.Parameters.AddWithValue("@InitialTreatment", patient.InitialTreatment.Trim());
            insertPatientCommand.Parameters.AddWithValue("@HasDataPrivacyConsent", patient.HasDataPrivacyConsent ? 1 : 0);
            insertPatientCommand.Parameters.AddWithValue("@DataPrivacyConsentAt", ToDatabaseDateTime(patient.DataPrivacyConsentAt));
            insertPatientCommand.Parameters.AddWithValue("@DataPrivacyConsentVersion", patient.DataPrivacyConsentVersion.Trim());
            insertPatientCommand.Parameters.AddWithValue("@CreatedAt", createdAt);

            int patientId = Convert.ToInt32(insertPatientCommand.ExecuteScalar());

            using SqliteCommand insertHistoryCommand = connection.CreateCommand();
            insertHistoryCommand.Transaction = transaction;
            insertHistoryCommand.CommandText = @"
        INSERT INTO PatientHistories (
            PatientId,
            HasMedicalCondition,
            MedicalConditionNotes,
            AllergyNotes,
            CurrentMedication,
            RequiresMedicalClearance,
            ClearanceNotes,
            InitialTreatmentNotes,
            CreatedAt
        )
        VALUES (
            @PatientId,
            @HasMedicalCondition,
            @MedicalConditionNotes,
            @AllergyNotes,
            @CurrentMedication,
            @RequiresMedicalClearance,
            @ClearanceNotes,
            @InitialTreatmentNotes,
            @CreatedAt
        );";

            insertHistoryCommand.Parameters.AddWithValue("@PatientId", patientId);
            insertHistoryCommand.Parameters.AddWithValue("@HasMedicalCondition", patient.HasMedicalCondition ? 1 : 0);
            insertHistoryCommand.Parameters.AddWithValue("@MedicalConditionNotes", encrypt(patient.MedicalConditionNotes.Trim()));
            insertHistoryCommand.Parameters.AddWithValue("@AllergyNotes", encrypt(patient.AllergyNotes.Trim()));
            insertHistoryCommand.Parameters.AddWithValue("@CurrentMedication", encrypt(patient.CurrentMedication.Trim()));
            insertHistoryCommand.Parameters.AddWithValue("@RequiresMedicalClearance", patient.RequiresMedicalClearance ? 1 : 0);
            insertHistoryCommand.Parameters.AddWithValue("@ClearanceNotes", encrypt(patient.ClearanceNotes.Trim()));
            insertHistoryCommand.Parameters.AddWithValue("@InitialTreatmentNotes", encrypt(patient.InitialTreatmentNotes.Trim()));
            insertHistoryCommand.Parameters.AddWithValue("@CreatedAt", createdAt);

            insertHistoryCommand.ExecuteNonQuery();

            return patientId;
        }

        #region Update Patient

        public void UpdatePatient(Patient patient)
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            try
            {
                string updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                using SqliteCommand updatePatientCommand = connection.CreateCommand();
                updatePatientCommand.Transaction = transaction;
                updatePatientCommand.CommandText = @"
        UPDATE Patients
        SET
            FirstName = @FirstName,
            MiddleName = @MiddleName,
            LastName = @LastName,
            PhoneNumber = @PhoneNumber,
            BirthDate = @BirthDate,
            Gender = @Gender,
            Address = @Address,
            IsPWD = @IsPWD,
            IsSeniorCitizen = @IsSeniorCitizen,
            InitialTreatment = @InitialTreatment,
            HasDataPrivacyConsent = @HasDataPrivacyConsent,
            DataPrivacyConsentAt = @DataPrivacyConsentAt,
            DataPrivacyConsentVersion = @DataPrivacyConsentVersion,
            UpdatedAt = @UpdatedAt
        WHERE PatientId = @PatientId;";

                updatePatientCommand.Parameters.AddWithValue("@FirstName", patient.FirstName.Trim());
                updatePatientCommand.Parameters.AddWithValue("@MiddleName", patient.MiddleName.Trim());
                updatePatientCommand.Parameters.AddWithValue("@LastName", patient.LastName.Trim());
                updatePatientCommand.Parameters.AddWithValue("@PhoneNumber", CryptoService.EncryptString(patient.PhoneNumber.Trim()));
                updatePatientCommand.Parameters.AddWithValue("@BirthDate", patient.BirthDate.ToString("yyyy-MM-dd"));
                updatePatientCommand.Parameters.AddWithValue("@Gender", patient.Gender.Trim());
                updatePatientCommand.Parameters.AddWithValue("@Address", CryptoService.EncryptString(patient.Address.Trim()));
                updatePatientCommand.Parameters.AddWithValue("@IsPWD", patient.IsPwd ? 1 : 0);
                updatePatientCommand.Parameters.AddWithValue("@IsSeniorCitizen", patient.IsSeniorCitizen ? 1 : 0);
                updatePatientCommand.Parameters.AddWithValue("@InitialTreatment", patient.InitialTreatment.Trim());
                updatePatientCommand.Parameters.AddWithValue("@HasDataPrivacyConsent", patient.HasDataPrivacyConsent ? 1 : 0);
                updatePatientCommand.Parameters.AddWithValue("@DataPrivacyConsentAt", ToDatabaseDateTime(patient.DataPrivacyConsentAt));
                updatePatientCommand.Parameters.AddWithValue("@DataPrivacyConsentVersion", patient.DataPrivacyConsentVersion.Trim());
                updatePatientCommand.Parameters.AddWithValue("@UpdatedAt", updatedAt);
                updatePatientCommand.Parameters.AddWithValue("@PatientId", patient.PatientId);

                updatePatientCommand.ExecuteNonQuery();

                using SqliteCommand upsertHistoryCommand = connection.CreateCommand();
                upsertHistoryCommand.Transaction = transaction;
                upsertHistoryCommand.CommandText = @"
        INSERT INTO PatientHistories (
            PatientId,
            HasMedicalCondition,
            MedicalConditionNotes,
            AllergyNotes,
            CurrentMedication,
            RequiresMedicalClearance,
            ClearanceNotes,
            InitialTreatmentNotes,
            CreatedAt,
            UpdatedAt
        )
        VALUES (
            @PatientId,
            @HasMedicalCondition,
            @MedicalConditionNotes,
            @AllergyNotes,
            @CurrentMedication,
            @RequiresMedicalClearance,
            @ClearanceNotes,
            @InitialTreatmentNotes,
            @CreatedAt,
            @UpdatedAt
        )
        ON CONFLICT(PatientId) DO UPDATE SET
            HasMedicalCondition = excluded.HasMedicalCondition,
            MedicalConditionNotes = excluded.MedicalConditionNotes,
            AllergyNotes = excluded.AllergyNotes,
            CurrentMedication = excluded.CurrentMedication,
            RequiresMedicalClearance = excluded.RequiresMedicalClearance,
            ClearanceNotes = excluded.ClearanceNotes,
            InitialTreatmentNotes = excluded.InitialTreatmentNotes,
            UpdatedAt = excluded.UpdatedAt;";

                upsertHistoryCommand.Parameters.AddWithValue("@PatientId", patient.PatientId);
                upsertHistoryCommand.Parameters.AddWithValue("@HasMedicalCondition", patient.HasMedicalCondition ? 1 : 0);
                upsertHistoryCommand.Parameters.AddWithValue("@MedicalConditionNotes", CryptoService.EncryptString(patient.MedicalConditionNotes.Trim()));
                upsertHistoryCommand.Parameters.AddWithValue("@AllergyNotes", CryptoService.EncryptString(patient.AllergyNotes.Trim()));
                upsertHistoryCommand.Parameters.AddWithValue("@CurrentMedication", CryptoService.EncryptString(patient.CurrentMedication.Trim()));
                upsertHistoryCommand.Parameters.AddWithValue("@RequiresMedicalClearance", patient.RequiresMedicalClearance ? 1 : 0);
                upsertHistoryCommand.Parameters.AddWithValue("@ClearanceNotes", CryptoService.EncryptString(patient.ClearanceNotes.Trim()));
                upsertHistoryCommand.Parameters.AddWithValue("@InitialTreatmentNotes", CryptoService.EncryptString(patient.InitialTreatmentNotes.Trim()));
                upsertHistoryCommand.Parameters.AddWithValue("@CreatedAt", updatedAt);
                upsertHistoryCommand.Parameters.AddWithValue("@UpdatedAt", updatedAt);

                upsertHistoryCommand.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Treatment Records

        public List<TreatmentRecordListItem> GetTreatmentRecordsByPatientId(int patientId)
        {
            List<TreatmentRecordListItem> treatmentRecords = new();

            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            TreatmentRecordId,
            PatientId,
            AppointmentId,
            ServiceName,
            DentistName,
            TreatmentDate,
            TreatmentTime,
            TreatmentNotes,
            ServiceStage,
            FollowUpDate,
            TreatmentDetails
        FROM TreatmentRecords
        WHERE PatientId = @PatientId
        ORDER BY TreatmentDate DESC, TreatmentTime DESC, TreatmentRecordId DESC;";

            command.Parameters.AddWithValue("@PatientId", patientId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                TreatmentRecordListItem treatmentRecord = new()
                {
                    TreatmentRecordId = Convert.ToInt32(reader["TreatmentRecordId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    AppointmentId = SafeGetNullableInt(reader, "AppointmentId"),
                    ServiceName = reader["ServiceName"]?.ToString() ?? string.Empty,
                    DentistName = reader["DentistName"]?.ToString() ?? "Unassigned",
                    TreatmentDate = ParseDate(reader["TreatmentDate"]?.ToString()),
                    TreatmentTime = ParseNullableTime(reader["TreatmentTime"]?.ToString()),
                    TreatmentNotes = DecryptTreatmentNotes(reader["TreatmentNotes"]?.ToString()),
                    ServiceStage = SafeGetString(reader, "ServiceStage"),
                    FollowUpDate = ParseNullableDate(reader["FollowUpDate"]?.ToString()),
                    TreatmentDetails = reader["TreatmentDetails"]?.ToString() ?? string.Empty
                };

                treatmentRecords.Add(treatmentRecord);
            }

            foreach (TreatmentRecordListItem treatmentRecord in treatmentRecords)
                LoadTreatmentRecordImages(connection, treatmentRecord);

            return treatmentRecords;
        }

        private void LoadTreatmentRecordImages(SqliteConnection connection, TreatmentRecordListItem treatmentRecord)
        {
            if (!treatmentRecord.AppointmentId.HasValue)
                return;

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT AppointmentImageId, FilePath
FROM AppointmentImages
WHERE AppointmentId = @AppointmentId
ORDER BY AppointmentImageId DESC;";

            command.Parameters.AddWithValue("@AppointmentId", treatmentRecord.AppointmentId.Value);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                treatmentRecord.TeethImages.Add(new AppointmentImageItem
                {
                    AppointmentImageId = Convert.ToInt32(reader["AppointmentImageId"]),
                    FilePath = reader["FilePath"]?.ToString() ?? string.Empty
                });
            }
        }

        #endregion

        #region Archive and Restore

        public void SetPatientActiveStatus(int patientId, bool isActive)
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Patients
SET
    IsActive = @IsActive,
    UpdatedAt = @UpdatedAt
WHERE PatientId = @PatientId;";

            command.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@PatientId", patientId);

            command.ExecuteNonQuery();
        }

        #endregion

        #region Duplicate Checking

        public PatientListItem? FindDuplicatePatientByIdentity(
            string firstName,
            string middleName,
            string lastName,
            DateTime birthDate,
            int? excludedPatientId = null)
        {
            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            PatientId,
            PatientCode,
            FirstName,
            MiddleName,
            LastName,
            PhoneNumber,
            BirthDate,
            Gender,
            InitialTreatment,
            IsPWD,
            IsSeniorCitizen,
            HasDataPrivacyConsent,
            DataPrivacyConsentAt,
            IsActive,
            CreatedAt,
            0 AS HasPatientHistory,
            0 AS HasBalance
        FROM Patients
        WHERE
            LOWER(TRIM(FirstName)) = LOWER(TRIM(@FirstName))
            AND LOWER(TRIM(IFNULL(MiddleName, ''))) = LOWER(TRIM(@MiddleName))
            AND LOWER(TRIM(LastName)) = LOWER(TRIM(@LastName))
            AND BirthDate = @BirthDate
            AND (@ExcludedPatientId IS NULL OR PatientId <> @ExcludedPatientId)
        LIMIT 1;";

            command.AddTextParameter("@FirstName", firstName.Trim());
            command.AddTextParameter("@MiddleName", middleName.Trim());
            command.AddTextParameter("@LastName", lastName.Trim());
            command.AddDateParameter("@BirthDate", birthDate);
            command.AddNullableParameter("@ExcludedPatientId", excludedPatientId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (reader.Read())
                return MapReaderToPatientListItem(reader);

            return null;
        }

        #endregion

        #region Service Options

        public List<ServiceItem> GetActiveServices()
        {
            List<ServiceItem> services = new();

            using SqliteConnection connection = DatabaseService.GetConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            ServiceId,
            ServiceName,
            DefaultPrice,
            IsActive
        FROM Services
        WHERE IsActive = 1
        ORDER BY ServiceName ASC;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                services.Add(new ServiceItem
                {
                    ServiceId = Convert.ToInt32(reader["ServiceId"]),
                    ServiceName = reader["ServiceName"]?.ToString() ?? string.Empty,
                    DefaultPrice = Convert.ToDouble(reader["DefaultPrice"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                });
            }

            return services;
        }

        #endregion

        #region Helpers

        private static string GenerateNextPatientCode(SqliteConnection connection, SqliteTransaction transaction)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT PatientCode
FROM Patients
WHERE PatientCode LIKE 'P%'
ORDER BY PatientId DESC
LIMIT 1;";

            object? result = command.ExecuteScalar();

            if (result == null)
                return "P001";

            string lastCode = result.ToString() ?? "P000";
            string digits = lastCode.Replace("P", "");

            if (!int.TryParse(digits, out int number))
                number = 0;

            return $"P{number + 1:000}";
        }

        private PatientListItem MapReaderToPatientListItem(SqliteDataReader reader)
        {
            return new PatientListItem
            {
                PatientId = Convert.ToInt32(reader["PatientId"]),
                PatientCode = reader["PatientCode"]?.ToString() ?? string.Empty,
                FirstName = reader["FirstName"]?.ToString() ?? string.Empty,
                MiddleName = reader["MiddleName"]?.ToString() ?? string.Empty,
                LastName = reader["LastName"]?.ToString() ?? string.Empty,
                PhoneNumber = CryptoService.DecryptString(reader["PhoneNumber"]?.ToString()),
                DateOfBirth = ParseNullableDate(reader["BirthDate"]?.ToString()),
                Gender = reader["Gender"]?.ToString() ?? string.Empty,
                Treatment = reader["InitialTreatment"]?.ToString() ?? string.Empty,
                IsPwd = Convert.ToInt32(reader["IsPWD"]) == 1,
                IsSenior = Convert.ToInt32(reader["IsSeniorCitizen"]) == 1,
                HasDataPrivacyConsent = SafeGetInt(reader, "HasDataPrivacyConsent") == 1,
                DataPrivacyConsentAt = ParseNullableDate(reader["DataPrivacyConsentAt"]?.ToString()),
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                CreatedAt = ParseDate(reader["CreatedAt"]?.ToString()),
                HasPatientHistory = Convert.ToInt32(reader["HasPatientHistory"]) == 1,
                HasBalance = Convert.ToInt32(reader["HasBalance"]) == 1
            };
        }

        private Patient MapReaderToPatient(SqliteDataReader reader)
        {
            return new Patient
            {
                PatientId = Convert.ToInt32(reader["PatientId"]),
                PatientCode = reader["PatientCode"]?.ToString() ?? string.Empty,
                FirstName = reader["FirstName"]?.ToString() ?? string.Empty,
                MiddleName = reader["MiddleName"]?.ToString() ?? string.Empty,
                LastName = reader["LastName"]?.ToString() ?? string.Empty,
                PhoneNumber = CryptoService.DecryptString(reader["PhoneNumber"]?.ToString()),
                BirthDate = ParseDate(reader["BirthDate"]?.ToString()),
                Gender = reader["Gender"]?.ToString() ?? string.Empty,
                Address = CryptoService.DecryptString(reader["Address"]?.ToString()),
                IsPwd = Convert.ToInt32(reader["IsPWD"]) == 1,
                IsSeniorCitizen = Convert.ToInt32(reader["IsSeniorCitizen"]) == 1,
                InitialTreatment = reader["InitialTreatment"]?.ToString() ?? string.Empty,
                HasDataPrivacyConsent = SafeGetInt(reader, "HasDataPrivacyConsent") == 1,
                DataPrivacyConsentAt = ParseNullableDate(reader["DataPrivacyConsentAt"]?.ToString()),
                DataPrivacyConsentVersion = SafeGetString(reader, "DataPrivacyConsentVersion"),
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                CreatedAt = ParseDate(reader["CreatedAt"]?.ToString()),
                UpdatedAt = ParseNullableDate(reader["UpdatedAt"]?.ToString()),
                HasMedicalCondition = SafeGetInt(reader, "HasMedicalCondition") == 1,
                MedicalConditionNotes = CryptoService.DecryptString(SafeGetString(reader, "MedicalConditionNotes")),
                AllergyNotes = CryptoService.DecryptString(SafeGetString(reader, "AllergyNotes")),
                CurrentMedication = CryptoService.DecryptString(SafeGetString(reader, "CurrentMedication")),
                RequiresMedicalClearance = SafeGetInt(reader, "RequiresMedicalClearance") == 1,
                ClearanceNotes = CryptoService.DecryptString(SafeGetString(reader, "ClearanceNotes")),
                InitialTreatmentNotes = CryptoService.DecryptString(SafeGetString(reader, "InitialTreatmentNotes"))
            };
        }
        private string SafeGetString(SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return string.Empty;

            return reader.GetString(ordinal);
        }

        private int SafeGetInt(SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return 0;

            return Convert.ToInt32(reader[columnName]);
        }

        private int? SafeGetNullableInt(SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return Convert.ToInt32(reader[columnName]);
        }

        private DateTime ParseDate(string? value)
        {
            return DateTime.TryParse(value, out DateTime date)
                ? date
                : DateTime.Today;
        }

        private DateTime? ParseNullableDate(string? value)
        {
            return DateTime.TryParse(value, out DateTime date)
                ? date
                : null;
        }

        private static object ToDatabaseDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : DBNull.Value;
        }

        private TimeSpan? ParseNullableTime(string? value)
        {
            return TimeSpan.TryParse(value, out TimeSpan time)
                ? time
                : null;
        }

        private string DecryptTreatmentNotes(string? encryptedValue)
        {
            if (string.IsNullOrWhiteSpace(encryptedValue))
                return string.Empty;

            try
            {
                return CryptoService.DecryptString(encryptedValue);
            }
            catch
            {
                // Fallback for old treatment notes that were saved before encryption was added.
                return encryptedValue;
            }
        }

        #endregion
    }
}
