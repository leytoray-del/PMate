using System.Diagnostics;

namespace PMate.Infrastructure;

/// <summary>
/// Checks the git remote for new commits so the UI can offer "Update and restart".
/// The update itself is done by scripts/start.ps1, which pulls and restarts the app.
/// </summary>
public sealed class UpdateChecker(AppPaths paths, IConfiguration config, ILogger<UpdateChecker> log) : BackgroundService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool Enabled => paths.RepoRoot is not null && config.GetValue("Updates:Enabled", true);
    public string? Commit { get; private set; }
    public int Behind { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public string? LastError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (paths.RepoRoot is null) return;
        Commit = (await GitAsync("rev-parse --short HEAD", ct)).Output;
        if (!Enabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            var minutes = Math.Max(5, config.GetValue("Updates:CheckIntervalMinutes", 60));
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
            do { await CheckAsync(ct); } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!Enabled) return;
        await _lock.WaitAsync(ct);
        try
        {
            var fetch = await GitAsync("fetch --quiet", ct);
            if (fetch.ExitCode != 0) { LastError = "Could not reach the git remote: " + fetch.Error; return; }
            var count = await GitAsync("rev-list --count HEAD..@{u}", ct);
            if (count.ExitCode != 0) { LastError = "No upstream branch is set: " + count.Error; return; }
            Behind = int.TryParse(count.Output, out var n) ? n : 0;
            LastError = null;
            LastChecked = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            log.LogWarning(ex, "Update check failed");
        }
        finally { _lock.Release(); }
    }

    private async Task<(int ExitCode, string Output, string Error)> GitAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = paths.RepoRoot!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Never block on a credential prompt in the background.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        try
        {
            using var p = Process.Start(psi)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            return (p.ExitCode, (await output).Trim(), (await error).Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (-1, "", ex.Message);
        }
    }
}
