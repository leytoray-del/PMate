using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PMate.Services;

namespace PMate.Auth;

public static class AuthEndpoints
{
    public static void MapAuthApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        // Used by the login page before anyone is signed in.
        auth.MapGet("/status", async (HttpContext ctx, UserService users) =>
            new { hasUsers = await users.HasUsersAsync(), signedIn = ctx.User.Identity?.IsAuthenticated == true }).AllowAnonymous();

        auth.MapPost("/setup", async (SetupInput input, HttpContext ctx, UserService users) =>
        {
            var u = await users.SetupAsync(input);
            // Data from before users existed belongs to this first admin.
            await Ownership.AssignUnownedAsync(ctx.RequestServices.GetRequiredService<PMate.Data.AppDbContext>());
            await AuthSetup.SignInAsync(ctx, u, remember: false);
            return UserDto.From(u);
        }).AllowAnonymous();

        auth.MapPost("/login", async (LoginInput input, HttpContext ctx, UserService users) =>
        {
            var u = await users.LoginAsync(input);
            await AuthSetup.SignInAsync(ctx, u, input.RememberMe);
            return UserDto.From(u);
        }).AllowAnonymous();

        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapGet("/me", async (ICurrentUser me, UserService users) =>
            UserDto.From(await users.FindAsync(me.Id!) ?? throw new AppException("User not found.", 401)));

        auth.MapPost("/change-password", async (PasswordInput input, HttpContext ctx, ICurrentUser me, UserService users) =>
        {
            var u = await users.ChangePasswordAsync(me.Id!, input.CurrentPassword, input.NewPassword);
            await AuthSetup.RefreshSignInAsync(ctx, u);
            return UserDto.From(u);
        });

        auth.MapPut("/profile", async (ProfileInput input, HttpContext ctx, ICurrentUser me, UserService users) =>
        {
            var u = await users.UpdateProfileAsync(me.Id!, input.DisplayName);
            await AuthSetup.RefreshSignInAsync(ctx, u);
            return UserDto.From(u);
        });

        // User management, admin only
        var admin = app.MapGroup("/api/users").RequireAuthorization(AuthSetup.AdminPolicy);
        admin.MapGet("", (UserService users) => users.ListAsync());
        admin.MapPost("", async (CreateUserInput input, UserService users) => UserDto.From(await users.CreateAsync(input)));
        admin.MapPut("/{id}", async (string id, UpdateUserInput input, ICurrentUser me, UserService users) =>
            UserDto.From(await users.UpdateAsync(id, input, me.Id!)));
        admin.MapPost("/{id}/reset-password", async (string id, PasswordInput input, UserService users) =>
            UserDto.From(await users.ResetPasswordAsync(id, input.NewPassword)));
    }
}
