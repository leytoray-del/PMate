using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

/// <summary>All data in one object. Used to load the UI, and as the export/backup file format.</summary>
public record Snapshot(
    string Format, int Version, DateTime ExportedAt, AppSettings Settings, List<BoardStatus> Statuses,
    List<Project> Projects, List<Milestone> Milestones, List<TaskItem> Tasks, List<TaskUpdate> Updates,
    List<Note>? Notes = null, List<RaidItem>? RaidItems = null)
{
    public const string FormatName = "pmate";
    public const int CurrentVersion = 4;
}

public record ImportResult(int Projects, int Milestones, int Tasks, int Updates, int Notes, int RaidItems);

public class DataService(AppDbContext db, SettingsService settings, StatusService statuses)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Snapshot> GetSnapshotAsync() => new(
        Snapshot.FormatName, Snapshot.CurrentVersion, DateTime.UtcNow,
        await settings.GetAsync(),
        await statuses.ListAsync(),
        await db.Projects.AsNoTracking().ToListAsync(),
        await db.Milestones.AsNoTracking().ToListAsync(),
        await db.Tasks.AsNoTracking().ToListAsync(),
        await db.Updates.AsNoTracking().ToListAsync(),
        await db.Notes.AsNoTracking().ToListAsync(),
        await db.RaidItems.AsNoTracking().ToListAsync());

    /// <summary>
    /// Imports a backup. "replace" deletes current data first and also restores settings and columns.
    /// "merge" only adds rows whose id does not exist yet, and maps unknown columns to existing ones.
    /// Accepts this app's format and the older single-file HTML format.
    /// </summary>
    public async Task<ImportResult> ImportAsync(JsonElement root, bool replace)
    {
        var data = root.TryGetProperty("logs", out _) ? LegacyFormat.Read(root) : ReadCurrent(root);

        await using var tx = await db.Database.BeginTransactionAsync();
        if (replace)
        {
            await ClearAsync();
            if (data.Statuses.Count > 0)
            {
                await statuses.ReplaceAsync(data.Statuses.Select(s => new StatusInput(s.Id, s.Name, s.Color, s.IsDone)).ToList());
            }
            if (data.Settings is not null) await settings.SaveAsync(data.Settings);
        }

        var cols = await statuses.ListAsync();
        var known = cols.ToDictionary(s => s.Id);
        var importedDone = data.Statuses.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();
        foreach (var t in data.Tasks.Where(t => !known.ContainsKey(t.Status)))
        {
            t.Status = importedDone.Contains(t.Status) ? cols.First(s => s.IsDone).Id : cols.First(s => !s.IsDone).Id;
        }

        var result = new ImportResult(
            await AddNewAsync(db.Projects, data.Projects, p => p.Id),
            await AddNewAsync(db.Milestones, data.Milestones, m => m.Id),
            await AddNewAsync(db.Tasks, data.Tasks, t => t.Id),
            await AddNewAsync(db.Updates, data.Updates, u => u.Id),
            await AddNewAsync(db.Notes, data.Notes ?? [], n => n.Id),
            await AddNewAsync(db.RaidItems, data.RaidItems ?? [], r => r.Id));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return result;
    }

    /// <summary>Permanently removes sample rows and everything under sample projects. Not undoable.</summary>
    public async Task RemoveSampleAsync()
    {
        var projectIds = await db.Projects.IgnoreQueryFilters().Where(p => p.IsSample).Select(p => p.Id).ToListAsync();
        var taskIds = await db.Tasks.IgnoreQueryFilters().Where(t => t.IsSample || (t.ProjectId != null && projectIds.Contains(t.ProjectId))).Select(t => t.Id).ToListAsync();
        await db.Updates.IgnoreQueryFilters().Where(u => u.IsSample || (u.TaskId != null && taskIds.Contains(u.TaskId)) || (u.ProjectId != null && projectIds.Contains(u.ProjectId))).ExecuteDeleteAsync();
        await db.Tasks.IgnoreQueryFilters().Where(t => taskIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Milestones.IgnoreQueryFilters().Where(m => projectIds.Contains(m.ProjectId)).ExecuteDeleteAsync();
        await db.Notes.IgnoreQueryFilters().Where(n => n.IsSample || (n.ProjectId != null && projectIds.Contains(n.ProjectId))).ExecuteDeleteAsync();
        await db.RaidItems.IgnoreQueryFilters().Where(r => r.IsSample || projectIds.Contains(r.ProjectId)).ExecuteDeleteAsync();
        await db.Projects.IgnoreQueryFilters().Where(p => p.IsSample).ExecuteDeleteAsync();
    }

    /// <summary>Permanently deletes all projects, tasks, records, notes, RAID items and the trash. Settings and columns are kept.</summary>
    public async Task ClearAsync()
    {
        await db.Updates.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Tasks.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Milestones.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Notes.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.RaidItems.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Projects.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.DeleteBatches.ExecuteDeleteAsync();
    }

    private static async Task<int> AddNewAsync<T>(DbSet<T> set, List<T> rows, Func<T, string> id) where T : class
    {
        var ids = rows.Select(id).ToList();
        var existing = new HashSet<string>();
        foreach (var chunk in ids.Chunk(500))
        {
            // Include rows in the trash: their ids are taken too.
            var found = await set.IgnoreQueryFilters().AsNoTracking().Where(e => chunk.Contains(EF.Property<string>(e, "Id"))).Select(e => EF.Property<string>(e, "Id")).ToListAsync();
            existing.UnionWith(found);
        }
        var added = rows.Where(r => existing.Add(id(r))).ToList();
        set.AddRange(added);
        return added.Count;
    }

    private static ImportData ReadCurrent(JsonElement root)
    {
        var s = root.Deserialize<Snapshot>(Json) ?? throw new AppException("The file is empty.");
        // "pm-assistant" is the format name from before the app was renamed to PMate.
        if (s.Format is not (Snapshot.FormatName or "pm-assistant")) throw new AppException("This file is not a PMate backup.");
        return new ImportData(s.Settings, s.Statuses ?? [], s.Projects ?? [], s.Milestones ?? [], s.Tasks ?? [], s.Updates ?? [], s.Notes, s.RaidItems);
    }
}

public record ImportData(AppSettings? Settings, List<BoardStatus> Statuses, List<Project> Projects,
    List<Milestone> Milestones, List<TaskItem> Tasks, List<TaskUpdate> Updates,
    List<Note>? Notes = null, List<RaidItem>? RaidItems = null);

/// <summary>Reads backups from the first version: a single HTML file that stored data in the browser.</summary>
internal static class LegacyFormat
{
    public static ImportData Read(JsonElement root)
    {
        AppSettings? settings = null;
        var statuses = new List<BoardStatus>();
        if (root.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            var hours = Num(s, "hoursPerDay");
            settings = new AppSettings(Str(s, "me") ?? "", hours > 0 ? hours : 8, null);
            if (s.TryGetProperty("statuses", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var x in list.EnumerateArray())
                    statuses.Add(new BoardStatus { Id = Str(x, "id") ?? Ids.New(), Name = Str(x, "name") ?? "Column", Color = Str(x, "color") ?? "gray", IsDone = Bool(x, "done"), SortOrder = i++ });
            }
        }

        return new ImportData(settings, statuses,
            Arr(root, "projects").Select(x => new Project
            {
                Id = Str(x, "id") ?? Ids.New(), Name = Str(x, "name") ?? "Untitled", Code = Str(x, "code"), Client = Str(x, "client"),
                Status = Str(x, "status") ?? ProjectStatuses.Active, Start = Date(x, "start"), End = Date(x, "end"),
                Description = Str(x, "desc"), IsSample = Bool(x, "sample"), CreatedAt = Time(x, "createdAt") ?? DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            }).ToList(),
            Arr(root, "milestones").Select(x => new Milestone
            {
                Id = Str(x, "id") ?? Ids.New(), ProjectId = Str(x, "projectId") ?? "", Name = Str(x, "name") ?? "Untitled",
                PlanDate = Date(x, "planDate"), ActualDate = Date(x, "actualDate"), Status = Str(x, "status") ?? MilestoneStatuses.Planned, Note = Str(x, "note"),
            }).ToList(),
            Arr(root, "tasks").Select(x => new TaskItem
            {
                Id = Str(x, "id") ?? Ids.New(), ProjectId = Str(x, "projectId"), MilestoneId = Str(x, "milestoneId"), Title = Str(x, "title") ?? "Untitled",
                Owner = Str(x, "owner"), Due = Date(x, "due"), Status = Str(x, "status") ?? "", Priority = Str(x, "priority") ?? Priorities.Medium,
                Description = Str(x, "desc"), IsSample = Bool(x, "sample"), CreatedAt = Time(x, "createdAt") ?? DateTime.UtcNow,
                UpdatedAt = Time(x, "updatedAt") ?? DateTime.UtcNow, DoneAt = Time(x, "doneAt"),
            }).ToList(),
            Arr(root, "logs").Select(x => new TaskUpdate
            {
                Id = Str(x, "id") ?? Ids.New(), TaskId = Str(x, "taskId"), ProjectId = Str(x, "projectId"), Date = Date(x, "date") ?? Clock.Today,
                Type = Str(x, "type") ?? UpdateTypes.Note, Text = Str(x, "text") ?? "", Hours = Num(x, "hours"), Who = Str(x, "who"),
                CommitDue = Date(x, "commitDue"), CommitDone = Bool(x, "commitDone"), FromStatus = Str(x, "from"), ToStatus = Str(x, "to"),
                FromName = Str(x, "fromName"), ToName = Str(x, "toName"), IsSample = Bool(x, "sample"), CreatedAt = Time(x, "createdAt") ?? DateTime.UtcNow,
            }).ToList());
    }

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static DateOnly? Date(JsonElement e, string name) =>
        DateOnly.TryParseExact(Str(e, name), "yyyy-MM-dd", out var d) ? d : null;

    /// <summary>The old format stored times as milliseconds since 1970. Zero means "not set".</summary>
    private static DateTime? Time(JsonElement e, string name)
    {
        var ms = Num(e, name);
        return ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : null;
    }
}
