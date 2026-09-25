using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PMate.Data;
using PMate.Domain;
using PMate.Infrastructure;
using PMate.Services;

namespace PMate.Auth;

public static class AuthSetup
{
    public const string AdminPolicy = "admin";
    public const string StampClaim = "pmate:stamp";
    public const string MustChangeClaim = "pmate:must-change-password";
    public const string UserNameClaim = "pmate:username";

    /// <summary>"Remember me" keeps the sign-in for this long. Without it, the sign-in ends when the browser closes.</summary>
    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(30);

    public static void AddPMateAuth(this IServiceCollection services, AppPaths paths)
    {
        // Keys that protect the sign-in cookie. Kept in the data folder, so sign-ins survive restarts and updates.
        var dp = services.AddDataProtection().SetApplicationName("PMate")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.DataDir, "keys")));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<UserService>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "pmate.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = RememberFor;
            o.SlidingExpiration = true;
            // This is an API: answer 401/403 instead of redirecting.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            o.Events.OnValidatePrincipal = ValidateAsync;
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, p => p.RequireRole(Roles.Admin));
    }

    public static ClaimsPrincipal CreatePrincipal(User u) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, u.Id),
        new Claim(ClaimTypes.Name, u.DisplayName),
        new Claim(UserNameClaim, u.UserName),
        new Claim(ClaimTypes.Role, u.Role),
        new Claim(StampClaim, u.SecurityStamp),
        new Claim(MustChangeClaim, u.MustChangePassword ? "true" : "false"),
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task SignInAsync(HttpContext ctx, User u, bool remember) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreatePrincipal(u), new AuthenticationProperties { IsPersistent = remember });

    /// <summary>Signs in again with fresh claims, keeping the "remember me" choice. Used after a password or name change.</summary>
    public static async Task RefreshSignInAsync(HttpContext ctx, User u)
    {
        var current = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await SignInAsync(ctx, u, current.Properties?.IsPersistent ?? false);
    }

    /// <summary>
    /// Runs on every request with a sign-in cookie. Rejects the cookie when the user was disabled or the
    /// password or role changed since sign-in. Keeps name, role and "must change password" up to date.
    /// </summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var u = id is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (principal is null || u is null || !u.IsActive || u.SecurityStamp != principal.FindFirstValue(StampClaim))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var fresh = CreatePrincipal(u);
        if (fresh.FindFirstValue(ClaimTypes.Name) != principal.FindFirstValue(ClaimTypes.Name) ||
            fresh.FindFirstValue(MustChangeClaim) != principal.FindFirstValue(MustChangeClaim))
        {
            ctx.ReplacePrincipal(fresh);
            ctx.ShouldRenew = true;
        }
    }

    /// <summary>
    /// Page and API rules that sit outside the endpoint policies:
    /// the app page needs a sign-in (otherwise go to the login page), and a user who must change
    /// their password can only use the account endpoints until they do.
    /// </summary>
    public static IApplicationBuilder UsePMatePageRules(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        var signedIn = ctx.User.Identity?.IsAuthenticated == true;
        if (!signedIn && (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.Redirect("/login.html");
            return;
        }
        if (signedIn && ctx.User.FindFirstValue(MustChangeClaim) == "true" &&
            path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth") && !path.StartsWithSegments("/api/health"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { error = "Please choose a new password first." });
            return;
        }
        await next();
    });
}

/// <summary>The signed-in user of the current request. Null in background jobs.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;
    public string? Id => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Name => Principal?.FindFirstValue(ClaimTypes.Name);
}
