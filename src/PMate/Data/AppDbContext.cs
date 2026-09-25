using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PMate.Domain;
using PMate.Services;

namespace PMate.Data;

/// <param name="user">
/// The signed-in user. Decides what the access filters let through. Null (background jobs, tests, sign-in itself)
/// means no access limit.
/// </param>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? user = null) : DbContext(options)
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string AccessFilter = "Access";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskUpdate> Updates => Set<TaskUpdate>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<RaidItem> RaidItems => Set<RaidItem>();
    public DbSet<DeleteBatch> DeleteBatches => Set<DeleteBatch>();
    public DbSet<BoardStatus> Statuses => Set<BoardStatus>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<User> Users => Set<User>();

    // Read by the access filters each time a query runs. EF turns them into query parameters.
    public bool AccessAll => user?.Id is null || user.IsAdmin;
    public string? AccessUserId => user?.Id;
    public string? AccessUserName => user?.Name;

    protected override void OnModelCreating(ModelBuilder b)
    {
        // No foreign key constraints: deletes cascade in the services, and imports stay tolerant of old data.
        b.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Code).HasMaxLength(50);
            e.HasIndex(x => x.Code);
        });
        b.Entity<ProjectMember>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.UserId });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<Milestone>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.ProjectId);
        });
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).HasMaxLength(500);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Due);
        });
        b.Entity<TaskUpdate>(e =>
        {
            e.ToTable("Updates");
            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.CreatedById);
        });
        b.Entity<Note>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.NoteDate);
        });
        b.Entity<RaidItem>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(500);
            e.HasIndex(x => new { x.ProjectId, x.Type });
        });
        b.Entity<BoardStatus>(e =>
        {
            e.ToTable("Statuses");
            e.Property(x => x.Name).HasMaxLength(100);
        });
        b.Entity<Setting>(e => e.HasKey(x => x.Key));
        b.Entity<User>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(50);
            e.Property(x => x.NormalizedUserName).HasMaxLength(50);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
        });

        SoftDelete<Project>(b);
        SoftDelete<Milestone>(b);
        SoftDelete<TaskItem>(b);
        SoftDelete<TaskUpdate>(b);
        SoftDelete<Note>(b);
        SoftDelete<RaidItem>(b);

        // Who sees what. Admins see everything. Members see:
        // - projects they are on, and everything in those projects;
        // - tasks without a project that they created or own;
        // - notes and records without a project or task that they created.
        b.Entity<Project>().HasQueryFilter(AccessFilter, p =>
            AccessAll || ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == AccessUserId));
        b.Entity<Milestone>().HasQueryFilter(AccessFilter, x =>
            AccessAll || ProjectMembers.Any(m => m.ProjectId == x.ProjectId && m.UserId == AccessUserId));
        b.Entity<RaidItem>().HasQueryFilter(AccessFilter, x =>
            AccessAll || ProjectMembers.Any(m => m.ProjectId == x.ProjectId && m.UserId == AccessUserId));
        b.Entity<TaskItem>().HasQueryFilter(AccessFilter, t =>
            AccessAll
            || (t.ProjectId != null && ProjectMembers.Any(m => m.ProjectId == t.ProjectId && m.UserId == AccessUserId))
            || (t.ProjectId == null && (t.CreatedById == AccessUserId || (t.Owner != null && t.Owner == AccessUserName))));
        b.Entity<TaskUpdate>().HasQueryFilter(AccessFilter, u =>
            AccessAll
            || (u.ProjectId != null && ProjectMembers.Any(m => m.ProjectId == u.ProjectId && m.UserId == AccessUserId))
            || (u.ProjectId == null && u.TaskId != null && Tasks.Any(t => t.Id == u.TaskId))
            || (u.ProjectId == null && u.TaskId == null && u.CreatedById == AccessUserId));
        b.Entity<Note>().HasQueryFilter(AccessFilter, n =>
            AccessAll
            || (n.ProjectId != null && ProjectMembers.Any(m => m.ProjectId == n.ProjectId && m.UserId == AccessUserId))
            || (n.ProjectId == null && n.CreatedById == AccessUserId));
        b.Entity<DeleteBatch>().HasQueryFilter(AccessFilter, x => AccessAll || x.DeletedById == AccessUserId);
    }

    /// <summary>
    /// All times are stored in UTC. SQLite does not keep the time zone, so mark values read back as UTC.
    /// Otherwise they are sent to the browser without "Z" and read as local time.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static void SoftDelete<T>(ModelBuilder b) where T : class, ISoftDelete
    {
        b.Entity<T>().HasQueryFilter(SoftDeleteFilter, x => x.DeletedAt == null);
        b.Entity<T>().HasIndex(x => x.DeleteBatchId);
    }
}
