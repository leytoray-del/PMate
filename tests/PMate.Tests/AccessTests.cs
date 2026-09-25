using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PMate.Auth;
using PMate.Data;
using PMate.Domain;
using PMate.Services;

namespace PMate.Tests;

/// <summary>Who sees what: project teams, private items, and admins.</summary>
public sealed class AccessTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private DbContextOptions<AppDbContext> _options = null!;
    private AppDbContext _setup = null!;

    private readonly TestUser _admin = new("Admin", "u-admin", isAdmin: true);
    private readonly TestUser _ann = new("Ann", "u-ann");
    private readonly TestUser _ben = new("Ben", "u-ben");

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _setup = new AppDbContext(_options);
        await _setup.Database.MigrateAsync();
        await new StatusService(_setup).EnsureDefaultsAsync();
        foreach (var u in new[] { _admin, _ann, _ben })
            _setup.Users.Add(new User { Id = u.Id!, UserName = u.Name!.ToLowerInvariant(), NormalizedUserName = u.Name.ToLowerInvariant(), DisplayName = u.Name, Role = u.IsAdmin ? Roles.Admin : Roles.Member, PasswordHash = "x" });
        await _setup.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _setup.DisposeAsync();
        await _conn.DisposeAsync();
    }

    /// <summary>A database context that sees data as this user does.</summary>
    private AppDbContext As(TestUser u) => new(_options, u);
    private static TaskService Tasks(AppDbContext db, TestUser u) => new(db, u, new TrashService(db, u));
    private static ProjectService Projects(AppDbContext db, TestUser u) => new(db, u, new TrashService(db, u));

    private async Task<Project> ProjectFor(TestUser creator, string name)
    {
        await using var db = As(creator);
        return await Projects(db, creator).SaveAsync(null, new ProjectInput(name, null, null, null, null, null, null));
    }

    [Fact]
    public async Task Member_sees_only_their_projects_and_everything_inside()
    {
        var crm = await ProjectFor(_admin, "CRM");
        var hr = await ProjectFor(_admin, "HR");
        await using (var db = As(_admin))
        {
            await Projects(db, _admin).SetMembersAsync(crm.Id, [_ann.Id!]);
            var t = await Tasks(db, _admin).CreateAsync(new TaskInput("CRM task", crm.Id, null, "Ben", null, null, null, null));
            await Tasks(db, _admin).AddUpdateAsync(new UpdateInput(t.Id, null, null, UpdateTypes.Work, "Did it", 1, null, null));
            await Tasks(db, _admin).CreateAsync(new TaskInput("HR task", hr.Id, null, "Ann", null, null, null, null));
            await new NoteService(db, _admin, new TrashService(db)).SaveAsync(null, new NoteInput(hr.Id, "HR minutes", "secret", null, null, false));
            await new RaidService(db, _admin, new TrashService(db)).SaveAsync(null, new RaidInput(crm.Id, RaidTypes.Risk, "CRM risk", null, null, null, null, null, null, null));
        }

        await using var ann = As(_ann);
        Assert.Equal(["CRM"], await ann.Projects.Select(p => p.Name).ToListAsync());
        // Even a task owned by Ann is hidden when it is in a project she is not on.
        Assert.Equal(["CRM task"], await ann.Tasks.Select(t => t.Title).ToListAsync());
        Assert.Equal(1, await ann.Updates.CountAsync());
        Assert.Equal(1, await ann.RaidItems.CountAsync());
        Assert.Equal(0, await ann.Notes.CountAsync());

        await using var ben = As(_ben);
        Assert.Equal(0, await ben.Projects.CountAsync());
        Assert.Equal(0, await ben.Tasks.CountAsync());

        await using var admin = As(_admin);
        Assert.Equal(2, await admin.Projects.CountAsync());
        Assert.Equal(2, await admin.Tasks.CountAsync());
    }

    [Fact]
    public async Task Tasks_without_a_project_are_seen_by_creator_and_owner_only()
    {
        await using (var db = As(_ann))
        {
            await Tasks(db, _ann).CreateAsync(new TaskInput("Ann private", null, null, null, null, null, null, null));
            await Tasks(db, _ann).CreateAsync(new TaskInput("For Ben", null, null, "Ben", null, null, null, null));
        }
        await using var ben = As(_ben);
        Assert.Equal(["For Ben"], await ben.Tasks.Select(t => t.Title).ToListAsync());
        await using var ann = As(_ann);
        Assert.Equal(2, await ann.Tasks.CountAsync());
    }

    [Fact]
    public async Task Notes_and_records_without_a_project_are_private()
    {
        await using (var db = As(_ann))
        {
            await new NoteService(db, _ann, new TrashService(db)).SaveAsync(null, new NoteInput(null, "Mine", "private", null, null, false));
            await Tasks(db, _ann).AddUpdateAsync(new UpdateInput(null, null, null, UpdateTypes.Work, "Admin work", 2, null, null));
        }
        await using var ben = As(_ben);
        Assert.Equal(0, await ben.Notes.CountAsync());
        Assert.Equal(0, await ben.Updates.CountAsync());
        await using var ann = As(_ann);
        Assert.Equal(1, await ann.Notes.CountAsync());
        Assert.Equal("u-ann", (await ann.Updates.SingleAsync()).CreatedById);
    }

    [Fact]
    public async Task Member_cannot_write_into_a_project_they_are_not_on()
    {
        var hr = await ProjectFor(_admin, "HR");
        string hrTask;
        await using (var db = As(_admin)) hrTask = (await Tasks(db, _admin).CreateAsync(new TaskInput("HR task", hr.Id, null, null, null, null, null, null))).Id;

        await using var ben = As(_ben);
        var create = await Assert.ThrowsAsync<AppException>(() => Tasks(ben, _ben).CreateAsync(new TaskInput("Sneaky", hr.Id, null, null, null, null, null, null)));
        Assert.Equal("Project not found.", create.Message);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Tasks(ben, _ben).UpdateAsync(hrTask, new TaskInput("Changed", hr.Id, null, null, null, null, null, null)))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Tasks(ben, _ben).DeleteAsync(hrTask))).StatusCode);
        await Assert.ThrowsAsync<AppException>(() => Tasks(ben, _ben).AddUpdateAsync(new UpdateInput(hrTask, null, null, UpdateTypes.Note, "x", 0, null, null)));
    }

    [Fact]
    public async Task Creating_a_project_puts_the_creator_on_the_team()
    {
        var p = await ProjectFor(_ben, "Ben's project");
        await using var ben = As(_ben);
        Assert.Equal(["Ben's project"], await ben.Projects.Select(x => x.Name).ToListAsync());
        Assert.Equal(["u-ben"], (await Projects(ben, _ben).MembersAsync(p.Id)).Select(m => m.UserId));
    }

    [Fact]
    public async Task Setting_the_team_replaces_it_and_rejects_unknown_users()
    {
        var p = await ProjectFor(_admin, "CRM");
        await using var db = As(_admin);
        var svc = Projects(db, _admin);
        await svc.SetMembersAsync(p.Id, [_ann.Id!, _ben.Id!]);
        var team = await svc.SetMembersAsync(p.Id, [_ben.Id!]);
        Assert.Equal(["u-ben"], team.Select(m => m.UserId));
        await Assert.ThrowsAsync<AppException>(() => svc.SetMembersAsync(p.Id, ["nobody"]));
    }

    [Fact]
    public async Task Members_see_only_their_own_deletes_in_the_trash()
    {
        var p = await ProjectFor(_admin, "CRM");
        await using (var db = As(_admin)) await Projects(db, _admin).SetMembersAsync(p.Id, [_ann.Id!, _ben.Id!]);
        await using (var db = As(_ann))
        {
            var t = await Tasks(db, _ann).CreateAsync(new TaskInput("Ann's", p.Id, null, null, null, null, null, null));
            await Tasks(db, _ann).DeleteAsync(t.Id);
        }
        await using var ben = As(_ben);
        Assert.Empty(await new TrashService(ben, _ben).ListAsync());
        await using var ann = As(_ann);
        Assert.Single(await new TrashService(ann, _ann).ListAsync());
    }

    [Fact]
    public async Task Snapshot_for_a_member_lists_only_people_on_their_projects()
    {
        var p = await ProjectFor(_admin, "CRM");
        await using (var db = As(_admin)) await Projects(db, _admin).SetMembersAsync(p.Id, [_ann.Id!]);

        await using var ann = As(_ann);
        var snap = await new DataService(ann, new SettingsService(ann), new StatusService(ann), _ann).GetSnapshotAsync();
        Assert.Equal(["Ann"], snap.People!.Select(x => x.DisplayName));

        await using var admin = As(_admin);
        var all = await new DataService(admin, new SettingsService(admin), new StatusService(admin), _admin).GetSnapshotAsync();
        Assert.Equal(3, all.People!.Count);
    }

    [Fact]
    public async Task Old_records_without_a_creator_go_to_the_first_admin()
    {
        _setup.Tasks.Add(new TaskItem { Title = "Old", Status = "todo" });
        _setup.Updates.Add(new TaskUpdate { Text = "Old log", Hours = 1, Date = new DateOnly(2026, 9, 1) });
        await _setup.SaveChangesAsync();

        await Ownership.AssignUnownedAsync(_setup);

        _setup.ChangeTracker.Clear();
        Assert.All(await _setup.Tasks.ToListAsync(), t => Assert.Equal("u-admin", t.CreatedById));
        Assert.All(await _setup.Updates.ToListAsync(), u => Assert.Equal("u-admin", u.CreatedById));
    }

    [Fact]
    public async Task Renaming_a_user_moves_their_tasks_to_the_new_name()
    {
        await using (var db = As(_admin)) await Tasks(db, _admin).CreateAsync(new TaskInput("Ann's task", null, null, "Ann", null, null, null, null));
        var users = new UserService(_setup, TimeProvider.System);
        await users.UpdateProfileAsync(_ann.Id!, "Ann Lee");
        _setup.ChangeTracker.Clear();
        Assert.Equal("Ann Lee", (await _setup.Tasks.SingleAsync()).Owner);
        await Assert.ThrowsAsync<AppException>(() => users.UpdateProfileAsync(_ben.Id!, "Ann Lee"));
    }
}

/// <summary>Project teams over HTTP.</summary>
public sealed class TeamApiTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("pmate-team-test-").FullName;
    private readonly WebApplicationFactory<Program> _app;

    public TeamApiTests()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Storage:DataDir", _dataDir)
            .UseSetting("Tray:Enabled", "false")
            .UseSetting("Updates:Enabled", "false"));
    }

    public void Dispose()
    {
        _app.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }

    private HttpClient Client()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-PM-Client", "1");
        return c;
    }

    private static async Task<T> Json<T>(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Admin_assigns_a_member_who_then_sees_the_project_but_cannot_change_the_team()
    {
        var admin = Client();
        await Json<UserDto>(admin.PostAsJsonAsync("/api/auth/setup", new { userName = "admin", displayName = "Admin", password = "admin-pass" }));
        var ann = await Json<UserDto>(admin.PostAsJsonAsync("/api/users", new { userName = "ann", displayName = "Ann", role = "member", password = "temp-pass-1" }));
        var crm = await Json<Project>(admin.PostAsJsonAsync("/api/projects", new { name = "CRM" }));
        await Json<Project>(admin.PostAsJsonAsync("/api/projects", new { name = "HR" }));

        var annClient = Client();
        await Json<UserDto>(annClient.PostAsJsonAsync("/api/auth/login", new { userName = "ann", password = "temp-pass-1" }));
        await Json<UserDto>(annClient.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "temp-pass-1", newPassword = "anns-own-pass" }));
        Assert.Empty((await Json<Snapshot>(annClient.GetAsync("/api/snapshot"))).Projects);

        await Json<List<ProjectMember>>(admin.PutAsJsonAsync($"/api/projects/{crm.Id}/members", new[] { ann.Id }));

        var snap = await Json<Snapshot>(annClient.GetAsync("/api/snapshot"));
        Assert.Equal(["CRM"], snap.Projects.Select(p => p.Name));
        Assert.Equal(HttpStatusCode.Forbidden, (await annClient.PutAsJsonAsync($"/api/projects/{crm.Id}/members", Array.Empty<string>())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await annClient.DeleteAsync($"/api/projects/{crm.Id}")).StatusCode);
    }
}
