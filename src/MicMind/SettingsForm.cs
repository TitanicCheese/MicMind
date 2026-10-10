using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace MicMind;

internal sealed class SettingsForm : Form
{
    private readonly AudioRelay relay;
    private readonly AppSettings original;
    private readonly ComboBox sourceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly ComboBox outputBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly CheckBox keepDefaultBox = new() { Text = "Håll den virtuella mikrofonen som Windows standardmikrofon", AutoSize = true };
    private readonly CheckBox autostartBox = new() { Text = "Starta MicMind automatiskt när Windows startar", AutoSize = true };
    private readonly Label statusLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly ProgressBar levelBar = new() { Width = 420, Height = 12, Maximum = 100 };
    private readonly LinkLabel cableMissing = new()
    {
        Text = "Ingen virtuell kabel hittades. Installera gratis VB-CABLE från vb-audio.com/Cable och starta om datorn.",
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = Color.Firebrick,
        Visible = false,
    };
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 50 };

    public SettingsForm(AppSettings current, AudioRelay relay)
    {
        this.relay = relay;
        original = current.Clone();

        Text = "MicMind – Inställningar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };

        layout.Controls.Add(Heading("1. Mikrofon att komma ihåg (t.ex. Blue Snowball)"));
        layout.Controls.Add(sourceBox);
        layout.Controls.Add(Heading("2. Virtuell kabel som ljudet skickas till (t.ex. CABLE Input)"));
        layout.Controls.Add(outputBox);
        layout.Controls.Add(cableMissing);
        layout.Controls.Add(Spacer());
        layout.Controls.Add(keepDefaultBox);
        layout.Controls.Add(autostartBox);
        layout.Controls.Add(Spacer());
        layout.Controls.Add(Heading("Status"));
        layout.Controls.Add(statusLabel);
        layout.Controls.Add(levelBar);
        layout.Controls.Add(Spacer());

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var save = new Button { Text = "Spara", AutoSize = true };
        var refresh = new Button { Text = "Uppdatera listor", AutoSize = true };
        var makeDefault = new Button { Text = "Gör virtuell mikrofon till standard nu", AutoSize = true };
        var close = new Button { Text = "Stäng", AutoSize = true };
        save.Click += (_, _) => SaveSettings();
        refresh.Click += (_, _) => LoadDevices(Selected(sourceBox), Selected(outputBox));
        makeDefault.Click += (_, _) => MakeVirtualMicDefault();
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange(new Control[] { save, refresh, makeDefault, close });
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = close;

        cableMissing.LinkClicked += (_, _) => OpenUrl("https://vb-audio.com/Cable/");
        keepDefaultBox.Checked = current.KeepVirtualMicDefault;
        autostartBox.Checked = Autostart.IsEnabled;

        meterTimer.Tick += (_, _) => levelBar.Value = (int)Math.Clamp(Math.Sqrt(relay.Level) * 100, 0, 100);
        meterTimer.Start();
        FormClosed += (_, _) => meterTimer.Dispose();

        LoadDevices(null, null);
        UpdateStatus();
    }

    public event EventHandler<AppSettings>? Saved;

    public void UpdateStatus() => statusLabel.Text = relay.StatusText;

    private void LoadDevices(DeviceItem? keepSource, DeviceItem? keepOutput)
    {
        using var enumerator = new MMDeviceEnumerator();
        var captures = List(enumerator, DataFlow.Capture).Where(d => !Devices.LooksLikeVirtualCable(d.Name + d.DeviceName)).ToList();
        var renders = List(enumerator, DataFlow.Render);

        var wantedSource = keepSource ?? FromSettings(original.SourceId, original.SourceName, original.SourceDeviceName)
            ?? captures.FirstOrDefault(d => d.Name.Contains("Snowball", StringComparison.OrdinalIgnoreCase));
        var wantedOutput = keepOutput ?? FromSettings(original.OutputId, original.OutputName, original.OutputDeviceName)
            ?? renders.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
            ?? renders.FirstOrDefault(d => Devices.LooksLikeVirtualCable(d.Name + d.DeviceName));

        Fill(sourceBox, captures, wantedSource);
        Fill(outputBox, renders, wantedOutput);
        cableMissing.Visible = !renders.Any(d => Devices.LooksLikeVirtualCable(d.Name + d.DeviceName));
    }

    private static List<DeviceItem> List(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        var result = new List<DeviceItem>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (d)
                result.Add(new DeviceItem(d.ID, d.FriendlyName, d.DeviceFriendlyName, true));
        }
        return result;
    }

    private static DeviceItem? FromSettings(string? id, string? name, string? deviceName) =>
        string.IsNullOrEmpty(name) ? null : new DeviceItem(id, name, deviceName, false);

    /// <summary>Fills a list; a remembered device that is unplugged right now is still shown.</summary>
    private static void Fill(ComboBox box, List<DeviceItem> items, DeviceItem? wanted)
    {
        box.Items.Clear();
        foreach (var item in items)
            box.Items.Add(item);

        if (wanted == null)
            return;
        var match = items.FirstOrDefault(i => i.Id == wanted.Id) ?? items.FirstOrDefault(i => i.Name == wanted.Name);
        if (match == null)
        {
            match = wanted with { Connected = false };
            box.Items.Insert(0, match);
        }
        box.SelectedItem = match;
    }

    private static DeviceItem? Selected(ComboBox box) => box.SelectedItem as DeviceItem;

    private void SaveSettings()
    {
        var source = Selected(sourceBox);
        var output = Selected(outputBox);
        if (source == null || output == null)
        {
            MessageBox.Show(this, "Välj både en mikrofon och en virtuell kabel.", "MicMind",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var result = new AppSettings
        {
            SourceId = source.Id,
            SourceName = source.Name,
            SourceDeviceName = source.DeviceName,
            OutputId = output.Id,
            OutputName = output.Name,
            OutputDeviceName = output.DeviceName,
            KeepVirtualMicDefault = keepDefaultBox.Checked,
        };

        try
        {
            Autostart.Set(autostartBox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kunde inte ändra autostart: " + ex.Message, "MicMind",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Saved?.Invoke(this, result);
        original.SourceId = result.SourceId;
        original.SourceName = result.SourceName;
        original.SourceDeviceName = result.SourceDeviceName;
        original.OutputId = result.OutputId;
        original.OutputName = result.OutputName;
        original.OutputDeviceName = result.OutputDeviceName;
    }

    private void MakeVirtualMicDefault()
    {
        var output = Selected(outputBox);
        using var enumerator = new MMDeviceEnumerator();
        using var mic = Devices.FindVirtualMic(enumerator, output?.DeviceName);
        if (mic == null)
        {
            MessageBox.Show(this, "Hittade ingen inspelningsenhet som hör till den valda virtuella kabeln.", "MicMind",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool ok = DefaultDevice.TrySetDefault(mic.ID);
        MessageBox.Show(this,
            ok ? $"\"{mic.FriendlyName}\" är nu standardmikrofon i Windows."
               : "Kunde inte ändra standardmikrofon. Gör det manuellt under Ljudinställningar → Inspelning.",
            "MicMind", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(0, 6, 0, 2) };

    private static Control Spacer() => new Panel { Height = 6, Width = 1 };

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private sealed record DeviceItem(string? Id, string Name, string? DeviceName, bool Connected)
    {
        public override string ToString() => Connected ? Name : Name + "  (frånkopplad just nu)";
    }
}
