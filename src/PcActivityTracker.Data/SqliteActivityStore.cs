using System.Globalization;
using Microsoft.Data.Sqlite;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Persistence;

namespace PcActivityTracker.Data;

public sealed class SqliteActivityStore(SqliteDatabase database) : IObservationStore, ITrackingBatchStore, IClassificationStore, IPrivacyStore, IWorkTaxonomyStore, IRetentionStore
{
    private const string TimestampFormat = "O";

    public async Task AddObservationAsync(RawObservation value, CancellationToken cancellationToken = default)
    {
        if (value.State == ActivityState.Private) throw new ArgumentException("Le osservazioni private non possono essere persistite.", nameof(value));
        const string sql = """INSERT INTO observations(id,source,observed_at_utc,time_zone_id,observed_offset_minutes,state,process_name,executable_path,file_path,document_type,browser_domain,browser_path,document_precision,document_provenance) VALUES($id,$source,$at,$zone,$offset,$state,$process,$exe,$file,$document,$domain,$path,$precision,$provenance);""";
        await ExecuteAsync(sql, command => BindObservation(command, value), cancellationToken);
    }

    public async Task PersistTrackingBatchAsync(TrackingPersistenceBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Observations.Any(x => x.State == ActivityState.Private)) throw new ArgumentException("Le osservazioni private non possono essere persistite.", nameof(batch));
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var value in batch.Observations)
                await ExecuteTrackingAsync(connection, transaction,
                    "INSERT INTO observations(id,source,observed_at_utc,time_zone_id,observed_offset_minutes,state,process_name,executable_path,file_path,document_type,browser_domain,browser_path,document_precision,document_provenance) VALUES($id,$source,$at,$zone,$offset,$state,$process,$exe,$file,$document,$domain,$path,$precision,$provenance);",
                    command => BindObservation(command, value), cancellationToken);
            foreach (var value in batch.Intervals)
                await ExecuteTrackingAsync(connection, transaction, "INSERT INTO activity_intervals(id,observation_id,start_utc,end_utc,state,end_reason,elapsed_ticks,elapsed_monotonic) VALUES($id,$observation,$start,$end,$state,$reason,$elapsed,$monotonic);",
                    command => { Add(command, "$id", value.Id.Value); Add(command, "$observation", value.ObservationId?.Value); Add(command, "$start", Format(value.Period.Start)); Add(command, "$end", Format(value.Period.End)); Add(command, "$state", (int)value.State); Add(command, "$reason", (int)value.EndReason); Add(command, "$elapsed", value.Elapsed.Ticks); Add(command, "$monotonic", value.IsElapsedMonotonic ? 1 : 0); }, cancellationToken);
            foreach (var value in batch.Gaps)
                await ExecuteTrackingAsync(connection, transaction, "INSERT INTO activity_gaps(id,start_utc,end_utc,state,elapsed_ticks,elapsed_monotonic) VALUES($id,$start,$end,$state,$elapsed,$monotonic);",
                    command => { Add(command, "$id", value.Id.Value); Add(command, "$start", Format(value.Period.Start)); Add(command, "$end", Format(value.Period.End)); Add(command, "$state", (int)value.State); Add(command, "$elapsed", value.Elapsed.Ticks); Add(command, "$monotonic", value.IsElapsedMonotonic ? 1 : 0); }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<RawObservation>> GetObservationsAsync(TimeRange period, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM observations WHERE observed_at_utc >= $start AND observed_at_utc < $end ORDER BY observed_at_utc,id;";
        return await QueryAsync(sql, c => { Add(c, "$start", Format(period.Start)); Add(c, "$end", Format(period.End)); }, reader => new RawObservation(new(Guid.Parse(reader.GetString("id"))), (ObservationSource)reader.GetInt32("source"), Parse(reader.GetString("observed_at_utc")), new(reader.GetString("time_zone_id"), TimeSpan.FromMinutes(reader.GetInt32("observed_offset_minutes"))), (ActivityState)reader.GetInt32("state"), new(reader.GetString("process_name"), Nullable(reader, "executable_path")), Nullable(reader, "file_path") is { } file ? new(file, Nullable(reader, "document_type"), (DocumentResolutionPrecision)reader.GetInt32("document_precision"), (DocumentProvenance)reader.GetInt32("document_provenance")) : null, Nullable(reader, "browser_domain") is { } domain ? new(domain, Nullable(reader, "browser_path")) : null), cancellationToken);
    }

    public async Task AddActivityIntervalAsync(ActivityInterval value, CancellationToken cancellationToken = default) => await PersistTrackingBatchAsync(new([], [value], []), cancellationToken);
    public async Task<IReadOnlyList<ActivityInterval>> GetActivityIntervalsAsync(TimeRange period, CancellationToken cancellationToken = default) => await QueryAsync<ActivityInterval>("SELECT * FROM activity_intervals WHERE start_utc < $end AND end_utc > $start ORDER BY start_utc,id;", c => { Add(c, "$start", Format(period.Start)); Add(c, "$end", Format(period.End)); }, r => new(new(Guid.Parse(r.GetString("id"))), Id<ObservationId>(r, "observation_id", v => new(v)), new(Parse(r.GetString("start_utc")), Parse(r.GetString("end_utc"))), (ActivityState)r.GetInt32("state"), (DiscontinuityReason)r.GetInt32("end_reason"), Elapsed(r, "elapsed_ticks"), r.GetInt32("elapsed_monotonic") == 1), cancellationToken);
    public async Task AddActivityGapAsync(ActivityGap value, CancellationToken cancellationToken = default) => await PersistTrackingBatchAsync(new([], [], [value]), cancellationToken);
    public async Task<IReadOnlyList<ActivityGap>> GetActivityGapsAsync(TimeRange period, CancellationToken cancellationToken = default) => await QueryAsync<ActivityGap>("SELECT * FROM activity_gaps WHERE start_utc < $end AND end_utc > $start ORDER BY start_utc,id;", c => { Add(c, "$start", Format(period.Start)); Add(c, "$end", Format(period.End)); }, r => new(new(Guid.Parse(r.GetString("id"))), new(Parse(r.GetString("start_utc")), Parse(r.GetString("end_utc"))), (ActivityState)r.GetInt32("state"), Elapsed(r, "elapsed_ticks"), r.GetInt32("elapsed_monotonic") == 1), cancellationToken);

    public async Task AddClassificationAsync(Classification value, CancellationToken cancellationToken = default) => await ExecuteAsync("INSERT INTO classifications VALUES($id,$type,$target,$provenance,$at,$rule,$rationale,$project,$job,$category);", c => { Add(c, "$id", value.Id.Value); Add(c, "$type", (int)value.TargetType); Add(c, "$target", value.TargetId); Add(c, "$provenance", (int)value.Provenance); Add(c, "$at", Format(value.ClassifiedAt)); Add(c, "$rule", value.RuleId); Add(c, "$rationale", value.Rationale); Add(c, "$project", value.ProjectId?.Value); Add(c, "$job", value.JobId?.Value); Add(c, "$category", value.CategoryId?.Value); }, cancellationToken);
    public async Task<IReadOnlyList<Classification>> GetClassificationsAsync(ClassificationTargetType type, Guid targetId, CancellationToken cancellationToken = default) => await QueryAsync<Classification>("SELECT * FROM classifications WHERE target_type=$type AND target_id=$target ORDER BY classified_at_utc,id;", c => { Add(c, "$type", (int)type); Add(c, "$target", targetId); }, r => new(new(Guid.Parse(r.GetString("id"))), type, targetId, (ClassificationProvenance)r.GetInt32("provenance"), Parse(r.GetString("classified_at_utc")), r.GetString("rationale"), Id<ProjectId>(r, "project_id", v => new(v)), Id<JobId>(r, "job_id", v => new(v)), Id<CategoryId>(r, "category_id", v => new(v)), Nullable(r, "rule_id")), cancellationToken);

    public async Task SaveExclusionAsync(ExclusionRule value, CancellationToken cancellationToken = default) => await ExecuteAsync("INSERT INTO exclusions VALUES($id,$kind,$pattern,$enabled) ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,pattern=excluded.pattern,enabled=excluded.enabled;", c => { Add(c, "$id", value.Id.Value); Add(c, "$kind", (int)value.Kind); Add(c, "$pattern", value.Pattern); Add(c, "$enabled", value.IsEnabled ? 1 : 0); }, cancellationToken);
    public async Task<IReadOnlyList<ExclusionRule>> GetExclusionsAsync(CancellationToken cancellationToken = default) => await QueryAsync<ExclusionRule>("SELECT * FROM exclusions ORDER BY id;", _ => { }, r => new(new(Guid.Parse(r.GetString("id"))), (ExclusionKind)r.GetInt32("kind"), r.GetString("pattern"), r.GetInt32("enabled") == 1), cancellationToken);
    public async Task SaveProjectAsync(Project value, CancellationToken cancellationToken = default) => await UpsertName("projects", value.Id.Value, value.Name, null, cancellationToken);
    public async Task SaveJobAsync(Job value, CancellationToken cancellationToken = default) => await UpsertName("jobs", value.Id.Value, value.Name, value.ProjectId.Value, cancellationToken);
    public async Task SaveCategoryAsync(Category value, CancellationToken cancellationToken = default) => await UpsertName("categories", value.Id.Value, value.Name, null, cancellationToken);
    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) => await QueryAsync<Project>("SELECT * FROM projects ORDER BY name;", _ => { }, r => new(new(Guid.Parse(r.GetString("id"))), r.GetString("name")), cancellationToken);
    public async Task<IReadOnlyList<Job>> GetJobsAsync(ProjectId projectId, CancellationToken cancellationToken = default) => await QueryAsync<Job>("SELECT * FROM jobs WHERE project_id=$project ORDER BY name;", c => Add(c, "$project", projectId.Value), r => new(new(Guid.Parse(r.GetString("id"))), projectId, r.GetString("name")), cancellationToken);
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => await QueryAsync<Category>("SELECT * FROM categories ORDER BY name;", _ => { }, r => new(new(Guid.Parse(r.GetString("id"))), r.GetString("name")), cancellationToken);

    public async Task<int> DeleteActivityBeforeAsync(UtcInstant cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var formattedCutoff = Format(cutoff);
        await ExecuteAsync(connection, transaction, "DELETE FROM activity_intervals WHERE end_utc <= $cutoff; UPDATE activity_intervals SET start_utc = $cutoff,elapsed_ticks=NULL,elapsed_monotonic=0 WHERE start_utc < $cutoff AND end_utc > $cutoff; DELETE FROM activity_gaps WHERE end_utc <= $cutoff; UPDATE activity_gaps SET start_utc = $cutoff,elapsed_ticks=NULL,elapsed_monotonic=0 WHERE start_utc < $cutoff AND end_utc > $cutoff;", formattedCutoff, cancellationToken);
        var deleted = await ExecuteAsync(connection, transaction, "DELETE FROM observations WHERE observed_at_utc < $cutoff;", formattedCutoff, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private async Task UpsertName(string table, Guid id, string name, Guid? projectId, CancellationToken token) => await ExecuteAsync(projectId is null ? $"INSERT INTO {table}(id,name) VALUES($id,$name) ON CONFLICT(id) DO UPDATE SET name=excluded.name;" : $"INSERT INTO {table}(id,project_id,name) VALUES($id,$project,$name) ON CONFLICT(id) DO UPDATE SET project_id=excluded.project_id,name=excluded.name;", c => { Add(c, "$id", id); Add(c, "$name", name); if (projectId is not null) Add(c, "$project", projectId); }, token);
    private async Task<int> ExecuteAsync(string sql, Action<SqliteCommand> bind, CancellationToken token) { await using var connection = await database.OpenConnectionAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql; bind(command); return await command.ExecuteNonQueryAsync(token); }
    private static async Task ExecuteTrackingAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, Action<SqliteCommand> bind, CancellationToken token) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; bind(command); await command.ExecuteNonQueryAsync(token); }
    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string cutoff, CancellationToken token) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; Add(command, "$cutoff", cutoff); return await command.ExecuteNonQueryAsync(token); }
    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> map, CancellationToken token) { var result = new List<T>(); await using var connection = await database.OpenConnectionAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql; bind(command); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) result.Add(map(reader)); return result; }
    private static void Add(SqliteCommand c, string name, object? value) => c.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static void BindObservation(SqliteCommand command, RawObservation value) { Add(command, "$id", value.Id.Value); Add(command, "$source", (int)value.Source); Add(command, "$at", Format(value.ObservedAt)); Add(command, "$zone", value.LocalTime.TimeZoneId); Add(command, "$offset", (int)value.LocalTime.ObservedUtcOffset.TotalMinutes); Add(command, "$state", (int)value.State); Add(command, "$process", value.Application.ProcessName); Add(command, "$exe", value.Application.ExecutablePath); Add(command, "$file", value.File?.Path); Add(command, "$document", value.File?.DocumentType); Add(command, "$domain", value.Browser?.Domain); Add(command, "$path", value.Browser?.Path); Add(command, "$precision", (int)(value.File?.Precision ?? DocumentResolutionPrecision.FullPath)); Add(command, "$provenance", (int)(value.File?.Provenance ?? DocumentProvenance.DirectlyObserved)); }
    private static string Format(UtcInstant value) => value.Value.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    private static UtcInstant Parse(string value) => new(DateTimeOffset.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
    private static string? Nullable(SqliteDataReader r, string name) => r.IsDBNull(name) ? null : r.GetString(name);
    private static TimeSpan? Elapsed(SqliteDataReader r, string name) => r.IsDBNull(name) ? null : TimeSpan.FromTicks(r.GetInt64(name));
    private static T? Id<T>(SqliteDataReader r, string name, Func<Guid, T> create) where T : struct => r.IsDBNull(name) ? null : create(Guid.Parse(r.GetString(name)));
}

internal static class SqliteDataReaderExtensions
{
    internal static string GetString(this SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    internal static int GetInt32(this SqliteDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    internal static long GetInt64(this SqliteDataReader reader, string name) => reader.GetInt64(reader.GetOrdinal(name));
    internal static bool IsDBNull(this SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name));
}
