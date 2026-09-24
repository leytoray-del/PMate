using PMate.Data;

namespace PMate.Services;

/// <summary>An error the user can fix. The API returns it as { error: message } with the given status code.</summary>
public class AppException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Who is making the change. Today this is the single local user from Settings.
/// When the app moves to the cloud, replace it with an implementation that reads the signed-in user.
/// </summary>
public interface ICurrentUser
{
    string? Name { get; }
}

public class LocalUser(AppDbContext db) : ICurrentUser
{
    private string? _name;
    private bool _loaded;

    public string? Name
    {
        get
        {
            if (!_loaded) { _name = db.Settings.Find(SettingKeys.Me)?.Value; _loaded = true; }
            return string.IsNullOrWhiteSpace(_name) ? null : _name;
        }
    }
}

public static class Clock
{
    /// <summary>Local calendar date. Cloud mode should take the date from the client instead.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

internal static class Text
{
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
