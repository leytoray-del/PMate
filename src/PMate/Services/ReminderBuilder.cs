using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public record ReminderMessage(string Title, string Text);

/// <summary>Builds the text of the morning and evening reminders. Returns null when there is nothing to say.</summary>
public class ReminderBuilder(AppDbContext db, SettingsService settings)
{
    public async Task<ReminderMessage?> MorningAsync(DateOnly today)
    {
        var doneIds = await db.Statuses.Where(s => s.IsDone).Select(s => s.Id).ToListAsync();
        var open = db.Tasks.Where(t => !doneIds.Contains(t.Status) && t.Due != null);
        var overdue = await open.CountAsync(t => t.Due < today);
        var dueToday = await open.CountAsync(t => t.Due == today);
        var commits = await db.Updates.CountAsync(u => u.Type == UpdateTypes.Commitment && !u.CommitDone && u.CommitDue != null && u.CommitDue <= today);
        var raid = await db.RaidItems.CountAsync(r => r.Status != RaidStatuses.Closed && r.DueDate != null && r.DueDate <= today);

        var parts = new List<string>();
        if (dueToday > 0) parts.Add(Count(dueToday, "task") + " due today");
        if (overdue > 0) parts.Add(Count(overdue, "task") + " overdue");
        if (commits > 0) parts.Add(Count(commits, "commitment") + " due");
        if (raid > 0) parts.Add(Count(raid, "RAID item") + " due");
        return parts.Count == 0 ? null : new ReminderMessage("Your day in PMate", string.Join(" · ", parts));
    }

    public async Task<ReminderMessage?> EveningAsync(DateOnly today)
    {
        var target = (await settings.GetAsync()).HoursPerDay;
        var hours = await db.Updates.Where(u => u.Date == today).SumAsync(u => u.Hours);
        if (hours >= target) return null;
        return new ReminderMessage("Log your time", $"You logged {hours:0.##}h today. Your target is {target:0.##}h.");
    }

    private static string Count(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
}
