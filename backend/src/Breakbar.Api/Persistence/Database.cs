using Breakbar.Api.Options;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Breakbar.Api.Persistence;

/// <summary>SQLite connection factory and schema. One file, WAL mode, short-lived connections (pooled).</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(StorageOptions storage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storage.DatabasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storage.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    static Database()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true; // report_id -> ReportId
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;");
        return connection;
    }

    /// <summary>Creates the schema if missing. Called once at startup.</summary>
    public void Initialize()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        // Not deployed yet: the schema is edited in place. From the first deployment on, changes need
        // migrations (PRAGMA user_version), see docs/PLAN.md 4a.
        // A report is identified by file hash + options: the same log anonymized and not anonymized are two reports.
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS reports (
                id                TEXT PRIMARY KEY,
                sha256            TEXT NOT NULL,
                anonymized        INTEGER NOT NULL DEFAULT 0,
                encounter_id      TEXT,
                log_name          TEXT,
                encounter_success INTEGER,
                duration_ms       INTEGER,
                ei_version        TEXT NOT NULL,
                html_bytes        INTEGER NOT NULL,
                json_bytes        INTEGER NOT NULL,
                created_at        INTEGER NOT NULL,
                UNIQUE (sha256, anonymized)
            );

            CREATE TABLE IF NOT EXISTS jobs (
                id             TEXT PRIMARY KEY,
                user_id        TEXT NOT NULL,
                batch_id       TEXT,
                file_name      TEXT NOT NULL,
                upload_path    TEXT NOT NULL,
                sha256         TEXT NOT NULL,
                anonymized     INTEGER NOT NULL DEFAULT 0,
                file_bytes     INTEGER NOT NULL,
                evtc_bytes     INTEGER NOT NULL,
                estimate_bytes INTEGER NOT NULL,
                status         TEXT NOT NULL,
                report_id      TEXT NOT NULL,
                error          TEXT,
                created_at     INTEGER NOT NULL,
                started_at     INTEGER,
                finished_at    INTEGER
            );

            CREATE INDEX IF NOT EXISTS ix_jobs_user   ON jobs (user_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_jobs_status ON jobs (status, created_at);
            """);
    }
}
