using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record NoteInput(string? ProjectId, string? Title, string? Body, string? Tags, DateOnly? NoteDate, bool Pinned);

/// <summary>A search result. The snippet marks matches with \u0001 (start) and \u0002 (end).</summary>
public record NoteHit(string Id, string Snippet);

public class NoteService(AppDbContext db, ICurrentUser user, TrashService trash)
{
    public async Task<Note> SaveAsync(string? id, NoteInput i)
    {
        var n = id is null ? new Note { CreatedBy = user.Name } : await db.Notes.FindAsync(id) ?? throw new AppException("Note not found.", 404);
        var projectId = Text.Clean(i.ProjectId);
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId)) throw new AppException("Project not found.");
        n.ProjectId = projectId;
        n.Body = i.Body ?? "";
        n.Title = Text.Clean(i.Title) ?? "";
        n.Tags = NormalizeTags(i.Tags);
        n.NoteDate = i.NoteDate ?? (id is null ? Clock.Today : n.NoteDate);
        n.Pinned = i.Pinned;
        n.UpdatedAt = DateTime.UtcNow;
        if (id is null) db.Notes.Add(n);
        await db.SaveChangesAsync();
        return n;
    }

    public async Task<DeleteResult> DeleteAsync(string id)
    {
        var n = await db.Notes.FindAsync(id) ?? throw new AppException("Note not found.", 404);
        var result = trash.MarkDeleted("note", $"Note \"{TaskService.Shorten(DisplayTitle(n))}\"", [n]);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Full-text search over title, body and tags. Uses the SQLite FTS5 "trigram" tokenizer, which matches
    /// any 3+ character substring, so it works for English and Chinese. Shorter queries fall back to LIKE.
    /// </summary>
    public async Task<List<NoteHit>> SearchAsync(string query, string? projectId)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        projectId = Text.Clean(projectId);

        if (q.Length < 3)
        {
            var like = await db.Notes
                .Where(n => projectId == null || n.ProjectId == projectId)
                .Where(n => EF.Functions.Like(n.Title, $"%{q}%") || EF.Functions.Like(n.Body, $"%{q}%") || EF.Functions.Like(n.Tags ?? "", $"%{q}%"))
                .OrderByDescending(n => n.NoteDate).Take(50)
                .Select(n => new { n.Id, n.Body }).ToListAsync();
            return like.Select(n => new NoteHit(n.Id, Snip(n.Body, q))).ToList();
        }

        var match = "\"" + q.Replace("\"", "\"\"") + "\"";
        return await db.Database.SqlQuery<NoteHit>($"""
            SELECT n.Id AS Id, snippet(NotesFts, -1, char(1), char(2), '…', 16) AS Snippet
            FROM NotesFts f JOIN Notes n ON n.rowid = f.rowid
            WHERE NotesFts MATCH {match} AND n.DeletedAt IS NULL AND ({projectId} IS NULL OR n.ProjectId = {projectId})
            ORDER BY f.rank LIMIT 50
            """).ToListAsync();
    }

    public static string DisplayTitle(Note n) =>
        n.Title.Length > 0 ? n.Title : n.Body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "Untitled note";

    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;
        var list = tags.Split([',', '，', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", list);
    }

    private static string Snip(string body, string q)
    {
        var i = body.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return body.Length > 120 ? body[..120] + "…" : body;
        var start = Math.Max(0, i - 40);
        var end = Math.Min(body.Length, i + q.Length + 60);
        return (start > 0 ? "…" : "") + body[start..i] + "\u0001" + body.Substring(i, q.Length) + "\u0002" + body[(i + q.Length)..end] + (end < body.Length ? "…" : "");
    }
}
