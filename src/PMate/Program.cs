using Microsoft.EntityFrameworkCore;
using PMate.Api;
using PMate.Auth;
using PMate.Data;
using PMate.Infrastructure;
using PMate.Services;

var builder = WebApplication.CreateBuilder(args);

var paths = AppPaths.Create(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(paths);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={paths.DatabaseFile}"));

builder.Services.AddPMateAuth(paths);
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<StatusService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<DataService>();
builder.Services.AddScoped<TrashService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<RaidService>();
builder.Services.AddScoped<ReminderBuilder>();

#if DESKTOP
if (builder.Configuration.GetValue("Tray:Enabled", true))
{
    builder.Services.AddSingleton<PMate.Desktop.TrayIcon>();
    builder.Services.AddSingleton<INotifier>(sp => sp.GetRequiredService<PMate.Desktop.TrayIcon>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PMate.Desktop.TrayIcon>());
}
else builder.Services.AddSingleton<INotifier, NullNotifier>();
#else
builder.Services.AddSingleton<INotifier, NullNotifier>();
#endif
builder.Services.AddSingleton<ReminderService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderService>());

builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateChecker>());
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await scope.ServiceProvider.GetRequiredService<StatusService>().EnsureDefaultsAsync();
    await scope.ServiceProvider.GetRequiredService<TrashService>().PurgeAsync();
    // Keeps the full-text index in step with the Notes table, even after a migration rebuilt that table.
    await db.Database.ExecuteSqlRawAsync("INSERT INTO NotesFts(NotesFts) VALUES('rebuild');");
}

app.Use(async (ctx, next) =>
{
    var isApi = ctx.Request.Path.StartsWithSegments("/api");
    if (isApi && !HttpMethods.IsGet(ctx.Request.Method) && !ctx.Request.Headers.ContainsKey(Endpoints.ClientHeader))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "Missing client header." });
        return;
    }
    try
    {
        await next();
    }
    catch (AppException ex) when (!ctx.Response.HasStarted)
    {
        ctx.Response.StatusCode = ex.StatusCode;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (BadHttpRequestException ex) when (!ctx.Response.HasStarted)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = "Invalid request: " + ex.Message });
    }
});

app.UseAuthentication();
app.UsePMatePageRules();
app.UseDefaultFiles();
// Static files (HTML, CSS, JS) hold no data, so they are public. All data comes from the API, which needs a sign-in.
// "no-cache" makes the browser check for a new file on each load, so an update shows up right after restart.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });
app.UseAuthorization();
app.MapAuthApi();
app.MapApi();

app.Logger.LogInformation("Data folder: {DataDir}", paths.DataDir);
app.Run();

public partial class Program;
