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

/// <summary>Stand-in for the signed-in user in service tests.</summary>
public sealed class TestUser(string? name = "Tester") : ICurrentUser
{
    public string? Id => "test-user";
    public string? Name => name;
}

/// <summary>Account rules: setup, sign-in, lockout and admin actions.</summary>
public sealed class UserServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private readonly FakeTime _time = new();
    private UserService Users => new(_db, _time);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    [Fact]
    public void Password_hash_verifies_only_the_right_password()
    {
        var hash = PasswordHasher.Hash("correct horse", iterations: 1000);
        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("Correct horse", hash));
        Assert.DoesNotContain("correct", hash);
        Assert.NotEqual(hash, PasswordHasher.Hash("correct horse", iterations: 1000)); // random salt
    }

    [Fact]
    public async Task Setup_creates_one_admin_and_only_works_once()
    {
        var admin = await Users.SetupAsync(new SetupInput("Lee", "Lee Tan", "secret-pass"));
        Assert.Equal(Roles.Admin, admin.Role);
        Assert.False(admin.MustChangePassword);
        var ex = await Assert.ThrowsAsync<AppException>(() => Users.SetupAsync(new SetupInput("other", "Other", "secret-pass")));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Sign_in_ignores_user_name_case_but_not_password_case()
    {
        await Users.SetupAsync(new SetupInput("Lee", "Lee Tan", "secret-pass"));
        Assert.Equal("Lee", (await Users.LoginAsync(new LoginInput(" LEE ", "secret-pass", false))).UserName);
        await Assert.ThrowsAsync<AppException>(() => Users.LoginAsync(new LoginInput("lee", "SECRET-PASS", false)));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_five_minutes()
    {
        await Users.SetupAsync(new SetupInput("lee", "Lee", "secret-pass"));
        for (var i = 0; i < 5; i++)
            Assert.Equal(401, (await Assert.ThrowsAsync<AppException>(() => Users.LoginAsync(new LoginInput("lee", "wrong", false)))).StatusCode);

        var locked = await Assert.ThrowsAsync<AppException>(() => Users.LoginAsync(new LoginInput("lee", "secret-pass", false)));
        Assert.Equal(429, locked.StatusCode);

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.NotNull(await Users.LoginAsync(new LoginInput("lee", "secret-pass", false)));
    }

    [Fact]
    public async Task Weak_passwords_and_bad_user_names_are_rejected()
    {
        await Assert.ThrowsAsync<AppException>(() => Users.SetupAsync(new SetupInput("lee", "Lee", "short")));
        await Assert.ThrowsAsync<AppException>(() => Users.SetupAsync(new SetupInput("leeleelee", "Lee", "LeeLeeLee")));
        await Assert.ThrowsAsync<AppException>(() => Users.SetupAsync(new SetupInput("a b", "Lee", "secret-pass")));
    }

    [Fact]
    public async Task New_and_reset_users_must_change_password_and_old_sign_ins_end()
    {
        await Users.SetupAsync(new SetupInput("admin", "Admin", "secret-pass"));
        var ben = await Users.CreateAsync(new CreateUserInput("ben", "Ben", Roles.Member, "temp-pass-1"));
        Assert.True(ben.MustChangePassword);

        var changed = await Users.ChangePasswordAsync(ben.Id, "temp-pass-1", "bens-own-pass");
        Assert.False(changed.MustChangePassword);
        var stamp = changed.SecurityStamp;

        var reset = await Users.ResetPasswordAsync(ben.Id, "temp-pass-2");
        Assert.True(reset.MustChangePassword);
        Assert.NotEqual(stamp, reset.SecurityStamp);
    }

    [Fact]
    public async Task There_is_always_one_active_admin()
    {
        var admin = await Users.SetupAsync(new SetupInput("admin", "Admin", "secret-pass"));
        var ben = await Users.CreateAsync(new CreateUserInput("ben", "Ben", Roles.Admin, "temp-pass-1"));

        // You cannot lock yourself out.
        await Assert.ThrowsAsync<AppException>(() => Users.UpdateAsync(admin.Id, new UpdateUserInput("Admin", Roles.Admin, false), admin.Id));
        // Another admin can be disabled while one admin is left.
        var disabled = await Users.UpdateAsync(ben.Id, new UpdateUserInput("Ben", Roles.Admin, false), admin.Id);
        Assert.False(disabled.IsActive);
        // The last active admin cannot lose the role, even when changed by someone else.
        await Assert.ThrowsAsync<AppException>(() => Users.UpdateAsync(admin.Id, new UpdateUserInput("Admin", Roles.Member, true), ben.Id));
    }

    [Fact]
    public async Task Disabled_user_cannot_sign_in()
    {
        var admin = await Users.SetupAsync(new SetupInput("admin", "Admin", "secret-pass"));
        var ben = await Users.CreateAsync(new CreateUserInput("ben", "Ben", Roles.Member, "temp-pass-1"));
        await Users.UpdateAsync(ben.Id, new UpdateUserInput("Ben", Roles.Member, false), admin.Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Users.LoginAsync(new LoginInput("ben", "temp-pass-1", false)))).StatusCode);
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 25, 1, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

/// <summary>Runs the real app in memory and checks sign-in over HTTP.</summary>
public sealed class AuthApiTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("pmate-api-test-").FullName;
    private readonly WebApplicationFactory<Program> _app;

    public AuthApiTests()
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

    private static async Task<HttpResponseMessage> Ok(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return r;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var c = Client();
        await Ok(c.PostAsJsonAsync("/api/auth/setup", new { userName = "admin", displayName = "Admin", password = "admin-pass" }));
        return c;
    }

    [Fact]
    public async Task Data_needs_sign_in_but_health_and_login_page_do_not()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/login.html")).StatusCode);

        var page = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login.html", page.Headers.Location?.ToString());
    }

    [Fact]
    public async Task First_setup_signs_in_as_admin()
    {
        var c = Client();
        Assert.False((await c.GetFromJsonAsync<Status>("/api/auth/status"))!.HasUsers);
        await Ok(c.PostAsJsonAsync("/api/auth/setup", new { userName = "admin", displayName = "Admin", password = "admin-pass" }));

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal("admin", (await c.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Role);
        Assert.Equal(HttpStatusCode.Conflict, (await Client().PostAsJsonAsync("/api/auth/setup", new { userName = "x", displayName = "X", password = "xxxxxxxxx" })).StatusCode);
    }

    [Fact]
    public async Task Member_must_change_password_first_and_cannot_use_admin_functions()
    {
        var admin = await AdminAsync();
        await Ok(admin.PostAsJsonAsync("/api/users", new { userName = "ben", displayName = "Ben", role = "member", password = "temp-pass-1" }));

        var ben = Client();
        await Ok(ben.PostAsJsonAsync("/api/auth/login", new { userName = "ben", password = "temp-pass-1", rememberMe = true }));
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.GetAsync("/api/snapshot")).StatusCode);

        await Ok(ben.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "temp-pass-1", newPassword = "bens-own-pass" }));
        Assert.Equal(HttpStatusCode.OK, (await ben.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.PutAsJsonAsync("/api/settings", new { hoursPerDay = 7 })).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_sign_in_right_away()
    {
        var admin = await AdminAsync();
        var created = await (await Ok(admin.PostAsJsonAsync("/api/users", new { userName = "ben", displayName = "Ben", role = "member", password = "temp-pass-1" })))
            .Content.ReadFromJsonAsync<UserDto>();
        var ben = Client();
        await Ok(ben.PostAsJsonAsync("/api/auth/login", new { userName = "ben", password = "temp-pass-1" }));
        Assert.Equal(HttpStatusCode.OK, (await ben.GetAsync("/api/auth/me")).StatusCode);

        await Ok(admin.PutAsJsonAsync($"/api/users/{created!.Id}", new { displayName = "Ben", role = "member", isActive = false }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await ben.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_password_ends_other_sign_ins_but_keeps_this_one()
    {
        var here = await AdminAsync();
        var other = Client();
        await Ok(other.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "admin-pass" }));

        await Ok(here.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "admin-pass", newPassword = "new-admin-pass" }));

        Assert.Equal(HttpStatusCode.OK, (await here.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Remember_me_sets_a_lasting_cookie_and_without_it_the_cookie_ends_with_the_browser()
    {
        await AdminAsync();
        var remember = await Ok(Client().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "admin-pass", rememberMe = true }));
        var session = await Ok(Client().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "admin-pass", rememberMe = false }));

        string Cookie(HttpResponseMessage r) => r.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("pmate.auth=", StringComparison.Ordinal));
        Assert.Contains("expires=", Cookie(remember), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", Cookie(session), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", Cookie(session), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", Cookie(session), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Records_are_stamped_with_the_signed_in_user()
    {
        var admin = await AdminAsync();
        var task = await (await Ok(admin.PostAsJsonAsync("/api/tasks", new { title = "Check" }))).Content.ReadFromJsonAsync<TaskItem>();
        Assert.Equal("Admin", task!.CreatedBy);
    }

    private sealed record Status(bool HasUsers, bool SignedIn);
}
