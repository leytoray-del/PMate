using System.Globalization;
using PMate.Services;

namespace PMate.Infrastructure;

/// <summary>Shows a desktop notification. On Windows this is the tray icon; elsewhere nothing is shown.</summary>
public interface INotifier
{
    bool IsAvailable { get; }
    void Notify(string title, string message);
}

public sealed class NullNotifier(ILogger<NullNotifier> log) : INotifier
{
    public bool IsAvailable => false;
    public void Notify(string title, string message) => log.LogInformation("Reminder (not shown): {Title} - {Message}", title, message);
}

/// <summary>
/// Sends the morning and evening reminders on workdays, once per day each.
/// If the computer starts after the reminder time, the reminder is sent at start-up
/// (the morning one only until the evening time).
/// </summary>
public sealed class ReminderService(IServiceScopeFactory scopes, INotifier notifier, ILogger<ReminderService> log) : BackgroundService
{
    public const string MorningKey = "reminder.morning.last", EveningKey = "reminder.evening.last";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try { await CheckAsync(DateTime.Now); }
                catch (Exception ex) { log.LogWarning(ex, "Reminder check failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task CheckAsync(DateTime now)
    {
        if (!notifier.IsAvailable) return;
        await using var scope = scopes.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var s = await settings.GetAsync();
        if (!s.RemindersEnabled || now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;

        var today = DateOnly.FromDateTime(now);
        var todayText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = TimeOnly.FromDateTime(now);
        var morning = TimeOnly.ParseExact(s.MorningReminder, "HH:mm", CultureInfo.InvariantCulture);
        var evening = TimeOnly.ParseExact(s.EveningReminder, "HH:mm", CultureInfo.InvariantCulture);
        var builder = scope.ServiceProvider.GetRequiredService<ReminderBuilder>();

        if (time >= morning && time < evening && await settings.GetValueAsync(MorningKey) != todayText)
        {
            await settings.SetValueAsync(MorningKey, todayText);
            if (await builder.MorningAsync(today) is { } m) notifier.Notify(m.Title, m.Text);
        }
        if (time >= evening && await settings.GetValueAsync(EveningKey) != todayText)
        {
            await settings.SetValueAsync(EveningKey, todayText);
            if (await builder.EveningAsync(today) is { } m) notifier.Notify(m.Title, m.Text);
        }
    }
}
