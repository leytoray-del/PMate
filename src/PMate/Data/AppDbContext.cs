using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PMate.Domain;

namespace PMate.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskUpdate> Updates => Set<TaskUpdate>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<RaidItem> RaidItems => Set<RaidItem>();
    public DbSet<DeleteBatch> DeleteBatches => Set<DeleteBatch>();
    public DbSet<BoardStatus> Statuses => Set<BoardStatus>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // No foreign key constraints: deletes cascade in the services, and imports stay tolerant of old data.
        b.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Code).HasMaxLength(50);
            e.HasIndex(x => x.Code);
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

        SoftDelete<Project>(b);
        SoftDelete<Milestone>(b);
        SoftDelete<TaskItem>(b);
        SoftDelete<TaskUpdate>(b);
        SoftDelete<Note>(b);
        SoftDelete<RaidItem>(b);
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
        b.Entity<T>().HasQueryFilter(x => x.DeletedAt == null);
        b.Entity<T>().HasIndex(x => x.DeleteBatchId);
    }
}
