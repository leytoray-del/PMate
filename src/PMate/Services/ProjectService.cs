using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record ProjectInput(string? Name, string? Code, string? Client, string? Status, DateOnly? Start, DateOnly? End, string? Description);

public record MilestoneInput(string? ProjectId, string? Name, DateOnly? PlanDate, DateOnly? ActualDate, string? Status, string? Note);

public class ProjectService(AppDbContext db, ICurrentUser user, TrashService trash)
{
    public async Task<Project> SaveAsync(string? id, ProjectInput i)
    {
        var p = id is null ? new Project { CreatedBy = user.Name } : await db.Projects.FindAsync(id) ?? throw new AppException("Project not found.", 404);
        p.Name = Text.Clean(i.Name) ?? throw new AppException("Project name is required.");
        p.Code = Text.Clean(i.Code);
        p.Client = Text.Clean(i.Client);
        p.Status = ProjectStatuses.All.Contains(i.Status) ? i.Status! : ProjectStatuses.Active;
        p.Start = i.Start;
        p.End = i.End;
        if (p.Start is not null && p.End is not null && p.End < p.Start) throw new AppException("End date cannot be before the start date.");
        p.Description = Text.Clean(i.Description);
        p.UpdatedAt = DateTime.UtcNow;
        if (id is null) db.Projects.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Deletes the project with its milestones, tasks, records, notes and RAID items. Can be undone.</summary>
    public async Task<DeleteResult> DeleteAsync(string id)
    {
        var p = await db.Projects.FindAsync(id) ?? throw new AppException("Project not found.", 404);
        var tasks = await db.Tasks.Where(t => t.ProjectId == id).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var rows = new List<ISoftDelete> { p };
        rows.AddRange(tasks);
        rows.AddRange(await db.Updates.Where(u => u.ProjectId == id || (u.TaskId != null && taskIds.Contains(u.TaskId))).ToListAsync());
        rows.AddRange(await db.Milestones.Where(m => m.ProjectId == id).ToListAsync());
        rows.AddRange(await db.Notes.Where(n => n.ProjectId == id).ToListAsync());
        rows.AddRange(await db.RaidItems.Where(r => r.ProjectId == id).ToListAsync());
        var result = trash.MarkDeleted("project", $"Project \"{p.Name}\" with {tasks.Count} task(s)", rows);
        await db.SaveChangesAsync();
        return result;
    }

    public async Task<Milestone> SaveMilestoneAsync(string? id, MilestoneInput i)
    {
        Milestone m;
        if (id is null)
        {
            var projectId = Text.Clean(i.ProjectId) ?? throw new AppException("Project is required.");
            if (!await db.Projects.AnyAsync(p => p.Id == projectId)) throw new AppException("Project not found.");
            m = new Milestone { ProjectId = projectId };
            db.Milestones.Add(m);
        }
        else m = await db.Milestones.FindAsync(id) ?? throw new AppException("Milestone not found.", 404);

        m.Name = Text.Clean(i.Name) ?? throw new AppException("Milestone name is required.");
        m.PlanDate = i.PlanDate;
        m.Status = MilestoneStatuses.All.Contains(i.Status) ? i.Status! : MilestoneStatuses.Planned;
        m.ActualDate = m.Status == MilestoneStatuses.Done ? i.ActualDate ?? Clock.Today : i.ActualDate;
        m.Note = Text.Clean(i.Note);
        m.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return m;
    }

    /// <summary>
    /// Deletes the milestone. Its tasks are kept. They keep the link while the milestone is in the trash,
    /// so an undo restores it; the UI shows such tasks as having no milestone.
    /// </summary>
    public async Task<DeleteResult> DeleteMilestoneAsync(string id)
    {
        var m = await db.Milestones.FindAsync(id) ?? throw new AppException("Milestone not found.", 404);
        var result = trash.MarkDeleted("milestone", $"Milestone \"{m.Name}\"", [m]);
        await db.SaveChangesAsync();
        return result;
    }
}
