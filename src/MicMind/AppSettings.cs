using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicMind;

/// <summary>Settings persisted to %APPDATA%\MicMind\settings.json.</summary>
internal sealed class AppSettings
{
    // The microphone to keep (e.g. Blue Snowball). Matched by id first, then by name,
    // so it is found again even if it is plugged into another USB port.
    public string? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceDeviceName { get; set; }

    // The playback side of the virtual cable (e.g. "CABLE Input (VB-Audio Virtual Cable)").
    public string? OutputId { get; set; }
    public string? OutputName { get; set; }
    public string? OutputDeviceName { get; set; }

    // Keep the recording side of the virtual cable as the Windows default microphone.
    public bool KeepVirtualMicDefault { get; set; } = true;

    [JsonIgnore]
    public bool IsComplete => !string.IsNullOrEmpty(SourceName) && !string.IsNullOrEmpty(OutputName);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicMind", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings: start over with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
}
