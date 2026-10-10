using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace MicMind;

internal static class Devices
{
    /// <summary>
    /// Finds an active endpoint: by id, then by friendly name, then by the name of the
    /// physical device. Windows adds a "2- " prefix when the same model is plugged into
    /// another USB port, so names are compared without it.
    /// </summary>
    public static MMDevice? Find(MMDeviceEnumerator enumerator, DataFlow flow, string? id, string? name, string? deviceName)
    {
        if (!string.IsNullOrEmpty(id))
        {
            try
            {
                var device = enumerator.GetDevice(id);
                if (device.DataFlow == flow && device.State == DeviceState.Active)
                    return device;
                device.Dispose();
            }
            catch (COMException)
            {
                // Unknown id: fall back to name matching.
            }
        }

        var active = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        MMDevice? match = null;
        if (!string.IsNullOrEmpty(name))
            match = active.FirstOrDefault(d => Normalize(d.FriendlyName) == Normalize(name));
        if (match == null && !string.IsNullOrEmpty(deviceName))
            match = active.FirstOrDefault(d => Normalize(d.DeviceFriendlyName) == Normalize(deviceName));

        foreach (var d in active)
            if (d != match) d.Dispose();
        return match;
    }

    /// <summary>
    /// The recording endpoint that belongs to the same virtual device as the given playback
    /// endpoint, e.g. "CABLE Output" for "CABLE Input".
    /// </summary>
    public static MMDevice? FindVirtualMic(MMDeviceEnumerator enumerator, string? outputDeviceName)
    {
        if (string.IsNullOrEmpty(outputDeviceName))
            return null;
        return Find(enumerator, DataFlow.Capture, null, null, outputDeviceName);
    }

    public static string? DefaultCaptureId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
            return device.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static bool LooksLikeVirtualCable(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string s) =>
        Regex.Replace(Regex.Replace(s.Trim(), @"^\d+-\s*", ""), @"\(\d+-\s*", "(");
}
