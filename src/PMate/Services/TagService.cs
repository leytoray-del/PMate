using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record TagInput(string? Id, string Name, string? Color);

/// <summary>Task tags. The migration AddTaskTagsAndProjectColor adds the default tags: Meeting, On-site, Doc and others.</summary>
public class TagService(AppDbContext db)
{
    public const int MaxNameLength = 20;

    public Task<List<TaskTag>> ListAsync() => db.Tags.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();

    /// <summary>Replaces the tag list. Tasks with a removed tag keep no tag.</summary>
    public async Task<List<TaskTag>> ReplaceAsync(IReadOnlyList<TagInput> input)
    {
        if (input.Any(s => string.IsNullOrWhiteSpace(s.Name))) throw new AppException("Every tag needs a name.");
        if (input.Any(s => s.Name.Trim().Length > MaxNameLength)) throw new AppException($"Keep tag names to {MaxNameLength} characters or fewer.");
        if (input.Select(s => s.Name.Trim().ToLowerInvariant()).Distinct().Count() != input.Count) throw new AppException("Two tags have the same name.");

        var next = input.Select((s, i) => new TaskTag
        {
            Id = Text.Clean(s.Id) ?? Ids.New(),
            Name = s.Name.Trim(),
            Color = StatusColors.All.Contains(s.Color) ? s.Color! : "gray",
            SortOrder = i,
        }).ToList();
        if (next.Select(s => s.Id).Distinct().Count() != next.Count) throw new AppException("Tag ids must be unique.");

        var existing = await db.Tags.ToListAsync();
        var byId = next.ToDictionary(s => s.Id);
        // Update in place, then remove and add, so EF never tracks two rows with the same key.
        foreach (var e in existing)
        {
            if (byId.TryGetValue(e.Id, out var n)) { e.Name = n.Name; e.Color = n.Color; e.SortOrder = n.SortOrder; }
            else db.Tags.Remove(e);
        }
        var existingIds = existing.Select(e => e.Id).ToHashSet();
        db.Tags.AddRange(next.Where(n => !existingIds.Contains(n.Id)));

        var removed = existing.Select(e => e.Id).Where(id => !byId.ContainsKey(id)).ToList();
        if (removed.Count > 0)
        {
            // Include tasks in the trash, so a restored task does not point to a missing tag.
            foreach (var t in await db.Tasks.IgnoreQueryFilters().Where(t => t.TagId != null && removed.Contains(t.TagId)).ToListAsync()) t.TagId = null;
        }

        await db.SaveChangesAsync();
        return await ListAsync();
    }
}
