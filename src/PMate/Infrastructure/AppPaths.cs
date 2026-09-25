namespace PMate.Infrastructure;

/// <summary>
/// Where the app keeps its files. Data lives outside the git repository,
/// so "git pull" never touches it and company data is never committed.
/// </summary>
public sealed class AppPaths
{
    public required string DataDir { get; init; }
    public required string DatabaseFile { get; init; }
    public required string BackupDir { get; init; }
    public required string LogDir { get; init; }
    /// <summary>The git working copy this app runs from, or null when it is not run from a clone.</summary>
    public string? RepoRoot { get; init; }

    public static AppPaths Create(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:DataDir"];
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(localAppData, "PMate")
            : Path.GetFullPath(configured, env.ContentRootPath);
        MoveDataFromOldName(string.IsNullOrWhiteSpace(configured) ? Path.Combine(localAppData, "PmAssistant") : null, dataDir);

        var paths = new AppPaths
        {
            DataDir = dataDir,
            DatabaseFile = Path.Combine(dataDir, "pmate.db"),
            BackupDir = Path.Combine(dataDir, "backups"),
            LogDir = Path.Combine(dataDir, "logs"),
            RepoRoot = FindRepoRoot(env.ContentRootPath),
        };
        Directory.CreateDirectory(paths.DataDir);
        Directory.CreateDirectory(paths.BackupDir);
        Directory.CreateDirectory(paths.LogDir);
        return paths;
    }

    /// <summary>
    /// The app was called "PM Assistant" before it was renamed to PMate.
    /// This moves its data folder, database and backups to the new names, once.
    /// </summary>
    public static void MoveDataFromOldName(string? oldDataDir, string dataDir)
    {
        try
        {
            // The launcher may already have created the new folder (for its logs), so check for the database, not the folder.
            if (oldDataDir is not null && Directory.Exists(oldDataDir) && !File.Exists(Path.Combine(dataDir, "pmate.db")))
            {
                Directory.CreateDirectory(dataDir);
                MoveContents(oldDataDir, dataDir);
                if (!Directory.EnumerateFileSystemEntries(oldDataDir).Any()) Directory.Delete(oldDataDir);
            }
            if (!Directory.Exists(dataDir)) return;

            if (File.Exists(Path.Combine(dataDir, "pm-assistant.db")) && !File.Exists(Path.Combine(dataDir, "pmate.db")))
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    var from = Path.Combine(dataDir, "pm-assistant.db" + suffix);
                    if (File.Exists(from)) File.Move(from, Path.Combine(dataDir, "pmate.db" + suffix));
                }
            }

            var backups = Path.Combine(dataDir, "backups");
            if (Directory.Exists(backups))
            {
                foreach (var f in Directory.GetFiles(backups, "pm-assistant-*.db"))
                {
                    var to = Path.Combine(backups, "pmate-" + Path.GetFileName(f)["pm-assistant-".Length..]);
                    if (!File.Exists(to)) File.Move(f, to);
                }
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Could not move the old PM Assistant data to {dataDir}. Close any running copy of the old app and start again.", ex);
        }
    }

    /// <summary>Moves files and folders that do not exist at the target yet. Files that exist at both places stay where they are.</summary>
    private static void MoveContents(string from, string to)
    {
        foreach (var f in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(f));
            if (!File.Exists(target)) File.Move(f, target);
        }
        foreach (var d in Directory.GetDirectories(from))
        {
            var target = Path.Combine(to, Path.GetFileName(d));
            if (!Directory.Exists(target)) { Directory.Move(d, target); continue; }
            MoveContents(d, target);
            if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
    }

    private static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        }
        return null;
    }
}
