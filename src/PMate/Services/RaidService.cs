using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record RaidInput(string? ProjectId, string? Type, string? Title, string? Description, string? Owner,
    string? Impact, string? Likelihood, string? Status, string? Response, DateOnly? DueDate);

public class RaidService(AppDbContext db, ICurrentUser user, TrashService trash)
{
    public async Task<RaidItem> SaveAsync(string? id, RaidInput i)
    {
        var r = id is null ? new RaidItem { CreatedBy = user.Name } : await db.RaidItems.FindAsync(id) ?? throw new AppException("RAID item not found.", 404);
        var projectId = Text.Clean(i.ProjectId) ?? throw new AppException("Please choose a project.");
        if (!await db.Projects.AnyAsync(p => p.Id == projectId)) throw new AppException("Project not found.");
        if (!RaidTypes.All.Contains(i.Type)) throw new AppException("Unknown RAID type.");

        // A new number when the item is new, or when it moves to another project or type.
        if (id is null || r.ProjectId != projectId || r.Type != i.Type)
        {
            var max = await db.RaidItems.IgnoreQueryFilters()
                .Where(x => x.ProjectId == projectId && x.Type == i.Type && x.Id != r.Id)
                .MaxAsync(x => (int?)x.Number) ?? 0;
            r.Number = max + 1;
        }
        r.ProjectId = projectId;
        r.Type = i.Type!;
        r.Title = Text.Clean(i.Title) ?? throw new AppException("Title is required.");
        r.Description = Text.Clean(i.Description);
        r.Owner = Text.Clean(i.Owner);
        r.Impact = Levels.All.Contains(i.Impact) ? i.Impact! : Levels.Medium;
        r.Likelihood = r.Type == RaidTypes.Risk ? (Levels.All.Contains(i.Likelihood) ? i.Likelihood : Levels.Medium) : null;
        var status = RaidStatuses.All.Contains(i.Status) ? i.Status! : RaidStatuses.Open;
        if (status == RaidStatuses.Closed && r.Status != RaidStatuses.Closed) r.ClosedAt = DateTime.UtcNow;
        if (status != RaidStatuses.Closed) r.ClosedAt = null;
        r.Status = status;
        r.Response = Text.Clean(i.Response);
        r.DueDate = i.DueDate;
        r.UpdatedAt = DateTime.UtcNow;
        if (id is null) db.RaidItems.Add(r);
        await db.SaveChangesAsync();
        return r;
    }

    public async Task<DeleteResult> DeleteAsync(string id)
    {
        var r = await db.RaidItems.FindAsync(id) ?? throw new AppException("RAID item not found.", 404);
        var result = trash.MarkDeleted("raid", $"{char.ToUpperInvariant(r.Type[0])}-{r.Number} \"{TaskService.Shorten(r.Title)}\"", [r]);
        await db.SaveChangesAsync();
        return result;
    }
}
