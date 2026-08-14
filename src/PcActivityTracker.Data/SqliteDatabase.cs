using Microsoft.Data.Sqlite;

namespace PcActivityTracker.Data;

public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 1;
    private readonly string connectionString;
    private readonly IReadOnlyList<SqliteMigration> migrations;

    public SqliteDatabase(string databasePath) : this(databasePath, DefaultMigrations.All) { }

    public SqliteDatabase(string databasePath, IReadOnlyList<SqliteMigration> migrations)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Il percorso del database è obbligatorio.", nameof(databasePath));
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        this.migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL CHECK(version >= 0)); INSERT INTO schema_info(version) SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_info);", cancellationToken);
        var version = await GetSchemaVersionAsync(connection, cancellationToken);
        foreach (var migration in migrations.OrderBy(item => item.Version).Where(item => item.Version > version))
        {
            if (migration.Version != version + 1) throw new InvalidOperationException($"Migrazione {version + 1} mancante.");
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await ExecuteAsync(connection, migration.Sql, cancellationToken, transaction);
                await ExecuteAsync(connection, $"UPDATE schema_info SET version = {migration.Version};", cancellationToken, transaction);
                await transaction.CommitAsync(cancellationToken);
                version = migration.Version;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GetSchemaVersionAsync(connection, cancellationToken);
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", cancellationToken);
        return connection;
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, System.Data.Common.DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = (SqliteTransaction?)transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record SqliteMigration(int Version, string Sql);

internal static class DefaultMigrations
{
    internal static readonly IReadOnlyList<SqliteMigration> All =
    [
        new(1, """
        CREATE TABLE projects (id TEXT PRIMARY KEY, name TEXT NOT NULL CHECK(length(trim(name)) > 0));
        CREATE TABLE jobs (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, name TEXT NOT NULL CHECK(length(trim(name)) > 0));
        CREATE INDEX ix_jobs_project ON jobs(project_id);
        CREATE TABLE categories (id TEXT PRIMARY KEY, name TEXT NOT NULL CHECK(length(trim(name)) > 0));
        CREATE TABLE observations (
          id TEXT PRIMARY KEY, source INTEGER NOT NULL, observed_at_utc TEXT NOT NULL,
          time_zone_id TEXT NOT NULL, observed_offset_minutes INTEGER NOT NULL CHECK(observed_offset_minutes BETWEEN -840 AND 840),
          state INTEGER NOT NULL, process_name TEXT NOT NULL, executable_path TEXT,
          file_path TEXT, document_type TEXT, browser_domain TEXT, browser_path TEXT,
          CHECK(source <> 1 OR file_path IS NOT NULL), CHECK(source <> 2 OR browser_domain IS NOT NULL));
        CREATE INDEX ix_observations_time ON observations(observed_at_utc);
        CREATE TRIGGER observations_immutable BEFORE UPDATE ON observations BEGIN SELECT RAISE(ABORT, 'raw observation is immutable'); END;
        CREATE TABLE activity_intervals (
          id TEXT PRIMARY KEY, observation_id TEXT NOT NULL REFERENCES observations(id) ON DELETE CASCADE,
          start_utc TEXT NOT NULL, end_utc TEXT NOT NULL, state INTEGER NOT NULL, end_reason INTEGER NOT NULL,
          CHECK(end_utc >= start_utc));
        CREATE INDEX ix_activity_intervals_time ON activity_intervals(start_utc, end_utc);
        CREATE TABLE classifications (
          id TEXT PRIMARY KEY, target_type INTEGER NOT NULL, target_id TEXT NOT NULL, provenance INTEGER NOT NULL,
          classified_at_utc TEXT NOT NULL, rule_id TEXT, rationale TEXT NOT NULL CHECK(length(trim(rationale)) > 0),
          project_id TEXT REFERENCES projects(id) ON DELETE SET NULL,
          job_id TEXT REFERENCES jobs(id) ON DELETE SET NULL,
          category_id TEXT REFERENCES categories(id) ON DELETE SET NULL,
          CHECK(provenance <> 1 OR rule_id IS NOT NULL));
        CREATE INDEX ix_classifications_target ON classifications(target_type, target_id, classified_at_utc);
        CREATE INDEX ix_classifications_project ON classifications(project_id);
        CREATE INDEX ix_classifications_job ON classifications(job_id);
        CREATE INDEX ix_classifications_category ON classifications(category_id);
        CREATE TRIGGER classifications_target_exists BEFORE INSERT ON classifications
        WHEN (NEW.target_type = 0 AND NOT EXISTS (SELECT 1 FROM observations WHERE id = NEW.target_id))
          OR (NEW.target_type = 1 AND NOT EXISTS (SELECT 1 FROM activity_intervals WHERE id = NEW.target_id))
        BEGIN SELECT RAISE(ABORT, 'classification target does not exist'); END;
        CREATE TRIGGER observations_delete_classifications AFTER DELETE ON observations
        BEGIN DELETE FROM classifications WHERE target_type = 0 AND target_id = OLD.id; END;
        CREATE TRIGGER intervals_delete_classifications AFTER DELETE ON activity_intervals
        BEGIN DELETE FROM classifications WHERE target_type = 1 AND target_id = OLD.id; END;
        CREATE TABLE exclusions (id TEXT PRIMARY KEY, kind INTEGER NOT NULL, pattern TEXT NOT NULL CHECK(length(trim(pattern)) > 0), enabled INTEGER NOT NULL CHECK(enabled IN (0,1)));
        """)
    ];
}
