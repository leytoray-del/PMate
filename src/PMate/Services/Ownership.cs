using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

/// <summary>
/// Records made before creator ids existed (or restored from an old backup) have no creator id.
/// Runs at start-up, after the first admin is created, and after an import.
/// </summary>
public static class Ownership
{
    // Tables with CreatedBy (display name) and CreatedById columns. Fixed names, never user input.
    internal static readonly string[] Tables = ["Projects", "Tasks", "Updates", "Notes", "RaidItems"];

    public static async Task AssignUnownedAsync(AppDbContext db)
    {
        // 1. Older versions saved only the creator's display name. Find that user.
        foreach (var t in Tables)
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE {t} SET CreatedById = (SELECT u.Id FROM Users u WHERE u.DisplayName = {t}.CreatedBy)
                WHERE CreatedById IS NULL AND CreatedBy IN (SELECT DisplayName FROM Users)
                """);

        // 2. Records with no known creator (made before sign-in existed) go to the first admin.
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
