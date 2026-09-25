using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

/// <summary>
/// Records made before users existed (or restored from an old backup) have no creator id.
/// This gives them to the first admin, so they count as that admin's hours and private items.
/// Runs at start-up, after the first admin is created, and after an import.
/// </summary>
public static class Ownership
{
    public static async Task AssignUnownedAsync(AppDbContext db)
    {
        var admin = await db.Users.Where(u => u.Role == Roles.Admin && u.IsActive)
            .OrderBy(u => u.CreatedAt).Select(u => u.Id).FirstOrDefaultAsync();
        if (admin is null) return;
        await db.Projects.IgnoreQueryFilters().Where(x => x.CreatedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedById, admin));
        await db.Tasks.IgnoreQueryFilters().Where(x => x.CreatedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedById, admin));
        await db.Updates.IgnoreQueryFilters().Where(x => x.CreatedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedById, admin));
        await db.Notes.IgnoreQueryFilters().Where(x => x.CreatedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedById, admin));
        await db.RaidItems.IgnoreQueryFilters().Where(x => x.CreatedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedById, admin));
        await db.DeleteBatches.IgnoreQueryFilters().Where(x => x.DeletedById == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedById, admin));
    }
}
