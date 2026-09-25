namespace PMate.Services;

/// <summary>An error the user can fix. The API returns it as { error: message } with the given status code.</summary>
public class AppException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Who is making the change: the signed-in user (see <c>PMate.Auth.HttpCurrentUser</c>). Null in background jobs.</summary>
public interface ICurrentUser
{
    string? Id { get; }
    /// <summary>Display name. Stored in CreatedBy fields.</summary>
    string? Name { get; }
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
