using System.Text.Json.Serialization;

namespace PMate.Domain;

public static class Ids
{
    public static string New() => Guid.CreateVersion7().ToString("N");
}

public static class ProjectStatuses
{
    public const string Active = "active", OnHold = "hold", Closed = "closed";
    public static readonly string[] All = [Active, OnHold, Closed];
}

public static class MilestoneStatuses
{
    public const string Planned = "planned", InProgress = "doing", Done = "done";
    public static readonly string[] All = [Planned, InProgress, Done];
}

public static class Priorities
{
    public const string High = "high", Medium = "medium", Low = "low";
    public static readonly string[] All = [High, Medium, Low];
}

public static class UpdateTypes
{
    public const string Work = "work", Note = "note", Commitment = "commit", Decision = "decision", Risk = "risk";
    /// <summary>Written by the system when a task moves between board columns.</summary>
    public const string StatusChange = "status";
    public static readonly string[] UserTypes = [Work, Note, Commitment, Decision, Risk];
}

public static class StatusColors
{
    public static readonly string[] All = ["gray", "blue", "purple", "amber", "red", "green", "teal", "pink"];
}

/// <summary>RAID = Risks, Assumptions, Issues, Dependencies.</summary>
public static class RaidTypes
{
    public const string Risk = "risk", Assumption = "assumption", Issue = "issue", Dependency = "dependency";
    public static readonly string[] All = [Risk, Assumption, Issue, Dependency];
}

public static class RaidStatuses
{
    public const string Open = "open", Monitoring = "monitoring", Closed = "closed";
    public static readonly string[] All = [Open, Monitoring, Closed];
}

public static class Levels
{
    public const string High = "high", Medium = "medium", Low = "low";
    public static readonly string[] All = [High, Medium, Low];
}

/// <summary>
/// Rows are not removed right away. They get a DeletedAt time and the id of a <see cref="DeleteBatch"/>,
/// so a delete can be undone. A global query filter hides them. Batches older than 30 days are purged.
/// </summary>
public interface ISoftDelete
{
    DateTime? DeletedAt { get; set; }
    string? DeleteBatchId { get; set; }
}

public class Project : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Client { get; set; }
    public string Status { get; set; } = ProjectStatuses.Active;
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string? Description { get; set; }
    public bool IsSample { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

public class Milestone : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly? PlanDate { get; set; }
    public DateOnly? ActualDate { get; set; }
    public string Status { get; set; } = MilestoneStatuses.Planned;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

public class TaskItem : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string? ProjectId { get; set; }
    public string? MilestoneId { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Display name for now. A user id can be added when the app becomes multi-user.</summary>
    public string? Owner { get; set; }
    public DateOnly? Due { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = Priorities.Medium;
    public string? Description { get; set; }
    public bool IsSample { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DoneAt { get; set; }
    public string? CreatedBy { get; set; }
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

/// <summary>One entry on a task's timeline: a work log, note, commitment, decision, risk, or status change.</summary>
public class TaskUpdate : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string? TaskId { get; set; }
    public string? ProjectId { get; set; }
    public DateOnly Date { get; set; }
    public string Type { get; set; } = UpdateTypes.Work;
    public string Text { get; set; } = "";
    public double Hours { get; set; }
    public string? Who { get; set; }
    public DateOnly? CommitDue { get; set; }
    public bool CommitDone { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    /// <summary>Column names at the time of the change, so history stays readable after a column is renamed or removed.</summary>
    public string? FromName { get; set; }
    public string? ToName { get; set; }
    public bool IsSample { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

/// <summary>A work note, for example meeting minutes. Searchable with SQLite full-text search.</summary>
public class Note : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Comma-separated.</summary>
    public string? Tags { get; set; }
    public DateOnly NoteDate { get; set; }
    public bool Pinned { get; set; }
    public bool IsSample { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

/// <summary>One row in a project's RAID log.</summary>
public class RaidItem : ISoftDelete
{
    public string Id { get; set; } = Ids.New();
    public string ProjectId { get; set; } = "";
    public string Type { get; set; } = RaidTypes.Risk;
    /// <summary>Running number per project and type, shown as R-1, I-3, …</summary>
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public string Impact { get; set; } = Levels.Medium;
    /// <summary>Only used for risks.</summary>
    public string? Likelihood { get; set; }
    public string Status { get; set; } = RaidStatuses.Open;
    /// <summary>Mitigation for a risk, resolution for an issue, how to check an assumption, or follow-up for a dependency.</summary>
    public string? Response { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsSample { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string? CreatedBy { get; set; }
    [JsonIgnore] public DateTime? DeletedAt { get; set; }
    [JsonIgnore] public string? DeleteBatchId { get; set; }
}

/// <summary>One delete action that can be undone. All rows removed by that action share its id.</summary>
public class DeleteBatch
{
    public string Id { get; set; } = Ids.New();
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public int ItemCount { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}

public static class Roles
{
    /// <summary>Manages users, settings, board columns, backups and updates.</summary>
    public const string Admin = "admin";
    /// <summary>Works with projects, tasks, notes and the RAID log.</summary>
    public const string Member = "member";
    public static readonly string[] All = [Admin, Member];
}

/// <summary>A person who can sign in. Users are disabled, never deleted, so "created by" history stays valid.</summary>
public class User
{
    public string Id { get; set; } = Ids.New();
    public string UserName { get; set; } = "";
    /// <summary>Lower-case user name, unique. Sign-in is not case-sensitive.</summary>
    public string NormalizedUserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = Roles.Member;
    /// <summary>PBKDF2 hash with salt, see <c>PasswordHasher</c>. The password itself is never stored.</summary>
    [JsonIgnore] public string PasswordHash { get; set; } = "";
    /// <summary>Changes when the password, role or active state changes. Sign-ins made before the change stop working.</summary>
    [JsonIgnore] public string SecurityStamp { get; set; } = Ids.New();
    public bool IsActive { get; set; } = true;
    /// <summary>Set after an admin creates the user or resets the password.</summary>
    public bool MustChangePassword { get; set; }
    [JsonIgnore] public int FailedLogins { get; set; }
    [JsonIgnore] public DateTime? LockoutEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>A board column. Users can rename, reorder, add and remove them.</summary>
public class BoardStatus
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "gray";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}
