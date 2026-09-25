using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PMate.Data;
using PMate.Domain;
using PMate.Infrastructure;
using PMate.Services;

namespace PMate.Tests;

/// <summary>Tests for undo, record editing, notes, RAID and reminders.</summary>
public sealed class FeatureTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await new StatusService(_db).EnsureDefaultsAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
    private TrashService Trash => new(_db);
    private TaskService Tasks => new(_db, new TestUser(), Trash);
    private ProjectService Projects => new(_db, new TestUser(), Trash);
    private NoteService Notes => new(_db, new TestUser(), Trash);
    private RaidService Raid => new(_db, new TestUser(), Trash);

    private Task<Project> NewProject(string name = "CRM") => Projects.SaveAsync(null, new ProjectInput(name, null, null, null, null, null, null));
    private Task<TaskItem> NewTask(string title, string? projectId = null, DateOnly? due = null) =>
        Tasks.CreateAsync(new TaskInput(title, projectId, null, null, due, null, null, null));
    private Task<TaskUpdate> Log(string? taskId, string text, double hours, DateOnly? date = null, string type = UpdateTypes.Work) =>
        Tasks.AddUpdateAsync(new UpdateInput(taskId, null, date, type, text, hours, null, null));

    /* ---------- undo ---------- */

    [Fact]
    public async Task Deleted_task_is_hidden_and_restore_brings_back_task_and_timeline()
    {
        var t = await NewTask("Task");
        await Log(t.Id, "Worked", 1);

        var deleted = await Tasks.DeleteAsync(t.Id);
        Assert.Equal(0, await _db.Tasks.CountAsync());
        Assert.Equal(0, await _db.Updates.CountAsync());
        Assert.Single(await Trash.ListAsync());

        await Trash.RestoreAsync(deleted.UndoId);
        Assert.Equal(1, await _db.Tasks.CountAsync());
        Assert.Equal(1, await _db.Updates.CountAsync());
        Assert.Empty(await Trash.ListAsync());
    }

    [Fact]
    public async Task Deleting_a_project_moves_notes_and_raid_items_to_the_same_batch()
    {
        var p = await NewProject();
        await Notes.SaveAsync(null, new NoteInput(p.Id, "Kick-off", "Minutes", null, null, false));
        await Raid.SaveAsync(null, new RaidInput(p.Id, RaidTypes.Risk, "Vendor late", null, null, null, null, null, null, null));

        var deleted = await Projects.DeleteAsync(p.Id);
        Assert.Equal(0, await _db.Notes.CountAsync());
        Assert.Equal(0, await _db.RaidItems.CountAsync());

        await Trash.RestoreAsync(deleted.UndoId);
        Assert.Equal(1, await _db.Notes.CountAsync());
        Assert.Equal(1, await _db.RaidItems.CountAsync());
    }

    [Fact]
    public async Task Earlier_deleted_records_stay_deleted_when_the_task_is_restored()
    {
        var t = await NewTask("Task");
        var u = await Log(t.Id, "Wrong entry", 1);
        await Tasks.DeleteUpdateAsync(u.Id);
        var deleted = await Tasks.DeleteAsync(t.Id);

        await Trash.RestoreAsync(deleted.UndoId);

        Assert.Equal(0, await _db.Updates.CountAsync());
    }

    [Fact]
    public async Task Purge_removes_batches_older_than_30_days_for_good()
    {
        var t = await NewTask("Old");
        await Tasks.DeleteAsync(t.Id);

        Assert.Equal(0, await Trash.PurgeAsync(DateTime.UtcNow.AddDays(29)));
        Assert.Equal(1, await Trash.PurgeAsync(DateTime.UtcNow.AddDays(31)));
        Assert.Equal(0, await _db.Tasks.IgnoreQueryFilters().CountAsync());
        Assert.Empty(await Trash.ListAsync());
    }

    [Fact]
    public async Task Times_read_from_the_database_are_marked_as_utc()
    {
        var t = await NewTask("Task");
        await using var fresh = NewContext();
        var loaded = await fresh.Tasks.SingleAsync(x => x.Id == t.Id);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
    }

    /* ---------- rename from "PM Assistant" ---------- */

    [Fact]
    public async Task Backups_made_before_the_rename_still_import()
    {
        var json = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            format = "pm-assistant", version = 3, exportedAt = DateTime.UtcNow,
            statuses = Array.Empty<object>(), projects = new[] { new { id = "p1", name = "Old project", status = "active" } },
            milestones = Array.Empty<object>(), tasks = Array.Empty<object>(), updates = Array.Empty<object>(),
        });
        var result = await new DataService(_db, new SettingsService(_db), new StatusService(_db)).ImportAsync(json, replace: false);
        Assert.Equal(1, result.Projects);
    }

    [Fact]
    public void Old_database_and_backups_are_renamed_on_start()
    {
        var dir = Directory.CreateTempSubdirectory("pmate-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "pm-assistant.db"), "db");
            Directory.CreateDirectory(Path.Combine(dir, "backups"));
            File.WriteAllText(Path.Combine(dir, "backups", "pm-assistant-2026-09-01.db"), "backup");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = dir }).Build();
            var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = dir };

            var paths = AppPaths.Create(config, env);

            Assert.Equal("db", File.ReadAllText(paths.DatabaseFile));
            Assert.False(File.Exists(Path.Combine(dir, "pm-assistant.db")));
            Assert.True(File.Exists(Path.Combine(dir, "backups", "pmate-2026-09-01.db")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Old_data_folder_moves_even_when_the_launcher_already_created_the_new_one()
    {
        var root = Directory.CreateTempSubdirectory("pmate-test-").FullName;
        try
        {
            var oldDir = Path.Combine(root, "PmAssistant");
            var newDir = Path.Combine(root, "PMate");
            Directory.CreateDirectory(Path.Combine(oldDir, "logs"));
            File.WriteAllText(Path.Combine(oldDir, "pm-assistant.db"), "db");
            File.WriteAllText(Path.Combine(oldDir, "logs", "launcher.log"), "old log");
            File.WriteAllText(Path.Combine(oldDir, "logs", "app.log"), "old app log");
            Directory.CreateDirectory(Path.Combine(newDir, "logs"));
            File.WriteAllText(Path.Combine(newDir, "logs", "launcher.log"), "new log");

            AppPaths.MoveDataFromOldName(oldDir, newDir);

            Assert.Equal("db", File.ReadAllText(Path.Combine(newDir, "pmate.db")));
            Assert.Equal("new log", File.ReadAllText(Path.Combine(newDir, "logs", "launcher.log")));
            Assert.Equal("old app log", File.ReadAllText(Path.Combine(newDir, "logs", "app.log")));
            Assert.False(File.Exists(Path.Combine(oldDir, "pm-assistant.db")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /* ---------- editing records ---------- */

    [Fact]
    public async Task Editing_a_record_changes_type_text_hours_and_date()
    {
        var t = await NewTask("Task");
        var u = await Log(t.Id, "Draft", 1, new DateOnly(2026, 9, 1));

        var edited = await Tasks.EditUpdateAsync(u.Id, new UpdateInput(null, null, new DateOnly(2026, 9, 2), UpdateTypes.Commitment, "Send draft", 0.5, "Ben", new DateOnly(2026, 9, 5)));

        Assert.Equal((UpdateTypes.Commitment, "Send draft", 0.5, new DateOnly(2026, 9, 2), "Ben", t.Id),
            (edited.Type, edited.Text, edited.Hours, edited.Date, edited.Who, edited.TaskId));
    }

    [Fact]
    public async Task Status_changes_cannot_be_edited()
    {
        var t = await NewTask("Task");
        var change = (await Tasks.UpdateAsync(t.Id, new TaskInput("Task", null, null, null, null, "done", null, null))).StatusChange!;
        await Assert.ThrowsAsync<AppException>(() => Tasks.EditUpdateAsync(change.Id, new UpdateInput(null, null, null, UpdateTypes.Note, "x", 0, null, null)));
    }

    /* ---------- notes ---------- */

    [Fact]
    public async Task Note_search_finds_english_and_chinese_text_and_skips_deleted_notes()
    {
        var p = await NewProject();
        var a = await Notes.SaveAsync(null, new NoteInput(p.Id, "Steering committee", "Vendor agreed to deliver the migration plan", "steering", null, false));
        var b = await Notes.SaveAsync(null, new NoteInput(null, "周会", "讨论了数据迁移方案，下周确认上线日期", null, null, false));
        var c = await Notes.SaveAsync(null, new NoteInput(null, "Old", "migration plan draft", null, null, false));
        await Notes.DeleteAsync(c.Id);

        Assert.Equal([a.Id], (await Notes.SearchAsync("migration plan", null)).Select(h => h.Id));
        Assert.Equal([b.Id], (await Notes.SearchAsync("迁移方案", null)).Select(h => h.Id));
        Assert.Equal([b.Id], (await Notes.SearchAsync("上线", null)).Select(h => h.Id)); // 2 characters: LIKE fallback
        Assert.Empty(await Notes.SearchAsync("migration", "other-project"));
        Assert.Contains('\u0001', (await Notes.SearchAsync("agreed", null))[0].Snippet);
    }

    [Fact]
    public async Task Note_search_sees_edits()
    {
        var n = await Notes.SaveAsync(null, new NoteInput(null, "Minutes", "first version", null, null, false));
        await Notes.SaveAsync(n.Id, new NoteInput(null, "Minutes", "second version with budget", null, null, false));

        Assert.Empty(await Notes.SearchAsync("first", null));
        Assert.Single(await Notes.SearchAsync("budget", null));
    }

    [Fact]
    public async Task Note_tags_are_cleaned_up()
    {
        var n = await Notes.SaveAsync(null, new NoteInput(null, "", "body", " uat, vendor ,UAT，风险 ", null, false));
        Assert.Equal("uat, vendor, 风险", n.Tags);
    }

    [Fact]
    public async Task Block_editor_pages_are_searched_by_their_text_not_their_json()
    {
        const string page = """
            {"type":"doc","content":[
              {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Cut-over plan"}]},
              {"type":"panel","attrs":{"kind":"warning"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Freeze "},{"type":"text","marks":[{"type":"bold"}],"text":"deployments"}]}]},
              {"type":"codeBlock","attrs":{"language":"sql"},"content":[{"type":"text","text":"SELECT 1;\nSELECT 2;"}]}]}
            """;
        var n = await Notes.SaveAsync(null, new NoteInput(null, "", null, null, null, false, page));

        Assert.Equal("Cut-over plan\nFreeze deployments\nSELECT 1;\nSELECT 2;", n.Body);
        Assert.Equal("Cut-over plan", NoteService.DisplayTitle(n));
        Assert.Single(await Notes.SearchAsync("deployments", null));
        Assert.Empty(await Notes.SearchAsync("paragraph", null)); // JSON keys are not indexed
    }

    [Fact]
    public async Task Note_content_that_is_not_a_page_is_refused()
    {
        await Assert.ThrowsAsync<AppException>(() => Notes.SaveAsync(null, new NoteInput(null, "", null, null, null, false, "not json")));
        await Assert.ThrowsAsync<AppException>(() => Notes.SaveAsync(null, new NoteInput(null, "", null, null, null, false, """{"type":"table"}""")));
    }

    [Fact]
    public async Task A_save_based_on_an_old_version_is_refused()
    {
        var n = await Notes.SaveAsync(null, new NoteInput(null, "Minutes", "v1", null, null, false));
        var opened = n.Version;
        await Notes.SaveAsync(n.Id, new NoteInput(null, "Minutes", "Ann's change", null, null, false, Version: opened));

        var ex = await Assert.ThrowsAsync<AppException>(() => Notes.SaveAsync(n.Id, new NoteInput(null, "Minutes", "Ben's change", null, null, false, Version: opened)));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Ann's change", (await _db.Notes.FindAsync(n.Id))!.Body);
    }

    /* ---------- RAID ---------- */

    [Fact]
    public async Task Raid_items_are_numbered_per_project_and_type()
    {
        var p1 = await NewProject("A");
        var p2 = await NewProject("B");
        RaidInput In(string projectId, string type) => new(projectId, type, "Item", null, null, "high", "low", null, null, null);

        var r1 = await Raid.SaveAsync(null, In(p1.Id, RaidTypes.Risk));
        var r2 = await Raid.SaveAsync(null, In(p1.Id, RaidTypes.Risk));
        var i1 = await Raid.SaveAsync(null, In(p1.Id, RaidTypes.Issue));
        var other = await Raid.SaveAsync(null, In(p2.Id, RaidTypes.Risk));
        await Raid.DeleteAsync(r2.Id);
        var r3 = await Raid.SaveAsync(null, In(p1.Id, RaidTypes.Risk));

        Assert.Equal((1, 2, 1, 1, 3), (r1.Number, r2.Number, i1.Number, other.Number, r3.Number));
        Assert.Equal("low", r1.Likelihood);
        Assert.Null(i1.Likelihood);
    }

    [Fact]
    public async Task Closing_a_raid_item_records_the_time()
    {
        var p = await NewProject();
        var r = await Raid.SaveAsync(null, new RaidInput(p.Id, RaidTypes.Issue, "Server down", null, null, null, null, null, null, null));
        Assert.Null(r.ClosedAt);
        r = await Raid.SaveAsync(r.Id, new RaidInput(p.Id, RaidTypes.Issue, "Server down", null, null, null, null, RaidStatuses.Closed, "Restarted", null));
        Assert.NotNull(r.ClosedAt);
    }

    /* ---------- reminders ---------- */

    private static readonly DateOnly Wed = new(2026, 9, 23);

    [Fact]
    public async Task Morning_reminder_counts_due_overdue_and_commitments()
    {
        var t = await NewTask("Due today", due: Wed);
        await NewTask("Late", due: Wed.AddDays(-2));
        var done = await NewTask("Done late", due: Wed.AddDays(-2));
        await Tasks.UpdateAsync(done.Id, new TaskInput("Done late", null, null, null, Wed.AddDays(-2), "done", null, null));
        await Tasks.AddUpdateAsync(new UpdateInput(t.Id, null, null, UpdateTypes.Commitment, "Send plan", 0, "Ben", Wed));

        var m = await new ReminderBuilder(_db, new SettingsService(_db)).MorningAsync(Wed);

        Assert.Equal("1 task due today · 1 task overdue · 1 commitment due", m!.Text);
    }

    [Fact]
    public async Task Evening_reminder_only_when_hours_are_below_target()
    {
        var builder = new ReminderBuilder(_db, new SettingsService(_db));
        await Log(null, "Meeting", 3, Wed);
        Assert.Contains("3h", (await builder.EveningAsync(Wed))!.Text);
        await Log(null, "Report", 5, Wed);
        Assert.Null(await builder.EveningAsync(Wed));
    }

    [Fact]
    public async Task Reminders_are_sent_once_per_day_and_not_on_weekends()
    {
        await NewTask("Due", due: Wed);
        var notifier = new FakeNotifier();
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(_conn))
            .AddScoped<SettingsService>()
            .AddScoped<ReminderBuilder>()
            .BuildServiceProvider();
        var reminders = new ReminderService(services.GetRequiredService<IServiceScopeFactory>(), notifier, NullLogger<ReminderService>.Instance);

        await reminders.CheckAsync(new DateTime(2026, 9, 23, 8, 0, 0));   // before 09:00
        await reminders.CheckAsync(new DateTime(2026, 9, 23, 9, 30, 0));  // morning
        await reminders.CheckAsync(new DateTime(2026, 9, 23, 11, 0, 0));  // already sent
        await reminders.CheckAsync(new DateTime(2026, 9, 23, 17, 45, 0)); // evening: 0h logged
        await reminders.CheckAsync(new DateTime(2026, 9, 26, 9, 30, 0));  // Saturday

        Assert.Equal(["Your day in PMate", "Log your time"], notifier.Titles);
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<string> Titles { get; } = [];
        public bool IsAvailable => true;
        public void Notify(string title, string message) => Titles.Add(title);
    }
}
