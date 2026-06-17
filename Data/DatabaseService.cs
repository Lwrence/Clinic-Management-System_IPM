using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.IO;
using System.Linq;

namespace CruzNeryClinic.Data
{
    public static class DatabaseService
    {
        private static readonly string AppFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CruzNeryClinic");

        // Optional override file. If present, its first non-empty line is used as the
        // full path to the database file (e.g. a shared file on another laptop reached
        // via a UNC path like \\192.168.1.5\ClinicDB\cruz_nery_clinic.db).
        // When the file is absent or empty, the app falls back to the local default,
        // so standalone installs keep working exactly as before.
        private static readonly string ConfigPath =
            Path.Combine(AppFolder, "dbpath.config");

        private static readonly string DefaultDatabasePath =
            Path.Combine(AppFolder, "cruz_nery_clinic.db");

        private static readonly string DatabasePath = ResolveDatabasePath();

        public static string ConnectionString => $"Data Source={DatabasePath}";

        // True when a dbpath.config override points the database somewhere other than
        // the local default (i.e. the LAN-shared setup). In that mode the encryption
        // key is co-located with the shared database so every laptop uses the same key.
        public static bool IsSharedDatabase =>
            !string.Equals(DatabasePath, DefaultDatabasePath, StringComparison.OrdinalIgnoreCase);

        // The folder that contains the active database (the shared folder in LAN mode).
        public static string DatabaseDirectory =>
            Path.GetDirectoryName(DatabasePath) ?? AppFolder;

        public static SqliteConnection GetConnection()
        {
            if (!Directory.Exists(AppFolder))
                Directory.CreateDirectory(AppFolder);

            SqliteConnection connection = new SqliteConnection(ConnectionString);

            // Apply a busy timeout to every connection through this single chokepoint.
            // When the database lives on a network share and two laptops write close
            // together, SQLite waits/retries for up to 5 seconds instead of throwing
            // "database is locked" immediately.
            connection.StateChange += (_, e) =>
            {
                if (e.CurrentState == ConnectionState.Open)
                {
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "PRAGMA busy_timeout = 5000;";
                    command.ExecuteNonQuery();
                }
            };

            return connection;
        }

        public static string GetDatabasePath()
        {
            if (!Directory.Exists(AppFolder))
                Directory.CreateDirectory(AppFolder);

            return DatabasePath;
        }

        private static string ResolveDatabasePath()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string? overridePath = File.ReadAllLines(ConfigPath)
                        .Select(line => line.Trim())
                        .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("#"));

                    if (!string.IsNullOrWhiteSpace(overridePath))
                        return overridePath;
                }
            }
            catch
            {
                // If the override file is unreadable for any reason, fall back to the
                // default local database rather than failing to start.
            }

            return DefaultDatabasePath;
        }
    }
}
