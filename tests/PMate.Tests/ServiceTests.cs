using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;
using PMate.Services;

namespace PMate.Tests;

public sealed class ServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await new StatusService(_db).EnsureDefaultsAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private TaskService Tasks => new(_db, new TestUser(), new TrashService(_db));
    private ProjectService Projects => new(_db, new TestUser(), new TrashService(_db));
    private static TaskInput Input(string title, string? status = null, string? projectId = null) =>
        new(title, projectId, null, "Me", null, status, "high", null);

    [Fact]
    public async Task Moving_a_task_to_done_writes_a_status_change_and_sets_DoneAt()
    {
        var t = await Tasks.CreateAsync(Input("Write report"));
        Assert.Equal("todo", t.Status);

        var result = await Tasks.UpdateAsync(t.Id, Input("Write report", "done"));

        Assert.NotNull(result.Task.DoneAt);
        Assert.NotNull(result.StatusChange);
        Assert.Equal(("todo", "done", "To Do", "Done"),
            (result.StatusChange!.FromStatus, result.StatusChange.ToStatus, result.StatusChange.FromName, result.StatusChange.ToName));
    }

    [Fact]
    public async Task Saving_without_status_change_writes_no_timeline_entry()
    {
        var t = await Tasks.CreateAsync(Input("A"));
        var result = await Tasks.UpdateAsync(t.Id, Input("A renamed", "todo"));
        Assert.Null(result.StatusChange);
        Assert.Equal(0, await _db.Updates.CountAsync());
    }

    [Fact]
    public async Task Removing_a_column_moves_its_tasks_to_the_first_open_column()
    {
        var t = await Tasks.CreateAsync(Input("Waiting task", "waiting"));
        var cols = new List<StatusInput>
        {
            new("todo", "Backlog", "gray", false),
            new("doing", "Doing", "blue", false),
            new(null, "Blocked", "red", false),
            new("done", "Done", "green", true),
        };

        var saved = await new StatusService(_db).ReplaceAsync(cols);

        Assert.Equal(["Backlog", "Doing", "Blocked", "Done"], saved.Select(s => s.Name));
        _db.ChangeTracker.Clear();
        Assert.Equal("todo", (await _db.Tasks.FindAsync(t.Id))!.Status);
    }

    [Fact]
    public async Task Columns_must_include_a_done_column()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new StatusService(_db).ReplaceAsync([new StatusInput("todo", "To Do", "gray", false)]));
        Assert.Contains("Done", ex.Message);
    }

    [Fact]
    public async Task Deleting_a_project_deletes_its_tasks_milestones_and_records()
    {
        var projects = Projects;
        var p = await projects.SaveAsync(null, new ProjectInput("CRM", "PRJ-1", null, null, null, null, null));
        await projects.SaveMilestoneAsync(null, new MilestoneInput(p.Id, "UAT", null, null, null, null));
        var t = await Tasks.CreateAsync(Input("Task", projectId: p.Id));
        await Tasks.AddUpdateAsync(new UpdateInput(t.Id, null, null, UpdateTypes.Work, "Did work", 2, null, null));
        var other = await Tasks.CreateAsync(Input("Keep me"));

        await projects.DeleteAsync(p.Id);

        Assert.Equal(0, await _db.Projects.CountAsync());
        Assert.Equal(0, await _db.Milestones.CountAsync());
        Assert.Equal(0, await _db.Updates.CountAsync());
        Assert.Equal([other.Id], await _db.Tasks.Select(x => x.Id).ToListAsync());
    }

    [Fact]
    public async Task Work_log_takes_the_project_from_its_task()
    {
        var p = await Projects.SaveAsync(null, new ProjectInput("CRM", null, null, null, null, null, null));
        var t = await Tasks.CreateAsync(Input("Task", projectId: p.Id));
        var u = await Tasks.AddUpdateAsync(new UpdateInput(t.Id, null, new DateOnly(2026, 9, 1), UpdateTypes.Work, " Meeting ", 1.5, null, null));
        Assert.Equal(p.Id, u.ProjectId);
        Assert.Equal("Meeting", u.Text);
    }

    [Fact]
    public async Task Update_without_text_is_rejected()
    {
        await Assert.ThrowsAsync<AppException>(() => Tasks.AddUpdateAsync(new UpdateInput(null, null, null, UpdateTypes.Work, "  ", 1, null, null)));
    }

    [Fact]
    public async Task Imports_backup_from_the_single_file_version()
    {
        const string legacy = """
        {
          "version": 2,
          "settings": { "me": "Lee", "hoursPerDay": 7.5,
            "statuses": [ { "id": "todo", "name": "To Do", "color": "gray", "done": false },
                          { "id": "fin", "name": "Finished", "color": "green", "done": true } ] },
          "projects": [ { "id": "p1", "name": "CRM", "code": "PRJ-CRM", "status": "active", "start": "2026-09-01", "end": "", "desc": "Scope", "createdAt": 1790000000000 } ],
          "milestones": [],
          "tasks": [ { "id": "t1", "projectId": "p1", "milestoneId": "", "title": "Do it", "owner": "Ben", "due": "2026-09-30",
                       "status": "fin", "priority": "high", "desc": "", "createdAt": 1790000000000, "doneAt": 1790000500000 } ],
          "logs": [ { "id": "l1", "taskId": "t1", "projectId": "p1", "date": "2026-09-02", "type": "commit", "text": "Send plan",
                      "hours": 0, "who": "Ben", "commitDue": "2026-09-05", "commitDone": false, "createdAt": 1790000000000 } ]
        }
        """;
        var data = new DataService(_db, new SettingsService(_db), new StatusService(_db));

        var result = await data.ImportAsync(JsonDocument.Parse(legacy).RootElement, replace: true);

        Assert.Equal(new ImportResult(1, 0, 1, 1, 0, 0), result);
        var snap = await data.GetSnapshotAsync();
        Assert.Equal("Lee", snap.Settings.Me);
        Assert.Equal(7.5, snap.Settings.HoursPerDay);
        Assert.Equal(["To Do", "Finished"], snap.Statuses.Select(s => s.Name));
        Assert.Equal("Scope", snap.Projects[0].Description);
        Assert.Null(snap.Projects[0].End);
        Assert.Equal("fin", snap.Tasks[0].Status);
        Assert.Null(snap.Tasks[0].MilestoneId);
        Assert.Equal(new DateOnly(2026, 9, 5), snap.Updates[0].CommitDue);
    }

    [Fact]
    public async Task Merge_import_skips_existing_rows_and_maps_unknown_columns()
    {
        var existing = await Tasks.CreateAsync(Input("Mine"));
        var data = new DataService(_db, new SettingsService(_db), new StatusService(_db));
        var json = JsonSerializer.SerializeToElement(new
        {
            format = "pmate", version = 3, exportedAt = DateTime.UtcNow,
            statuses = Array.Empty<object>(), projects = Array.Empty<object>(), milestones = Array.Empty<object>(), updates = Array.Empty<object>(),
            tasks = new object[]
            {
                new { id = existing.Id, title = "Duplicate", status = "todo", priority = "low" },
                new { id = "new1", title = "Imported", status = "no-such-column", priority = "low" },
            },
        });

        var result = await data.ImportAsync(json, replace: false);

        Assert.Equal(1, result.Tasks);
        _db.ChangeTracker.Clear();
        Assert.Equal("Mine", (await _db.Tasks.FindAsync(existing.Id))!.Title);
        Assert.Equal("todo", (await _db.Tasks.FindAsync("new1"))!.Status);
    }

    [Fact]
    public async Task New_database_has_the_default_tags()
    {
        var tags = await new TagService(_db).ListAsync();
        Assert.Equal(["Meeting", "On-site", "Doc", "Review", "Confirm", "Follow-up", "Approve", "Report", "Fix", "Test"], tags.Select(t => t.Name));
    }

    [Fact]
    public async Task A_task_keeps_a_known_tag_and_drops_an_unknown_one()
    {
        var t = await Tasks.CreateAsync(Input("Kick-off") with { TagId = "meeting" });
        Assert.Equal("meeting", t.TagId);

        var saved = await Tasks.UpdateAsync(t.Id, Input("Kick-off") with { TagId = "no-such-tag" });
        Assert.Null(saved.Task.TagId);
    }

    [Fact]
    public async Task Removing_a_tag_clears_it_from_tasks_also_in_the_trash()
    {
        var live = await Tasks.CreateAsync(Input("Live") with { TagId = "doc" });
        var deleted = await Tasks.CreateAsync(Input("Deleted") with { TagId = "doc" });
        await Tasks.DeleteAsync(deleted.Id);
        var keep = (await new TagService(_db).ListAsync()).Where(t => t.Id != "doc").Select(t => new TagInput(t.Id, t.Name, t.Color)).ToList();

        var tags = await new TagService(_db).ReplaceAsync([.. keep, new TagInput(null, "Workshop", "no-such-color")]);

        Assert.DoesNotContain(tags, t => t.Id == "doc");
        Assert.Equal("gray", tags.Single(t => t.Name == "Workshop").Color);
        _db.ChangeTracker.Clear();
        Assert.Null((await _db.Tasks.FindAsync(live.Id))!.TagId);
        Assert.Null((await _db.Tasks.IgnoreQueryFilters().SingleAsync(x => x.Id == deleted.Id)).TagId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A name that is much too long")]
    public async Task Tag_names_must_be_set_and_short(string name)
    {
        await Assert.ThrowsAsync<AppException>(() => new TagService(_db).ReplaceAsync([new TagInput(null, name, "blue")]));
    }

    [Fact]
    public async Task Tag_names_must_be_unique()
    {
        await Assert.ThrowsAsync<AppException>(() => new TagService(_db).ReplaceAsync([new TagInput(null, "Doc", "blue"), new TagInput(null, "doc", "red")]));
    }

    [Fact]
    public async Task Project_color_must_be_a_known_color()
    {
        var p = await Projects.SaveAsync(null, new ProjectInput("CRM", null, null, null, null, null, null, "teal"));
        Assert.Equal("teal", p.Color);
        p = await Projects.SaveAsync(p.Id, new ProjectInput("CRM", null, null, null, null, null, null, "neon"));
        Assert.Null(p.Color);
    }

    [Fact]
    public async Task Import_drops_unknown_tags_and_a_replace_restores_the_backup_tags()
    {
        var data = new DataService(_db, new SettingsService(_db), new StatusService(_db));
        var json = JsonSerializer.SerializeToElement(new
        {
            format = "pmate", version = 6, exportedAt = DateTime.UtcNow,
            statuses = new object[] { new { id = "todo", name = "To Do", color = "gray", isDone = false }, new { id = "done", name = "Done", color = "green", isDone = true } },
            tags = new object[] { new { id = "call", name = "Call", color = "blue" } },
            projects = Array.Empty<object>(), milestones = Array.Empty<object>(), updates = Array.Empty<object>(),
            tasks = new object[]
            {
                new { id = "t1", title = "Phone vendor", status = "todo", priority = "low", tagId = "call" },
                new { id = "t2", title = "Old tag", status = "todo", priority = "low", tagId = "gone" },
            },
        });

        await data.ImportAsync(json, replace: true);

        _db.ChangeTracker.Clear();
        Assert.Equal(["Call"], (await new TagService(_db).ListAsync()).Select(t => t.Name));
        Assert.Equal("call", (await _db.Tasks.FindAsync("t1"))!.TagId);
        Assert.Null((await _db.Tasks.FindAsync("t2"))!.TagId);
    }
}
