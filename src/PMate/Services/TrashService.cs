using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record DeleteResult(string UndoId, string Label);

/// <summary>Soft delete with undo. See <see cref="ISoftDelete"/>.</summary>
public class TrashService(AppDbContext db, ICurrentUser? user = null)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    /// <summary>Marks the rows as deleted under one new batch. The caller still calls SaveChanges.</summary>
    public DeleteResult MarkDeleted(string kind, string label, IEnumerable<ISoftDelete> rows)
    {
        var batch = new DeleteBatch { Kind = kind, Label = label, DeletedById = user?.Id };
        var count = 0;
        foreach (var r in rows.Where(r => r.DeletedAt is null))
        {
            r.DeletedAt = batch.DeletedAt;
            r.DeleteBatchId = batch.Id;
            count++;
        }
        batch.ItemCount = count;
        db.DeleteBatches.Add(batch);
        return new DeleteResult(batch.Id, label);
    }

    public Task<List<DeleteBatch>> ListAsync() => db.DeleteBatches.AsNoTracking().OrderByDescending(b => b.DeletedAt).ToListAsync();

    public async Task RestoreAsync(string batchId)
    {
        var batch = await db.DeleteBatches.FindAsync(batchId) ?? throw new AppException("This item can no longer be restored.", 404);
        await RestoreRows(db.Projects, batchId);
        await RestoreRows(db.Milestones, batchId);
        await RestoreRows(db.Tasks, batchId);
        await RestoreRows(db.Updates, batchId);
        await RestoreRows(db.Notes, batchId);
        await RestoreRows(db.RaidItems, batchId);
        db.DeleteBatches.Remove(batch);
        await db.SaveChangesAsync();
    }

    /// <summary>Permanently removes batches older than <see cref="KeepFor"/>.</summary>
    public async Task<int> PurgeAsync(DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.UtcNow) - KeepFor;
        var ids = await db.DeleteBatches.Where(b => b.DeletedAt < cutoff).Select(b => b.Id).ToListAsync();
        if (ids.Count == 0) return 0;
        await PurgeRows(db.Projects, ids);
        await PurgeRows(db.Milestones, ids);
        await PurgeRows(db.Tasks, ids);
        await PurgeRows(db.Updates, ids);
        await PurgeRows(db.Notes, ids);
        await PurgeRows(db.RaidItems, ids);
        await db.DeleteBatches.Where(b => ids.Contains(b.Id)).ExecuteDeleteAsync();
        return ids.Count;
    }

    private static async Task RestoreRows<T>(DbSet<T> set, string batchId) where T : class, ISoftDelete
    {
        foreach (var r in await set.IgnoreQueryFilters().Where(x => x.DeleteBatchId == batchId).ToListAsync())
        {
            r.DeletedAt = null;
            r.DeleteBatchId = null;
        }
    }

    private static Task PurgeRows<T>(DbSet<T> set, List<string> ids) where T : class, ISoftDelete =>
        set.IgnoreQueryFilters().Where(x => x.DeleteBatchId != null && ids.Contains(x.DeleteBatchId)).ExecuteDeleteAsync();
}
