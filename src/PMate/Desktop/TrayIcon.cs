using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PMate.Infrastructure;

namespace PMate.Desktop;

/// <summary>
/// Icon in the Windows notification area. Shows reminders as Windows notifications,
/// and has "Open" and "Exit" in its menu. Runs its own message loop on a separate thread.
/// This folder is only compiled on Windows (see the .csproj).
/// </summary>
public sealed class TrayIcon(IConfiguration config, IHostApplicationLifetime lifetime, ILogger<TrayIcon> log) : IHostedService, INotifier
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? _ui;
    private NotifyIcon? _icon;

    public bool IsAvailable => _icon is not null;
    private string Url => (config["Urls"] ?? "http://localhost:5178").Split(';')[0];

    public Task StartAsync(CancellationToken ct)
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "TrayIcon" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return Task.WhenAny(_ready.Task, Task.Delay(5000, ct));
    }

    public Task StopAsync(CancellationToken ct)
    {
        _ui?.Send(_ =>
        {
            if (_icon is not null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            Application.ExitThread();
        }, null);
        return Task.CompletedTask;
    }

    public void Notify(string title, string message) =>
        _ui?.Post(_ => _icon?.ShowBalloonTip(15000, title, message, ToolTipIcon.Info), null);

    public void OpenApp()
    {
        try { Process.Start(new ProcessStartInfo("msedge.exe", $"--app={Url}") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
    }

    private void Run()
    {
        try
        {
            _ui = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_ui);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open PMate", null, (_, _) => OpenApp());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => lifetime.StopApplication());
            _icon = new NotifyIcon { Icon = CreateIcon(), Text = "PMate", Visible = true, ContextMenuStrip = menu };
            _icon.DoubleClick += (_, _) => OpenApp();
            _icon.BalloonTipClicked += (_, _) => OpenApp();
            _ready.TrySetResult();
            Application.Run();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Tray icon could not start. Reminders are turned off.");
            _icon = null;
            _ready.TrySetResult();
        }
    }

    /// <summary>Draws the app icon in code, so no .ico file is needed.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(37, 99, 235));
            using var box = new GraphicsPath();
            const int r = 14;
            box.AddArc(0, 0, r, r, 180, 90);
            box.AddArc(31 - r, 0, r, r, 270, 90);
            box.AddArc(31 - r, 31 - r, r, r, 0, 90);
            box.AddArc(0, 31 - r, r, r, 90, 90);
            box.CloseFigure();
            g.FillPath(bg, box);
            using var pen = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 8, 11, 24, 11);
            g.DrawLine(pen, 8, 16, 19, 16);
            g.DrawLine(pen, 8, 21, 15, 21);
            using var green = new SolidBrush(Color.FromArgb(74, 222, 128));
            g.FillEllipse(green, 20, 18, 7, 7);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
