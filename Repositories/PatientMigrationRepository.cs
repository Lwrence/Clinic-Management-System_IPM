using CruzNeryClinic.Data;
using CruzNeryClinic.Models;
using CruzNeryClinic.Models.Migration;
using CruzNeryClinic.Services;
using Microsoft.Data.Sqlite;

namespace CruzNeryClinic.Repositories;

public record MigrationSnapshot(List<MigrationIdentity> Patients, HashSet<string> ImportedSources);

public class PatientMigrationRepository
{
    private readonly Func<SqliteConnection> getConnection;
    private readonly Func<string?, string> encrypt;
    private readonly Func<string?, string> decrypt;
    public PatientMigrationRepository() : this(DatabaseService.GetConnection, CryptoService.EncryptString, CryptoService.DecryptString) { }
    public PatientMigrationRepository(Func<SqliteConnection> getConnection, Func<string?, string> encrypt, Func<string?, string>? decrypt = null)
    {
        this.getConnection = getConnection;
        this.encrypt = encrypt;
        this.decrypt = decrypt ?? CryptoService.DecryptString;
    }

    public static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS PatientMigrationRecords (
    MigrationRecordId INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceFileName TEXT NOT NULL,
    SourceHash TEXT NOT NULL,
    SourceRecordNumber INTEGER NOT NULL,
    PatientId INTEGER NOT NULL,
    ReviewedByUserId INTEGER NOT NULL,
    ImportedAt TEXT NOT NULL,
    UNIQUE(SourceHash, SourceRecordNumber),
    FOREIGN KEY(PatientId) REFERENCES Patients(PatientId),
    FOREIGN KEY(ReviewedByUserId) REFERENCES Users(UserId)
);
CREATE TABLE IF NOT EXISTS PatientMigrationAttempts (
    AttemptId INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceFileName TEXT NOT NULL,
    SourceRecordNumber INTEGER NOT NULL,
    Result TEXT NOT NULL CHECK(Result IN ('Failed', 'Rejected')),
    Details TEXT NOT NULL,
    ProcessedByUserId INTEGER NOT NULL,
    ProcessedAt TEXT NOT NULL,
    FOREIGN KEY(ProcessedByUserId) REFERENCES Users(UserId)
);";
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AppointmentServiceOption> GetActiveServices()
    {
        if (!SessionService.CanAccessModule("DataMigration"))
            throw new UnauthorizedAccessException("Sign in with an authorized clinic account to access migration service options.");
        using var connection = getConnection();
        connection.Open();
        return AppointmentRepository.GetActiveServices(connection);
    }

    public MigrationSnapshot GetSnapshot()
    {
        if (!SessionService.CanAccessModule("DataMigration"))
            throw new UnauthorizedAccessException("Sign in with an authorized clinic account to access patient migration and validation.");
        using var connection = getConnection();
        connection.Open();
        var snapshot = GetSnapshot(connection, null);
        snapshot.Patients.ReplaceAllPhones(decrypt);
        return snapshot;
    }

    private static MigrationSnapshot GetSnapshot(SqliteConnection connection, SqliteTransaction? transaction)
    {
        List<MigrationIdentity> patients = new();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT PatientId, PatientCode, FirstName, MiddleName, LastName, BirthDate, PhoneNumber FROM Patients;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                patients.Add(new(Convert.ToInt32(reader["PatientId"]), reader["PatientCode"].ToString()!,
                    reader["FirstName"].ToString()!, reader["MiddleName"].ToString() ?? "",
                    reader["LastName"].ToString()!, DateTime.Parse(reader["BirthDate"].ToString()!,
                    System.Globalization.CultureInfo.InvariantCulture), reader["PhoneNumber"].ToString() ?? ""));
        }
        HashSet<string> sources = new();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT SourceHash, SourceRecordNumber FROM PatientMigrationRecords;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) sources.Add($"{reader["SourceHash"]}:{reader["SourceRecordNumber"]}");
        }
        return new(patients, sources);
    }

    public IReadOnlyList<int> ImportReviewed(IReadOnlyList<PatientMigrationDraft> drafts)
    {
        if (!SessionService.CanAccessModule("DataMigration") || SessionService.CurrentUser == null)
            throw new UnauthorizedAccessException("Sign in with an authorized clinic account before saving migration records.");
        User actor = SessionService.CurrentUser;
        if (drafts.Count == 0) throw new InvalidOperationException("Select at least one record.");
        using var connection = getConnection();
        connection.Open();
        // Immediate write transaction prevents another LAN client inserting between validation and save.
        using var transaction = connection.BeginTransaction(deferred: false);
        var snapshot = GetSnapshot(connection, transaction);
        snapshot.Patients.ReplaceAllPhones(decrypt);
        List<int> ids = new();
        List<string> patientCodes = new();
        foreach (var draft in drafts)
        {
            if (!draft.IsReviewed || draft.IsImported)
                throw new InvalidOperationException($"{draft.SourceDisplay}: every selected record must be reviewed.");
            string sourceKey = $"{draft.SourceHash}:{draft.SourceRecordNumber}";
            if (snapshot.ImportedSources.Contains(sourceKey))
                throw new InvalidOperationException($"{draft.SourceDisplay}: this source record was already imported.");
            var validation = PatientMigrationValidationService.Validate(draft, snapshot.Patients, drafts, DateTime.Today);
            if (!validation.CanImport)
                throw new InvalidOperationException($"{draft.SourceDisplay}:\n{string.Join("\n", validation.Errors)}");
            int patientId = PatientRepository.InsertPatient(connection, transaction, validation.Patient!, encrypt);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO PatientMigrationRecords (SourceFileName, SourceHash, SourceRecordNumber, PatientId, ReviewedByUserId, ImportedAt)
VALUES (@File, @Hash, @Record, @Patient, @User, @At);";
                command.Parameters.AddWithValue("@File", encrypt(draft.SourceFileName));
                command.Parameters.AddWithValue("@Hash", draft.SourceHash);
                command.Parameters.AddWithValue("@Record", draft.SourceRecordNumber);
                command.Parameters.AddWithValue("@Patient", patientId);
                command.Parameters.AddWithValue("@User", actor.UserId);
                command.Parameters.AddWithValue("@At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO ActivityLogs (UserId, UserCode, Username, Action, Module, Description, CreatedAt)
VALUES (@User, @Code, @Name, 'Import', 'Data Migration', @Description, @At);";
                command.Parameters.AddWithValue("@User", actor.UserId);
                command.Parameters.AddWithValue("@Code", actor.UserCode);
                command.Parameters.AddWithValue("@Name", actor.Username);
                command.Parameters.AddWithValue("@Description", $"Staff verified and imported patient #{patientId}, source {draft.SourceHash[..12]}, record {draft.SourceRecordNumber}.");
                command.Parameters.AddWithValue("@At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                command.ExecuteNonQuery();
            }
            using (var codeCommand = connection.CreateCommand())
            {
                codeCommand.Transaction = transaction;
                codeCommand.CommandText = "SELECT PatientCode FROM Patients WHERE PatientId = @PatientId;";
                codeCommand.Parameters.AddWithValue("@PatientId", patientId);
                patientCodes.Add(codeCommand.ExecuteScalar()?.ToString() ?? $"#{patientId}");
            }
            snapshot.ImportedSources.Add(sourceKey);
            var p = validation.Patient!;
            snapshot.Patients.Add(new(patientId, $"#{patientId}", p.FirstName, p.MiddleName, p.LastName, p.BirthDate, p.PhoneNumber));
            ids.Add(patientId);
        }
        transaction.Commit();
        // No visible approval or imported state changes until all records have committed.
        for (int i = 0; i < drafts.Count; i++)
        {
            drafts[i].ImportedPatientCode = patientCodes[i];
            drafts[i].IsImported = true;
            drafts[i].IncludeInImport = false;
            drafts[i].ValidationText = $"Imported successfully as patient #{ids[i]}.";
        }
        return ids;
    }
    public void RecordFailedAttempt(string fileName, int recordNumber, string result, string details)
    {
        if (!SessionService.CanAccessModule("DataMigration") || SessionService.CurrentUser == null)
            throw new UnauthorizedAccessException("Sign in with an authorized clinic account to record migration results.");
        if (result is not ("Failed" or "Rejected")) throw new ArgumentException("Unsupported migration result.", nameof(result));
        using var connection = getConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO PatientMigrationAttempts (SourceFileName, SourceRecordNumber, Result, Details, ProcessedByUserId, ProcessedAt)
VALUES (@File, @Record, @Result, @Details, @User, @At);";
        command.Parameters.AddWithValue("@File", encrypt(fileName));
        command.Parameters.AddWithValue("@Record", recordNumber);
        command.Parameters.AddWithValue("@Result", result);
        command.Parameters.AddWithValue("@Details", encrypt(details));
        command.Parameters.AddWithValue("@User", SessionService.CurrentUser.UserId);
        command.Parameters.AddWithValue("@At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<MigrationResultItem> GetHistory()
    {
        if (!SessionService.CanAccessModule("DataMigration"))
            throw new UnauthorizedAccessException("Sign in with an authorized clinic account to view migration results.");
        using var connection = getConnection();
        connection.Open();
        List<MigrationResultItem> history = new();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT m.SourceFileName, m.SourceRecordNumber, p.PatientCode, p.FirstName, p.MiddleName, p.LastName,
    COALESCE(u.Username, 'Unknown user') AS ProcessedBy, m.ImportedAt
FROM PatientMigrationRecords m
JOIN Patients p ON p.PatientId = m.PatientId
LEFT JOIN Users u ON u.UserId = m.ReviewedByUserId
ORDER BY m.MigrationRecordId DESC
LIMIT 100;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                history.Add(new(decrypt(reader["SourceFileName"].ToString()), Convert.ToInt32(reader["SourceRecordNumber"]),
                    reader["PatientCode"].ToString()!, string.Join(" ", new[] { reader["FirstName"].ToString(),
                    reader["MiddleName"].ToString(), reader["LastName"].ToString() }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    "Saved", "Approved patient information saved with an audit entry.",
                    reader["ProcessedBy"].ToString()!, DateTime.Parse(reader["ImportedAt"].ToString()!,
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT a.SourceFileName, a.SourceRecordNumber, a.Result, a.Details,
    COALESCE(u.Username, 'Unknown user') AS ProcessedBy, a.ProcessedAt
FROM PatientMigrationAttempts a
LEFT JOIN Users u ON u.UserId = a.ProcessedByUserId
ORDER BY a.AttemptId DESC
LIMIT 100;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                history.Add(new(decrypt(reader["SourceFileName"].ToString()), Convert.ToInt32(reader["SourceRecordNumber"]),
                    "", "", reader["Result"].ToString()!, decrypt(reader["Details"].ToString()),
                    reader["ProcessedBy"].ToString()!, DateTime.Parse(reader["ProcessedAt"].ToString()!,
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        return history.OrderByDescending(x => x.ProcessedAt).Take(100).ToArray();
    }

}

// Keep decryption out of SQL so encrypted phone fields can participate in duplicate warnings.
internal static class MigrationIdentityExtensions
{
    public static void ReplaceAllPhones(this List<MigrationIdentity> patients, Func<string?, string> decrypt)
    {
        for (int i = 0; i < patients.Count; i++)
            patients[i] = patients[i] with { PhoneNumber = decrypt(patients[i].PhoneNumber) };
    }
}

