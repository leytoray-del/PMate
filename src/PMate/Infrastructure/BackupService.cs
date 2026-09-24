using Microsoft.Data.Sqlite;
using PMate.Services;

namespace PMate.Infrastructure;

/// <summary>
/// Makes one database backup per day, into the local backup folder and, if set, a second folder such as OneDrive.
/// Uses the SQLite backup API, so the copy is consistent even while the app is running.
/// The live database should stay on the local disk: a sync tool like OneDrive can damage an open SQLite file.
/// </summary>
public sealed class BackupService(AppPaths paths, IServiceScopeFactory scopes, IConfiguration config, ILogger<BackupService> log) : BackgroundService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DateTime? LastBackup { get; private set; }
    public string? LastError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                if (!File.Exists(Path.Combine(paths.BackupDir, FileName(DateTime.Now)))) await BackupNowAsync();
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task<IReadOnlyList<string>> BackupNowAsync()
    {
        await _lock.WaitAsync();
        try
        {
            string? extraDir;
            using (var scope = scopes.CreateScope())
            {
                extraDir = (await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync()).BackupDir;
            }
            var written = new List<string>();
            var errors = new List<string>();
            foreach (var dir in new[] { paths.BackupDir, extraDir }.OfType<string>())
            {
                try { written.Add(BackupTo(dir)); }
                catch (Exception ex) { errors.Add($"{dir}: {ex.Message}"); log.LogWarning(ex, "Backup to {Dir} failed", dir); }
            }
            LastError = errors.Count > 0 ? string.Join("; ", errors) : null;
            if (written.Count > 0) LastBackup = DateTime.UtcNow;
            return written;
        }
        finally { _lock.Release(); }
    }

    private string BackupTo(string dir)
    {
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, FileName(DateTime.Now));
        var temp = target + ".tmp";
        using (var source = new SqliteConnection($"Data Source={paths.DatabaseFile};Mode=ReadOnly;Pooling=False"))
        using (var dest = new SqliteConnection($"Data Source={temp};Pooling=False"))
        {
            source.Open();
            source.BackupDatabase(dest);
        }
        File.Move(temp, target, overwrite: true);

        var keep = Math.Max(1, config.GetValue("Backup:KeepCount", 14));
        foreach (var old in new DirectoryInfo(dir).GetFiles("pmate-*.db").OrderByDescending(f => f.Name).Skip(keep))
        {
            old.Delete();
        }
        return target;
    }

    private static string FileName(DateTime day) => $"pmate-{day:yyyy-MM-dd}.db";
}
