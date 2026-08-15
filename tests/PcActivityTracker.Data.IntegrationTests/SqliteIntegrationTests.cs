using Microsoft.Data.Sqlite;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Data;
using Xunit;

namespace PcActivityTracker.Data.IntegrationTests;

public sealed class SqliteIntegrationTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"pcactivity-{Guid.NewGuid():N}.db");
    private static readonly UtcInstant Now = new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix); }

    [Fact] public async Task NewDatabaseMigratesToLatestSchema() { var db = await Create(); Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await db.GetSchemaVersionAsync()); }
    [Fact] public async Task InitializeCanReopenDatabase() { await Create(); var reopened = new SqliteDatabase(path); await reopened.InitializeAsync(); Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await reopened.GetSchemaVersionAsync()); }
    [Fact]
    public async Task MigrationV1ToV2PreservesRowsAndMarksLegacyElapsedAsCivilFallback()
    {
        var migrations = new[]
        {
            new SqliteMigration(1, "CREATE TABLE activity_intervals(id TEXT PRIMARY KEY,observation_id TEXT,start_utc TEXT NOT NULL,end_utc TEXT NOT NULL,state INTEGER NOT NULL,end_reason INTEGER NOT NULL); CREATE TABLE activity_gaps(id TEXT PRIMARY KEY,start_utc TEXT NOT NULL,end_utc TEXT NOT NULL,state INTEGER NOT NULL); INSERT INTO activity_gaps VALUES('legacy','2026-08-14T12:00:00.0000000+00:00','2026-08-14T12:01:00.0000000+00:00',1);"),
            new SqliteMigration(2, "ALTER TABLE activity_intervals ADD COLUMN elapsed_ticks INTEGER NULL CHECK(elapsed_ticks IS NULL OR elapsed_ticks >= 0); ALTER TABLE activity_intervals ADD COLUMN elapsed_monotonic INTEGER NOT NULL DEFAULT 0 CHECK(elapsed_monotonic IN (0,1)); ALTER TABLE activity_gaps ADD COLUMN elapsed_ticks INTEGER NULL CHECK(elapsed_ticks IS NULL OR elapsed_ticks >= 0); ALTER TABLE activity_gaps ADD COLUMN elapsed_monotonic INTEGER NOT NULL DEFAULT 0 CHECK(elapsed_monotonic IN (0,1));")
        };
        var database = new SqliteDatabase(path, migrations); await database.InitializeAsync();
        await using var connection = Open(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT elapsed_ticks,elapsed_monotonic FROM activity_gaps WHERE id='legacy';";
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(0)); Assert.Equal(0L, reader.GetInt64(1));
    }
    [Fact] public async Task ObservationRoundTrips() { var db = await Create(); var store = new SqliteActivityStore(db); var value = Observation(); await store.AddObservationAsync(value); var found = await store.GetObservationsAsync(new(new(Now.Value.AddMinutes(-1)), new(Now.Value.AddMinutes(1)))); Assert.Equal(value, Assert.Single(found)); }
    [Fact] public async Task FileNamePrecisionAndProvenanceRoundTrip() { var db = await Create(); var store = new SqliteActivityStore(db); var value = new RawObservation(ObservationId.New(), ObservationSource.FileDocument, Now, new("UTC", TimeSpan.Zero), ActivityState.Active, new("WINWORD"), new("draft.docx", Precision: DocumentResolutionPrecision.FileNameOnly, Provenance: DocumentProvenance.Derived)); await store.AddObservationAsync(value); var found = Assert.Single(await store.GetObservationsAsync(new(new(Now.Value.AddMinutes(-1)), new(Now.Value.AddMinutes(1))))); Assert.Equal(value.File, found.File); }
    [Fact] public async Task ActivityIntervalRoundTrips() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(); await store.AddObservationAsync(observation); var interval = new ActivityInterval(ActivityIntervalId.New(), observation.Id, new(Now, new(Now.Value.AddMinutes(2))), ActivityState.Active); await store.AddActivityIntervalAsync(interval); Assert.Equal(interval, Assert.Single(await store.GetActivityIntervalsAsync(new(Now, new(Now.Value.AddHours(1)))))); }
    [Fact] public async Task ForeignKeysRejectOrphanInterval() { var db = await Create(); var store = new SqliteActivityStore(db); var interval = new ActivityInterval(ActivityIntervalId.New(), ObservationId.New(), new(Now, new(Now.Value.AddMinutes(1))), ActivityState.Active); await Assert.ThrowsAsync<SqliteException>(() => store.AddActivityIntervalAsync(interval)); }
    [Fact] public async Task IntegrityRejectsOrphanClassification() { var db = await Create(); var store = new SqliteActivityStore(db); var classification = new Classification(ClassificationId.New(), ClassificationTargetType.Observation, Guid.NewGuid(), ClassificationProvenance.Manual, Now, "orfana"); await Assert.ThrowsAsync<SqliteException>(() => store.AddClassificationAsync(classification)); }
    [Fact] public async Task IntegrityAcceptsIntervalTargetAndRejectsMissingIntervalTarget() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(); await store.AddObservationAsync(observation); var interval = new ActivityInterval(ActivityIntervalId.New(), observation.Id, new(Now, new(Now.Value.AddMinutes(1))), ActivityState.Active); await store.AddActivityIntervalAsync(interval); var valid = new Classification(ClassificationId.New(), ClassificationTargetType.ActivityInterval, interval.Id.Value, ClassificationProvenance.Manual, Now, "valida"); await store.AddClassificationAsync(valid); Assert.Equal(valid, Assert.Single(await store.GetClassificationsAsync(ClassificationTargetType.ActivityInterval, interval.Id.Value))); var missing = new Classification(ClassificationId.New(), ClassificationTargetType.ActivityInterval, Guid.NewGuid(), ClassificationProvenance.Manual, Now, "orfana"); await Assert.ThrowsAsync<SqliteException>(() => store.AddClassificationAsync(missing)); }
    [Fact] public async Task ClassificationDoesNotRewriteObservation() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(); await store.AddObservationAsync(observation); var classification = new Classification(ClassificationId.New(), ClassificationTargetType.Observation, observation.Id.Value, ClassificationProvenance.Manual, Now, "correzione"); await store.AddClassificationAsync(classification); Assert.Equal(classification, Assert.Single(await store.GetClassificationsAsync(ClassificationTargetType.Observation, observation.Id.Value))); Assert.Equal(observation, Assert.Single(await store.GetObservationsAsync(new(new(Now.Value.AddMinutes(-1)), new(Now.Value.AddMinutes(1)))))); }
    [Fact] public async Task TaxonomyAndExclusionsRoundTrip() { var db = await Create(); var store = new SqliteActivityStore(db); var project = new Project(ProjectId.New(), "Tracker"); var job = new Job(JobId.New(), project.Id, "Sprint 01"); var category = new Category(CategoryId.New(), "Sviluppo"); var exclusion = new ExclusionRule(ExclusionRuleId.New(), ExclusionKind.Application, "password-manager"); await store.SaveProjectAsync(project); await store.SaveJobAsync(job); await store.SaveCategoryAsync(category); await store.SaveExclusionAsync(exclusion); Assert.Equal(project, Assert.Single(await store.GetProjectsAsync())); Assert.Equal(job, Assert.Single(await store.GetJobsAsync(project.Id))); Assert.Equal(category, Assert.Single(await store.GetCategoriesAsync())); Assert.Equal(exclusion, Assert.Single(await store.GetExclusionsAsync())); }
    [Fact] public async Task RetentionDeletesIntervalEntirelyBeforeCutoff() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(new(Now.Value.AddHours(-2))); await store.AddObservationAsync(observation); await store.AddActivityIntervalAsync(new(ActivityIntervalId.New(), observation.Id, new(observation.ObservedAt, new(Now.Value.AddHours(-1))), ActivityState.Active)); await store.DeleteActivityBeforeAsync(Now); Assert.Empty(await store.GetActivityIntervalsAsync(new(new(Now.Value.AddHours(-3)), new(Now.Value.AddHours(1))))); }
    [Fact] public async Task RetentionPreservesIntervalEntirelyAfterCutoff() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(new(Now.Value.AddMinutes(1))); await store.AddObservationAsync(observation); var interval = new ActivityInterval(ActivityIntervalId.New(), observation.Id, new(observation.ObservedAt, new(Now.Value.AddMinutes(2))), ActivityState.Active); await store.AddActivityIntervalAsync(interval); await store.DeleteActivityBeforeAsync(Now); Assert.Equal(interval, Assert.Single(await store.GetActivityIntervalsAsync(new(Now, new(Now.Value.AddHours(1)))))); }
    [Fact] public async Task RetentionTrimsCrossingIntervalAndDetachesDeletedEvidence() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(new(Now.Value.AddHours(-1))); await store.AddObservationAsync(observation); var interval = new ActivityInterval(ActivityIntervalId.New(), observation.Id, new(observation.ObservedAt, new(Now.Value.AddHours(1))), ActivityState.Active); await store.AddActivityIntervalAsync(interval); await store.DeleteActivityBeforeAsync(Now); var retained = Assert.Single(await store.GetActivityIntervalsAsync(new(Now, new(Now.Value.AddHours(2))))); Assert.Equal(Now, retained.Period.Start); Assert.Null(retained.ObservationId); }
    [Fact] public async Task RetentionDeletesClassificationsOfDeletedTargets() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(new(Now.Value.AddHours(-2))); await store.AddObservationAsync(observation); var interval = new ActivityInterval(ActivityIntervalId.New(), observation.Id, new(observation.ObservedAt, new(Now.Value.AddHours(-1))), ActivityState.Active); await store.AddActivityIntervalAsync(interval); await store.AddClassificationAsync(new(ClassificationId.New(), ClassificationTargetType.Observation, observation.Id.Value, ClassificationProvenance.Manual, Now, "osservazione")); await store.AddClassificationAsync(new(ClassificationId.New(), ClassificationTargetType.ActivityInterval, interval.Id.Value, ClassificationProvenance.Manual, Now, "intervallo")); await store.DeleteActivityBeforeAsync(Now); Assert.Empty(await store.GetClassificationsAsync(ClassificationTargetType.Observation, observation.Id.Value)); Assert.Empty(await store.GetClassificationsAsync(ClassificationTargetType.ActivityInterval, interval.Id.Value)); }
    [Fact] public async Task ObservationImmutableTriggerRejectsUpdate() { await Create(); var store = new SqliteActivityStore(new(path)); var observation = Observation(); await store.AddObservationAsync(observation); await using var connection = Open(); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE observations SET process_name='changed' WHERE id=$id;"; command.Parameters.AddWithValue("$id", observation.Id.Value); var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync()); Assert.Contains("immutable", error.Message, StringComparison.OrdinalIgnoreCase); }
    [Fact] public async Task DatabaseRejectsPrivateRawActivity() { await Create(); await using var connection = Open(); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO observations(id,source,observed_at_utc,time_zone_id,observed_offset_minutes,state,process_name) VALUES($id,0,$at,'UTC',0,5,'secret');"; command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$at", Now.Value.ToString("O")); await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync()); }
    [Fact] public async Task PrivateGapRoundTripsWithoutIdentifyingContent() { var db = await Create(); var store = new SqliteActivityStore(db); var gap = new ActivityGap(ActivityGapId.New(), new(Now, new(Now.Value.AddMinutes(10))), ActivityState.Private); await store.AddActivityGapAsync(gap); Assert.Equal(gap, Assert.Single(await store.GetActivityGapsAsync(new(Now, new(Now.Value.AddHours(1)))))); }
    [Fact] public async Task DatabaseRejectsProjectAndJobTogether() { var db = await Create(); var store = new SqliteActivityStore(db); var observation = Observation(); await store.AddObservationAsync(observation); var first = new Project(ProjectId.New(), "first"); var second = new Project(ProjectId.New(), "second"); var job = new Job(JobId.New(), second.Id, "job"); await store.SaveProjectAsync(first); await store.SaveProjectAsync(second); await store.SaveJobAsync(job); await using var connection = Open(); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO classifications(id,target_type,target_id,provenance,classified_at_utc,rationale,project_id,job_id) VALUES($id,0,$target,0,$at,'incoerente',$project,$job);"; command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$target", observation.Id.Value); command.Parameters.AddWithValue("$at", Now.Value.ToString("O")); command.Parameters.AddWithValue("$project", first.Id.Value); command.Parameters.AddWithValue("$job", job.Id.Value); await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync()); }
    [Fact]
    public async Task DatabaseRejectsEmptyTaxonomyIdentifiers()
    {
        var database = await Create();
        var store = new SqliteActivityStore(database);
        var project = new Project(ProjectId.New(), "valid");
        await store.SaveProjectAsync(project);
        await using var connection = Open();
        await using var projectIdCommand = connection.CreateCommand();
        projectIdCommand.CommandText = "SELECT id FROM projects LIMIT 1;";
        var persistedProjectId = (string)(await projectIdCommand.ExecuteScalarAsync())!;
        var statements = new[]
        {
            "INSERT INTO projects VALUES('00000000-0000-0000-0000-000000000000','bad');",
            "INSERT INTO categories VALUES('00000000-0000-0000-0000-000000000000','bad');",
            $"INSERT INTO jobs VALUES('00000000-0000-0000-0000-000000000000','{persistedProjectId}','bad');",
        };
        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        }
    }
    [Fact] public async Task ExpectedIndexesAndWalExist() { await Create(); await using var connection = Open(); var indexes = await Strings(connection, "SELECT name FROM sqlite_master WHERE type='index';"); Assert.Contains("ix_observations_time", indexes); Assert.Contains("ix_activity_intervals_time", indexes); Assert.Contains("ix_classifications_project", indexes); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode;"; Assert.Equal("wal", await command.ExecuteScalarAsync()); }
    [Fact] public async Task FailedMigrationRollsBackAtomically() { var migrations = new[] { new SqliteMigration(1, "CREATE TABLE stable(id INTEGER);"), new SqliteMigration(2, "CREATE TABLE partial(id INTEGER); INVALID SQL;") }; var db = new SqliteDatabase(path, migrations); await Assert.ThrowsAsync<SqliteException>(() => db.InitializeAsync()); await using var connection = Open(); Assert.DoesNotContain("partial", await Strings(connection, "SELECT name FROM sqlite_master WHERE type='table';")); await using var command = connection.CreateCommand(); command.CommandText = "SELECT version FROM schema_info;"; Assert.Equal(1L, await command.ExecuteScalarAsync()); }
    [Fact] public async Task InterruptedTransactionLeavesNoPartialRows() { await Create(); await using (var connection = Open()) { await using var transaction = await connection.BeginTransactionAsync(); await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction; command.CommandText = "INSERT INTO categories VALUES('one','one'); INSERT INTO categories VALUES('two','two');"; await command.ExecuteNonQueryAsync(); await transaction.RollbackAsync(); } await using var reopened = Open(); await using var count = reopened.CreateCommand(); count.CommandText = "SELECT count(*) FROM categories;"; Assert.Equal(0L, await count.ExecuteScalarAsync()); }

    [Fact]
    public async Task RuntimePipelinePersistsObservationIntervalAndPausedGap()
    {
        var database = await Create(); var store = new SqliteActivityStore(database);
        var source = new QueuedSource([RuntimeSignal(TrackingSignalKind.Start, 0), RuntimeSignal(TrackingSignalKind.Pause, 10), RuntimeSignal(TrackingSignalKind.Resume, 20), RuntimeSignal(TrackingSignalKind.Stop, 30)], new(42, "editor"));
        var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); var coordinator = new TrackingCoordinator(machine, store, source);
        await coordinator.RunAsync();
        var period = new TimeRange(Now, new(Now.Value.AddMinutes(1)));
        Assert.Equal(2, (await store.GetObservationsAsync(period)).Count);
        Assert.Equal(2, (await store.GetActivityIntervalsAsync(period)).Count);
        Assert.Equal(ActivityState.Paused, Assert.Single(await store.GetActivityGapsAsync(period)).State);
    }
    [Fact]
    public async Task RuntimePipelineNeverPersistsPrivateObservation()
    {
        var database = await Create(); var store = new SqliteActivityStore(database);
        var source = new QueuedSource([RuntimeSignal(TrackingSignalKind.Start, 0), RuntimeSignal(TrackingSignalKind.EnterPrivate, 1), RuntimeSignal(TrackingSignalKind.ForegroundChanged, 2, new(99, "secret")), RuntimeSignal(TrackingSignalKind.ExitPrivate, 3), RuntimeSignal(TrackingSignalKind.Stop, 4)], new(42, "editor"));
        var coordinator = new TrackingCoordinator(new(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)), store, source);
        await coordinator.RunAsync();
        var observations = await store.GetObservationsAsync(new(Now, new(Now.Value.AddMinutes(1))));
        Assert.DoesNotContain(observations, x => x.Application.ProcessName == "secret");
        Assert.Equal(ActivityState.Private, Assert.Single(await store.GetActivityGapsAsync(new(Now, new(Now.Value.AddMinutes(1))))).State);
    }
    [Fact]
    public async Task TrackingBatchRollsBackAtomicallyWhenAnyWriteFails()
    {
        var database = await Create(); var store = new SqliteActivityStore(database); var observation = Observation();
        var invalidInterval = new ActivityInterval(ActivityIntervalId.New(), ObservationId.New(), new(Now, new(Now.Value.AddSeconds(1))), ActivityState.Active);
        await Assert.ThrowsAsync<SqliteException>(() => store.PersistTrackingBatchAsync(new([observation], [invalidInterval], [])));
        Assert.Empty(await store.GetObservationsAsync(new(new(Now.Value.AddSeconds(-1)), new(Now.Value.AddSeconds(2)))));
    }

    private static TrackingSignal RuntimeSignal(TrackingSignalKind kind, int seconds, ForegroundSnapshot? foreground = null) => new(kind, new(Now.Value.AddSeconds(seconds)), new(seconds), foreground);
    private sealed class QueuedSource(IEnumerable<TrackingSignal> signals, ForegroundSnapshot? foreground) : ITrackingSignalSource
    {
        private TrackingSignal current = RuntimeSignal(TrackingSignalKind.Start, 0);
        private bool reconcile;
        public async IAsyncEnumerable<TrackingSignal> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var signal in signals) { cancellationToken.ThrowIfCancellationRequested(); current = signal; yield return signal; if (reconcile) { reconcile = false; yield return current with { Kind = TrackingSignalKind.Reconcile, Foreground = foreground }; } await Task.Yield(); } }
        public ValueTask PublishAsync(TrackingSignalKind kind, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void RequestReconciliation() => reconcile = true;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private async Task<SqliteDatabase> Create() { var db = new SqliteDatabase(path); await db.InitializeAsync(); return db; }
    private SqliteConnection Open() { var c = new SqliteConnection($"Data Source={path}"); c.Open(); return c; }
    private static RawObservation Observation(UtcInstant? at = null) => new(ObservationId.New(), ObservationSource.ForegroundApplication, at ?? Now, new("Europe/Rome", TimeSpan.FromHours(2)), ActivityState.Active, new("editor", "/opt/editor"));
    private static async Task<List<string>> Strings(SqliteConnection c, string sql) { await using var command = c.CreateCommand(); command.CommandText = sql; var result = new List<string>(); await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) result.Add(reader.GetString(0)); return result; }
}
