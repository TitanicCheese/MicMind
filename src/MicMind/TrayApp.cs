using System.Drawing.Drawing2D;

namespace MicMind;

/// <summary>Runs in the notification area; there is no main window.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly AppSettings settings;
    private readonly AudioRelay relay = new();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem statusItem;
    private readonly SynchronizationContext ui;
    private readonly Icon iconRunning = MakeIcon(Color.FromArgb(46, 160, 67));
    private readonly Icon iconWaiting = MakeIcon(Color.FromArgb(230, 150, 20));
    private readonly Icon iconProblem = MakeIcon(Color.FromArgb(210, 50, 50));
    private SettingsForm? settingsForm;

    public TrayApp()
    {
        statusItem = new ToolStripMenuItem("Startar…") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Inställningar…", null, (_, _) => ShowSettings());
        menu.Items.Add("Avsluta", null, (_, _) => ExitThread());

        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        tray = new NotifyIcon
        {
            Icon = iconWaiting,
            Text = "MicMind",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        settings = AppSettings.Load();
        relay.StateChanged += (_, _) => ui.Post(_ => UpdateTray(), null);
        relay.Start(settings);

        if (!settings.IsComplete)
            ShowSettings();
    }

    private void ShowSettings()
    {
        if (settingsForm != null)
        {
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(settings, relay);
        settingsForm.Saved += (_, newSettings) =>
        {
            CopyInto(newSettings, settings);
            settings.Save();
            relay.Apply(settings);
        };
        settingsForm.FormClosed += (_, _) => settingsForm = null;
        settingsForm.Show();
        settingsForm.Activate();
    }

    private void UpdateTray()
    {
        tray.Icon = relay.State switch
        {
            RelayState.Running => iconRunning,
            RelayState.WaitingForMic => iconWaiting,
            _ => iconProblem,
        };
        string text = "MicMind – " + relay.StatusText;
        tray.Text = text.Length > 63 ? text[..62] + "…" : text;
        statusItem.Text = relay.StatusText;
        settingsForm?.UpdateStatus();
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        relay.Dispose();
        base.ExitThreadCore();
    }

    private static void CopyInto(AppSettings from, AppSettings to)
    {
        to.SourceId = from.SourceId;
        to.SourceName = from.SourceName;
        to.SourceDeviceName = from.SourceDeviceName;
        to.OutputId = from.OutputId;
        to.OutputName = from.OutputName;
        to.OutputDeviceName = from.OutputDeviceName;
        to.KeepVirtualMicDefault = from.KeepVirtualMicDefault;
    }

    /// <summary>Draws a small microphone in the given status colour.</summary>
    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 3);
            using var head = new GraphicsPath();
            head.AddArc(10, 2, 12, 12, 180, 180);
            head.AddArc(10, 10, 12, 12, 0, 180);
            head.CloseFigure();
            g.FillPath(brush, head);
            g.DrawArc(pen, 6, 8, 20, 18, 0, 180);
            g.DrawLine(pen, 16, 26, 16, 30);
            g.DrawLine(pen, 10, 30, 22, 30);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
