using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MicMind;

internal enum RelayState
{
    NotConfigured,
    OutputMissing,
    WaitingForMic,
    Running,
}

/// <summary>
/// Copies audio from the chosen microphone to the playback side of a virtual cable.
/// The cable keeps playing (silence) while the microphone is unplugged, so Windows and
/// every app keep seeing the same microphone. All device work runs on one background
/// thread that reacts to Windows device notifications and also polls every two seconds.
/// </summary>
internal sealed class AudioRelay : IDisposable
{
    private const int MaxBufferedMs = 200;

    private readonly AutoResetEvent wake = new(false);
    private readonly Thread worker;
    private readonly object sync = new();
    private volatile bool stopping;

    // Guarded by sync.
    private AppSettings settings = new();
    private bool restartRequested;

    // Owned by the worker thread.
    private MMDeviceEnumerator? enumerator;
    private DeviceNotifications? notifications;
    private MMDevice? outputDevice;
    private WasapiOut? output;
    private BufferedWaveProvider? buffer;
    private MMDevice? captureDevice;
    private WasapiCapture? capture;
    private volatile bool outputFailed;
    private volatile bool captureFailed;

    private volatile float level;

    public AudioRelay()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "MicMind audio" };
    }

    public event EventHandler? StateChanged;

    public RelayState State { get; private set; } = RelayState.NotConfigured;
    public string StatusText { get; private set; } = "Startar…";

    /// <summary>Current microphone level, 0..1.</summary>
    public float Level => level;

    public void Start(AppSettings initial)
    {
        Apply(initial);
        worker.Start();
    }

    public void Apply(AppSettings newSettings)
    {
        lock (sync)
        {
            settings = newSettings.Clone();
            restartRequested = true;
        }
        wake.Set();
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        if (worker.IsAlive)
            worker.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        enumerator = new MMDeviceEnumerator();
        notifications = new DeviceNotifications(this);
        enumerator.RegisterEndpointNotificationCallback(notifications);

        while (!stopping)
        {
            try
            {
                Reconcile();
            }
            catch (Exception ex)
            {
                // Never let a device hiccup kill the worker; tear down and retry.
                StopCapture();
                StopOutput();
                SetStatus(RelayState.OutputMissing, "Fel: " + ex.Message);
            }
            wake.WaitOne(TimeSpan.FromSeconds(2));
        }

        StopCapture();
        StopOutput();
        enumerator.UnregisterEndpointNotificationCallback(notifications);
        enumerator.Dispose();
    }

    private void Reconcile()
    {
        AppSettings current;
        bool restart;
        lock (sync)
        {
            current = settings;
            restart = restartRequested;
            restartRequested = false;
        }

        if (restart || outputFailed || !IsActive(outputDevice))
        {
            StopCapture();
            StopOutput();
        }
        if (captureFailed || !IsActive(captureDevice))
            StopCapture();

        if (!current.IsComplete)
        {
            SetStatus(RelayState.NotConfigured, "Inte inställt – öppna Inställningar");
            return;
        }

        if (output == null && !StartOutput(current))
        {
            SetStatus(RelayState.OutputMissing, $"Hittar inte den virtuella kabeln \"{current.OutputName}\"");
            return;
        }

        if (current.KeepVirtualMicDefault)
            KeepVirtualMicDefault();

        if (capture == null && !StartCapture(current))
        {
            SetStatus(RelayState.WaitingForMic, $"Väntar på {current.SourceName} – ansluts automatiskt");
            return;
        }

        SetStatus(RelayState.Running, $"Aktiv: {captureDevice!.FriendlyName}");
    }

    private bool StartOutput(AppSettings s)
    {
        var device = Devices.Find(enumerator!, DataFlow.Render, s.OutputId, s.OutputName, s.OutputDeviceName);
        if (device == null)
            return false;

        try
        {
            WaveFormat mix;
            using (var client = device.AudioClient)
                mix = client.MixFormat;

            buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels))
            {
                BufferDuration = TimeSpan.FromSeconds(1),
                DiscardOnBufferOverflow = true,
                ReadFully = true, // silence when the microphone is gone
            };
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            output.PlaybackStopped += (_, _) => { if (!stopping) { outputFailed = true; wake.Set(); } };
            output.Init(buffer);
            output.Play();
            outputDevice = device;
            outputFailed = false;
            return true;
        }
        catch
        {
            output?.Dispose();
            output = null;
            buffer = null;
            device.Dispose();
            return false;
        }
    }

    private bool StartCapture(AppSettings s)
    {
        var device = Devices.Find(enumerator!, DataFlow.Capture, s.SourceId, s.SourceName, s.SourceDeviceName);
        if (device == null)
            return false;

        try
        {
            var newCapture = new WasapiCapture(device, true, 20);
            var converter = new SampleConverter(newCapture.WaveFormat, buffer!.WaveFormat);
            var target = buffer;
            byte[] bytes = Array.Empty<byte>();

            newCapture.DataAvailable += (_, e) =>
            {
                int count = converter.Convert(e.Buffer, e.BytesRecorded, out var samples);
                int byteCount = count * sizeof(float);
                if (bytes.Length < byteCount)
                    bytes = new byte[byteCount];
                Buffer.BlockCopy(samples, 0, bytes, 0, byteCount);

                // The two devices run on separate clocks; drop backlog instead of drifting into lag.
                if (target.BufferedDuration.TotalMilliseconds > MaxBufferedMs)
                    target.ClearBuffer();
                target.AddSamples(bytes, 0, byteCount);

                level = Math.Max(converter.Peak, level * 0.8f);
            };
            newCapture.RecordingStopped += (_, _) => { if (!stopping) { captureFailed = true; wake.Set(); } };

            newCapture.StartRecording();
            capture = newCapture;
            captureDevice = device;
            captureFailed = false;
            return true;
        }
        catch
        {
            capture?.Dispose();
            capture = null;
            device.Dispose();
            return false;
        }
    }

    private void StopCapture()
    {
        if (capture != null)
        {
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
            capture = null;
        }
        captureDevice?.Dispose();
        captureDevice = null;
        captureFailed = false;
        level = 0;
        buffer?.ClearBuffer();
    }

    private void StopOutput()
    {
        if (output != null)
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
            output = null;
        }
        outputDevice?.Dispose();
        outputDevice = null;
        buffer = null;
        outputFailed = false;
    }

    private void KeepVirtualMicDefault()
    {
        using var virtualMic = Devices.FindVirtualMic(enumerator!, outputDevice?.DeviceFriendlyName);
        if (virtualMic == null)
            return;

        if (Devices.DefaultCaptureId(enumerator!, Role.Console) != virtualMic.ID ||
            Devices.DefaultCaptureId(enumerator!, Role.Communications) != virtualMic.ID)
        {
            DefaultDevice.TrySetDefault(virtualMic.ID);
        }
    }

    private static bool IsActive(MMDevice? device)
    {
        if (device == null)
            return true; // nothing to check
        try
        {
            return device.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    private void SetStatus(RelayState state, string text)
    {
        if (state == State && text == StatusText)
            return;
        State = state;
        StatusText = text;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Windows calls these on its own threads; just wake the worker.</summary>
    private sealed class DeviceNotifications : IMMNotificationClient
    {
        private readonly AudioRelay owner;

        public DeviceNotifications(AudioRelay owner) => this.owner = owner;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.wake.Set();
        public void OnDeviceAdded(string pwstrDeviceId) => owner.wake.Set();
        public void OnDeviceRemoved(string deviceId) => owner.wake.Set();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => owner.wake.Set();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
