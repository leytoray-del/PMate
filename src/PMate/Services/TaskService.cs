using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record TaskInput(string? Title, string? ProjectId, string? MilestoneId, string? Owner, DateOnly? Due,
    string? Status, string? Priority, string? Description);

public record TaskSaveResult(TaskItem Task, TaskUpdate? StatusChange);

public record UpdateInput(string? TaskId, string? ProjectId, DateOnly? Date, string? Type, string? Text, double? Hours,
    string? Who, DateOnly? CommitDue);

public class TaskService(AppDbContext db, ICurrentUser user, TrashService trash)
{
    public async Task<TaskItem> CreateAsync(TaskInput input)
    {
        var statuses = await Statuses();
        var t = new TaskItem { CreatedBy = user.Name, CreatedById = user.Id };
        await ApplyAsync(t, input, statuses, isNew: true);
        if (statuses.First(s => s.Id == t.Status).IsDone) t.DoneAt = DateTime.UtcNow;
        db.Tasks.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    /// <summary>Saves all task fields. A status change is also written to the task's timeline.</summary>
    public async Task<TaskSaveResult> UpdateAsync(string id, TaskInput input)
    {
        var t = await db.Tasks.FindAsync(id) ?? throw new AppException("Task not found.", 404);
        var statuses = await Statuses();
        var oldStatus = t.Status;
        var oldProject = t.ProjectId;
        await ApplyAsync(t, input, statuses, isNew: false);

        TaskUpdate? change = null;
        if (t.Status != oldStatus)
        {
            var from = statuses.FirstOrDefault(s => s.Id == oldStatus);
            var to = statuses.First(s => s.Id == t.Status);
            if (!to.IsDone) t.DoneAt = null;
            else if (from is not { IsDone: true } || t.DoneAt is null) t.DoneAt = DateTime.UtcNow;
            change = new TaskUpdate
            {
                TaskId = t.Id, ProjectId = t.ProjectId, Date = Clock.Today, Type = UpdateTypes.StatusChange,
                FromStatus = oldStatus, ToStatus = t.Status, FromName = from?.Name, ToName = to.Name, CreatedBy = user.Name, CreatedById = user.Id,
            };
            db.Updates.Add(change);
        }
        if (t.ProjectId != oldProject)
        {
            foreach (var u in await db.Updates.Where(u => u.TaskId == id).ToListAsync()) u.ProjectId = t.ProjectId;
        }
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new TaskSaveResult(t, change);
    }

    /// <summary>Deletes the task and its timeline. Can be undone.</summary>
    public async Task<DeleteResult> DeleteAsync(string id)
    {
        var t = await db.Tasks.FindAsync(id) ?? throw new AppException("Task not found.", 404);
        var updates = await db.Updates.Where(u => u.TaskId == id).ToListAsync();
        var result = trash.MarkDeleted("task", $"Task \"{t.Title}\"", [t, .. updates]);
        await db.SaveChangesAsync();
        return result;
    }

    public async Task<TaskUpdate> AddUpdateAsync(UpdateInput input)
    {
        var (text, hours) = Validate(input);

        string? projectId = Text.Clean(input.ProjectId);
        var taskId = Text.Clean(input.TaskId);
        if (taskId is not null)
        {
            var t = await db.Tasks.FindAsync(taskId) ?? throw new AppException("Task not found.", 404);
            projectId = t.ProjectId;
            t.UpdatedAt = DateTime.UtcNow;
        }
        else if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId))
        {
            throw new AppException("Project not found.");
        }

        var isCommit = input.Type == UpdateTypes.Commitment;
        var u = new TaskUpdate
        {
            TaskId = taskId, ProjectId = projectId, Date = input.Date ?? Clock.Today, Type = input.Type!, Text = text,
            Hours = Math.Round(hours, 2), Who = isCommit ? Text.Clean(input.Who) : null, CommitDue = isCommit ? input.CommitDue : null,
            CreatedBy = user.Name, CreatedById = user.Id,
        };
        db.Updates.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<TaskUpdate> SetCommitDoneAsync(string id, bool done)
    {
        var u = await db.Updates.FindAsync(id) ?? throw new AppException("Record not found.", 404);
        if (u.Type != UpdateTypes.Commitment) throw new AppException("Only commitments can be marked as kept.");
        u.CommitDone = done;
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>Edits a record's type, date, text, hours and commitment fields. The task stays the same.</summary>
    public async Task<TaskUpdate> EditUpdateAsync(string id, UpdateInput input)
    {
        var u = await db.Updates.FindAsync(id) ?? throw new AppException("Record not found.", 404);
        if (u.Type == UpdateTypes.StatusChange) throw new AppException("Status changes cannot be edited.");
        var (text, hours) = Validate(input);
        var isCommit = input.Type == UpdateTypes.Commitment;
        u.Type = input.Type!;
        u.Text = text;
        u.Hours = Math.Round(hours, 2);
        u.Date = input.Date ?? u.Date;
        u.Who = isCommit ? Text.Clean(input.Who) : null;
        u.CommitDue = isCommit ? input.CommitDue : null;
        if (!isCommit) u.CommitDone = false;
        if (u.TaskId is null)
        {
            var projectId = Text.Clean(input.ProjectId);
            if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId)) throw new AppException("Project not found.");
            u.ProjectId = projectId;
        }
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<DeleteResult> DeleteUpdateAsync(string id)
    {
        var u = await db.Updates.FindAsync(id) ?? throw new AppException("Record not found.", 404);
        var label = u.Type == UpdateTypes.StatusChange ? "Status change" : $"Record \"{Shorten(u.Text)}\"";
        var result = trash.MarkDeleted("update", label, [u]);
        await db.SaveChangesAsync();
        return result;
    }

    private static (string Text, double Hours) Validate(UpdateInput input)
    {
        if (!UpdateTypes.UserTypes.Contains(input.Type)) throw new AppException("Unknown record type.");
        var text = Text.Clean(input.Text) ?? throw new AppException("Please enter some text.");
        var hours = input.Hours ?? 0;
        if (hours is < 0 or > 24) throw new AppException("Hours must be between 0 and 24.");
        return (text, hours);
    }

    internal static string Shorten(string s, int max = 40) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private Task<List<BoardStatus>> Statuses() => db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();

    private async Task ApplyAsync(TaskItem t, TaskInput i, List<BoardStatus> statuses, bool isNew)
    {
        t.Title = Text.Clean(i.Title) ?? throw new AppException("Task name is required.");
        var projectId = Text.Clean(i.ProjectId);
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId)) throw new AppException("Project not found.");
        var milestoneId = Text.Clean(i.MilestoneId);
        if (milestoneId is not null && !await db.Milestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId)) milestoneId = null;
        t.ProjectId = projectId;
        t.MilestoneId = milestoneId;
        t.Owner = Text.Clean(i.Owner);
        t.Due = i.Due;
        t.Priority = Priorities.All.Contains(i.Priority) ? i.Priority! : Priorities.Medium;
        t.Description = Text.Clean(i.Description);
        var status = statuses.FirstOrDefault(s => s.Id == i.Status)
            ?? (isNew ? null : statuses.FirstOrDefault(s => s.Id == t.Status))
            ?? statuses.First(s => !s.IsDone);
        t.Status = status.Id;
    }
}
