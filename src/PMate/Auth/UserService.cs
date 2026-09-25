using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;
using PMate.Services;

namespace PMate.Auth;

/// <summary>What the API returns about a user. Never includes the password hash.</summary>
public record UserDto(string Id, string UserName, string DisplayName, string Role, bool IsActive, bool MustChangePassword,
    DateTime CreatedAt, DateTime? LastLoginAt)
{
    public static UserDto From(User u) => new(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt);
}

public record SetupInput(string? UserName, string? DisplayName, string? Password);
public record LoginInput(string? UserName, string? Password, bool RememberMe);
public record CreateUserInput(string? UserName, string? DisplayName, string? Role, string? Password);
public record UpdateUserInput(string? DisplayName, string? Role, bool IsActive);
public record PasswordInput(string? NewPassword, string? CurrentPassword = null);
public record ProfileInput(string? DisplayName);

public partial class UserService(AppDbContext db, TimeProvider time)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(5);
    // Used when the user name does not exist, so a wrong name takes as long as a wrong password.
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash("not-a-real-password"));

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public Task<bool> HasUsersAsync() => db.Users.AnyAsync();

    /// <summary>Creates the first account, as admin. Only works while there are no users.</summary>
    public async Task<User> SetupAsync(SetupInput input)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await db.Users.AnyAsync()) throw new AppException("PMate is already set up. Please sign in.", 409);
        var u = await NewUserAsync(input.UserName, input.DisplayName, Roles.Admin, input.Password);
        u.LastLoginAt = Now; // setup signs the admin in
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return u;
    }

    public async Task<User> LoginAsync(LoginInput input)
    {
        var key = Normalize(input.UserName);
        var u = await db.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == key);
        if (u is null)
        {
            PasswordHasher.Verify(input.Password ?? "", DummyHash.Value);
            throw WrongPassword();
        }
        if (u.LockoutEnd > Now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((u.LockoutEnd.Value - Now).TotalMinutes));
            throw new AppException($"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.", 429);
        }
        if (!PasswordHasher.Verify(input.Password ?? "", u.PasswordHash))
        {
            u.FailedLogins++;
            if (u.FailedLogins >= MaxFailedLogins) { u.LockoutEnd = Now + LockoutTime; u.FailedLogins = 0; }
            await db.SaveChangesAsync();
            throw WrongPassword();
        }
        // Only say "disabled" after a correct password, so the message does not reveal which accounts exist.
        if (!u.IsActive) throw new AppException("This account is disabled. Ask an admin to turn it on again.", 403);
        u.FailedLogins = 0;
        u.LockoutEnd = null;
        u.LastLoginAt = Now;
        await db.SaveChangesAsync();
        return u;
    }

    public Task<User?> FindAsync(string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<List<UserDto>> ListAsync() =>
        (await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync()).Select(UserDto.From).ToList();

    /// <summary>Admin creates a user with a temporary password. The user must choose a new one at first sign-in.</summary>
    public async Task<User> CreateAsync(CreateUserInput input)
    {
        var u = await NewUserAsync(input.UserName, input.DisplayName, input.Role, input.Password);
        u.MustChangePassword = true;
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<User> UpdateAsync(string id, UpdateUserInput input, string currentUserId)
    {
        var u = await FindAsync(id) ?? throw new AppException("User not found.", 404);
        var role = Roles.All.Contains(input.Role) ? input.Role! : u.Role;
        if (id == currentUserId && (!input.IsActive || role != Roles.Admin))
            throw new AppException("You cannot disable your own account or remove your own admin role.");
        if (u.Role == Roles.Admin && u.IsActive && (role != Roles.Admin || !input.IsActive) &&
            !await db.Users.AnyAsync(x => x.Id != id && x.Role == Roles.Admin && x.IsActive))
            throw new AppException("PMate needs at least one active admin.");

        if (role != u.Role || input.IsActive != u.IsActive) u.SecurityStamp = Ids.New();
        await RenameOwnerAsync(u.DisplayName, CleanDisplayName(input.DisplayName));
        u.DisplayName = CleanDisplayName(input.DisplayName);
        u.Role = role;
        u.IsActive = input.IsActive;
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>Admin sets a temporary password. Existing sign-ins of that user end.</summary>
    public async Task<User> ResetPasswordAsync(string id, string? newPassword)
    {
        var u = await FindAsync(id) ?? throw new AppException("User not found.", 404);
        SetPassword(u, newPassword);
        u.MustChangePassword = true;
        u.FailedLogins = 0;
        u.LockoutEnd = null;
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>The user changes their own password. Other sign-ins of this user end.</summary>
    public async Task<User> ChangePasswordAsync(string id, string? currentPassword, string? newPassword)
    {
        var u = await FindAsync(id) ?? throw new AppException("User not found.", 404);
        if (!PasswordHasher.Verify(currentPassword ?? "", u.PasswordHash)) throw new AppException("Your current password is not correct.");
        if (currentPassword == newPassword) throw new AppException("Choose a password that is different from the current one.");
        SetPassword(u, newPassword);
        u.MustChangePassword = false;
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<User> UpdateProfileAsync(string id, string? displayName)
    {
        var u = await FindAsync(id) ?? throw new AppException("User not found.", 404);
        await RenameOwnerAsync(u.DisplayName, CleanDisplayName(displayName));
        u.DisplayName = CleanDisplayName(displayName);
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>
    /// Task owners, commitment owners and RAID owners are stored as names.
    /// When a user's display name changes, move their items to the new name so "My tasks" still finds them.
    /// </summary>
    private async Task RenameOwnerAsync(string oldName, string newName)
    {
        if (oldName.Length == 0 || oldName == newName) return;
        if (await db.Users.AnyAsync(x => x.DisplayName == newName)) throw new AppException("Another user already has this display name.");
        await db.Tasks.IgnoreQueryFilters().Where(t => t.Owner == oldName).ExecuteUpdateAsync(s => s.SetProperty(t => t.Owner, newName));
        await db.Updates.IgnoreQueryFilters().Where(x => x.Who == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.Who, newName));
        await db.RaidItems.IgnoreQueryFilters().Where(r => r.Owner == oldName).ExecuteUpdateAsync(s => s.SetProperty(r => r.Owner, newName));
    }

    private async Task<User> NewUserAsync(string? userName, string? displayName, string? role, string? password)
    {
        var name = userName?.Trim() ?? "";
        if (!UserNamePattern().IsMatch(name))
            throw new AppException("User name must be 3–50 characters: letters, numbers, dot, dash, underscore or @.");
        var key = Normalize(name);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == key)) throw new AppException("This user name is already taken.");
        // Task owners are matched by display name, so two users cannot share one.
        var display = CleanDisplayName(displayName ?? name);
        if (await db.Users.AnyAsync(u => u.DisplayName == display)) throw new AppException("Another user already has this display name.");
        var u = new User
        {
            UserName = name,
            NormalizedUserName = key,
            DisplayName = CleanDisplayName(displayName ?? name),
            Role = Roles.All.Contains(role) ? role! : Roles.Member,
        };
        SetPassword(u, password);
        db.Users.Add(u);
        return u;
    }

    private static void SetPassword(User u, string? password)
    {
        if (password is null || password.Length < 8) throw new AppException("Password must be at least 8 characters.");
        if (password.Length > 200) throw new AppException("Password is too long.");
        if (Normalize(password) == u.NormalizedUserName) throw new AppException("Password cannot be the same as the user name.");
        u.PasswordHash = PasswordHasher.Hash(password);
        u.SecurityStamp = Ids.New();
    }

    private static string CleanDisplayName(string? s) =>
        Text.Clean(s) is { } n ? (n.Length <= 100 ? n : n[..100]) : throw new AppException("Display name is required.");

    public static string Normalize(string? userName) => (userName ?? "").Trim().ToLowerInvariant();

    private static AppException WrongPassword() => new("Wrong user name or password.", 401);

    [GeneratedRegex(@"^[A-Za-z0-9._@-]{3,50}$")]
    private static partial Regex UserNamePattern();
}
