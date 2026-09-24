using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record StatusInput(string? Id, string Name, string? Color, bool IsDone);

public class StatusService(AppDbContext db)
{
    public static BoardStatus[] Defaults() =>
    [
        new() { Id = "todo", Name = "To Do", Color = "gray", SortOrder = 0 },
        new() { Id = "doing", Name = "In Progress", Color = "blue", SortOrder = 1 },
        new() { Id = "waiting", Name = "Waiting", Color = "purple", SortOrder = 2 },
        new() { Id = "done", Name = "Done", Color = "green", IsDone = true, SortOrder = 3 },
    ];

    public Task<List<BoardStatus>> ListAsync() => db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();

    public async Task EnsureDefaultsAsync()
    {
        if (await db.Statuses.AnyAsync()) return;
        db.Statuses.AddRange(Defaults());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Replaces the board columns. Tasks in removed columns move to the first open column
    /// (or the first done column if they were done).
    /// </summary>
    public async Task<List<BoardStatus>> ReplaceAsync(IReadOnlyList<StatusInput> input)
    {
        if (input.Count == 0) throw new AppException("At least one column is required.");
        if (input.Any(s => string.IsNullOrWhiteSpace(s.Name))) throw new AppException("Every column needs a name.");
        if (!input.Any(s => s.IsDone)) throw new AppException("Mark at least one column as Done.");
        if (!input.Any(s => !s.IsDone)) throw new AppException("You need at least one column that is not Done.");

        var next = input.Select((s, i) => new BoardStatus
        {
            Id = Text.Clean(s.Id) ?? Ids.New(),
            Name = s.Name.Trim(),
            Color = StatusColors.All.Contains(s.Color) ? s.Color! : "gray",
            IsDone = s.IsDone,
            SortOrder = i,
        }).ToList();
        if (next.Select(s => s.Id).Distinct().Count() != next.Count) throw new AppException("Column ids must be unique.");

        var existing = await db.Statuses.ToListAsync();
        var oldDone = existing.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();
        var byId = next.ToDictionary(s => s.Id);
        var fallbackOpen = next.First(s => !s.IsDone).Id;
        var fallbackDone = next.First(s => s.IsDone).Id;

        // Update in place, then remove and add, so EF never tracks two rows with the same key.
        foreach (var e in existing)
        {
            if (byId.TryGetValue(e.Id, out var n)) { e.Name = n.Name; e.Color = n.Color; e.IsDone = n.IsDone; e.SortOrder = n.SortOrder; }
            else db.Statuses.Remove(e);
        }
        var existingIds = existing.Select(e => e.Id).ToHashSet();
        db.Statuses.AddRange(next.Where(n => !existingIds.Contains(n.Id)));

        var now = DateTime.UtcNow;
        foreach (var t in await db.Tasks.ToListAsync())
        {
            if (!byId.ContainsKey(t.Status)) t.Status = oldDone.Contains(t.Status) ? fallbackDone : fallbackOpen;
            var done = byId[t.Status].IsDone;
            if (done && t.DoneAt is null) t.DoneAt = now;
            if (!done) t.DoneAt = null;
        }

        await db.SaveChangesAsync();
        return await ListAsync();
    }
}
