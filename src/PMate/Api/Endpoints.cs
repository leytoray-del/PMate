using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PMate.Infrastructure;
using PMate.Services;

namespace PMate.Api;

public record CommitDoneInput(bool Done);

public static class Endpoints
{
    /// <summary>Header the UI sends on every change. Browsers cannot add it cross-site without a CORS preflight, which this app never allows.</summary>
    public const string ClientHeader = "X-PM-Client";

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        api.MapGet("/snapshot", (DataService d) => d.GetSnapshotAsync());

        // Tasks and their timeline records
        // Deletes return { undoId, label }. POST /api/trash/{undoId}/restore undoes them.
        api.MapPost("/tasks", (TaskInput input, TaskService s) => s.CreateAsync(input));
        api.MapPut("/tasks/{id}", (string id, TaskInput input, TaskService s) => s.UpdateAsync(id, input));
        api.MapDelete("/tasks/{id}", (string id, TaskService s) => s.DeleteAsync(id));
        api.MapPost("/updates", (UpdateInput input, TaskService s) => s.AddUpdateAsync(input));
        api.MapPut("/updates/{id}", (string id, UpdateInput input, TaskService s) => s.EditUpdateAsync(id, input));
        api.MapPut("/updates/{id}/commit-done", (string id, CommitDoneInput input, TaskService s) => s.SetCommitDoneAsync(id, input.Done));
        api.MapDelete("/updates/{id}", (string id, TaskService s) => s.DeleteUpdateAsync(id));

        // Projects and milestones
        api.MapPost("/projects", (ProjectInput input, ProjectService s) => s.SaveAsync(null, input));
        api.MapPut("/projects/{id}", (string id, ProjectInput input, ProjectService s) => s.SaveAsync(id, input));
        api.MapDelete("/projects/{id}", (string id, ProjectService s) => s.DeleteAsync(id));
        api.MapPost("/milestones", (MilestoneInput input, ProjectService s) => s.SaveMilestoneAsync(null, input));
        api.MapPut("/milestones/{id}", (string id, MilestoneInput input, ProjectService s) => s.SaveMilestoneAsync(id, input));
        api.MapDelete("/milestones/{id}", (string id, ProjectService s) => s.DeleteMilestoneAsync(id));

        // Work notes and RAID log
        api.MapPost("/notes", (NoteInput input, NoteService s) => s.SaveAsync(null, input));
        api.MapPut("/notes/{id}", (string id, NoteInput input, NoteService s) => s.SaveAsync(id, input));
        api.MapDelete("/notes/{id}", (string id, NoteService s) => s.DeleteAsync(id));
        api.MapGet("/notes/search", (string q, string? projectId, NoteService s) => s.SearchAsync(q, projectId));
        api.MapPost("/raid", (RaidInput input, RaidService s) => s.SaveAsync(null, input));
        api.MapPut("/raid/{id}", (string id, RaidInput input, RaidService s) => s.SaveAsync(id, input));
        api.MapDelete("/raid/{id}", (string id, RaidService s) => s.DeleteAsync(id));

        // Recently deleted
        api.MapGet("/trash", (TrashService s) => s.ListAsync());
        api.MapPost("/trash/{id}/restore", async (string id, TrashService s) => { await s.RestoreAsync(id); return Results.NoContent(); });

        // Board columns and settings
        api.MapPut("/statuses", (List<StatusInput> input, StatusService s) => s.ReplaceAsync(input));
        api.MapGet("/settings", (SettingsService s) => s.GetAsync());
        api.MapPut("/settings", (AppSettings input, SettingsService s) => s.SaveAsync(input));

        // Backup, restore and bulk operations
        api.MapGet("/export", async (DataService d) =>
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(await d.GetSnapshotAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return Results.File(bytes, "application/json", $"pmate-backup-{DateTime.Now:yyyy-MM-dd}.json");
        });
        api.MapPost("/import", (JsonElement body, string mode, DataService d) => mode switch
        {
            "replace" => d.ImportAsync(body, replace: true),
            "merge" => d.ImportAsync(body, replace: false),
            _ => throw new AppException("mode must be 'replace' or 'merge'."),
        });
        api.MapDelete("/sample", async (DataService d) => { await d.RemoveSampleAsync(); return Results.NoContent(); });
        api.MapDelete("/data", async (string confirm, DataService d) =>
        {
            if (confirm != "DELETE") throw new AppException("Confirmation text does not match.");
            await d.ClearAsync();
            return Results.NoContent();
        });

        // App info, updates and backups
        api.MapGet("/system/info", SystemInfo);
        api.MapPost("/system/check-updates", async (UpdateChecker u, AppPaths p, BackupService b, SettingsService s, INotifier n) =>
        {
            await u.CheckAsync();
            return await SystemInfo(u, p, b, s, n);
        });
        api.MapPost("/system/test-reminder", async (INotifier notifier, ReminderBuilder builder) =>
        {
            if (!notifier.IsAvailable) throw new AppException("Desktop notifications are not available. The tray icon is off or not supported here.");
            var m = await builder.MorningAsync(DateOnly.FromDateTime(DateTime.Now))
                ?? new ReminderMessage("PMate", "Reminders are working. Nothing is due today.");
            notifier.Notify(m.Title, m.Text);
            return Results.NoContent();
        });
        api.MapPost("/system/backup", async (BackupService b) =>
        {
            var files = await b.BackupNowAsync();
            if (files.Count == 0) throw new AppException("Backup failed: " + b.LastError, 500);
            return new { files, error = b.LastError };
        });
        api.MapPost("/system/update", (AppPaths paths, UpdateChecker u, IHostApplicationLifetime life) =>
        {
            if (!u.Enabled) throw new AppException("Updates are not available. The app is not running from a git clone.");
            var script = Path.Combine(paths.RepoRoot!, "scripts", "start.ps1");
            if (!File.Exists(script)) throw new AppException("scripts/start.ps1 was not found.");
            // The script waits for this process to exit, pulls the new version, and starts it again.
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Restart -WaitPid {Environment.ProcessId}")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = paths.RepoRoot,
            });
            _ = Task.Run(async () => { await Task.Delay(800); life.StopApplication(); });
            return Results.Accepted();
        });
    }

    private static async Task<object> SystemInfo(UpdateChecker u, AppPaths p, BackupService b, SettingsService s, INotifier n) => new
    {
        notificationsAvailable = n.IsAvailable,
        version = typeof(Endpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],
        commit = u.Commit,
        updatesEnabled = u.Enabled,
        behind = u.Behind,
        lastChecked = u.LastChecked,
        updateError = u.LastError,
        dataDir = p.DataDir,
        databaseFile = p.DatabaseFile,
        backupDir = p.BackupDir,
        extraBackupDir = (await s.GetAsync()).BackupDir,
        lastBackup = b.LastBackup,
        backupError = b.LastError,
    };
}
