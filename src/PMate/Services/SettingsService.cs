using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;

namespace PMate.Services;

public static class SettingKeys
{
    public const string Me = "me", HoursPerDay = "hoursPerDay", BackupDir = "backupDir";
    public const string RemindersEnabled = "remindersEnabled", MorningReminder = "morningReminder", EveningReminder = "eveningReminder";
}

public record AppSettings(
    string? Me,
    double HoursPerDay,
    string? BackupDir,
    bool RemindersEnabled = true,
    string MorningReminder = "09:00",
    string EveningReminder = "17:30");

public class SettingsService(AppDbContext db)
{
    public async Task<AppSettings> GetAsync()
    {
        var d = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        var hours = double.TryParse(d.GetValueOrDefault(SettingKeys.HoursPerDay), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 8;
        return new AppSettings(
            d.GetValueOrDefault(SettingKeys.Me) ?? "",
            hours,
            Text.Clean(d.GetValueOrDefault(SettingKeys.BackupDir)),
            d.GetValueOrDefault(SettingKeys.RemindersEnabled) != "false",
            ValidTime(d.GetValueOrDefault(SettingKeys.MorningReminder)) ?? "09:00",
            ValidTime(d.GetValueOrDefault(SettingKeys.EveningReminder)) ?? "17:30");
    }

    public async Task<AppSettings> SaveAsync(AppSettings input)
    {
        if (input.HoursPerDay is <= 0 or > 24) throw new AppException("Hours per day must be between 0 and 24.");
        var backupDir = Text.Clean(input.BackupDir);
        if (backupDir is not null && !Path.IsPathFullyQualified(backupDir)) throw new AppException("Backup folder must be a full path, for example C:\\Users\\me\\OneDrive\\PM Backup.");
        var morning = ValidTime(input.MorningReminder) ?? throw new AppException("Morning reminder time must look like 09:00.");
        var evening = ValidTime(input.EveningReminder) ?? throw new AppException("Evening reminder time must look like 17:30.");
        if (string.CompareOrdinal(morning, evening) >= 0) throw new AppException("The morning reminder must be earlier than the evening reminder.");

        // "Me" is from before sign-in existed. Now the signed-in user's display name is used. Kept only for old backups.
        if (input.Me is not null) await SetAsync(SettingKeys.Me, input.Me.Trim());
        await SetAsync(SettingKeys.HoursPerDay, input.HoursPerDay.ToString(CultureInfo.InvariantCulture));
        await SetAsync(SettingKeys.BackupDir, backupDir);
        await SetAsync(SettingKeys.RemindersEnabled, input.RemindersEnabled ? "true" : "false");
        await SetAsync(SettingKeys.MorningReminder, morning);
        await SetAsync(SettingKeys.EveningReminder, evening);
        await db.SaveChangesAsync();
        return await GetAsync();
    }

    /// <summary>Reads an internal value that is not part of <see cref="AppSettings"/>.</summary>
    public async Task<string?> GetValueAsync(string key) => (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetValueAsync(string key, string? value)
    {
        await SetAsync(key, value);
        await db.SaveChangesAsync();
    }

    private async Task SetAsync(string key, string? value)
    {
        var e = await db.Settings.FindAsync(key);
        if (e is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else e.Value = value;
    }

    private static string? ValidTime(string? s) =>
        TimeOnly.TryParseExact(s?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
}
