using System.Reflection;
using System.Text.RegularExpressions;

internal sealed class PerformanceSettings
{
    private static readonly object Sync = new();
    private static PerformanceSettings _current = new();
    private static DateTime _lastReadUtc = DateTime.MinValue;
    private static string? _settingsPath;

    public bool Enabled { get; private set; }
    public bool DisableVoiceRecognition { get; private set; } = true;
    public bool DisableAssetAutoload { get; private set; } = true;
    public bool DisableBodycamWeaponPolling { get; private set; } = true;
    public bool DisableRadioStreaming { get; private set; } = true;
    public bool DisableNpcAiScans { get; private set; } = true;

    public static PerformanceSettings Current
    {
        get
        {
            RefreshIfNeeded();
            return _current;
        }
    }

    public bool VoiceRecognitionAllowed => !Enabled || !DisableVoiceRecognition;
    public bool AssetAutoloadAllowed => !Enabled || !DisableAssetAutoload;
    public bool BodycamWeaponPollingAllowed => !Enabled || !DisableBodycamWeaponPolling;
    public bool RadioStreamingAllowed => !Enabled || !DisableRadioStreaming;
    public bool NpcAiScansAllowed => !Enabled || !DisableNpcAiScans;

    private static void RefreshIfNeeded()
    {
        if ((DateTime.UtcNow - _lastReadUtc).TotalSeconds < 2)
            return;

        lock (Sync)
        {
            if ((DateTime.UtcNow - _lastReadUtc).TotalSeconds < 2)
                return;

            _lastReadUtc = DateTime.UtcNow;
            string path = GetSettingsPath();
            EnsureTemplate(path);

            try
            {
                string json = File.ReadAllText(path);
                _current = new PerformanceSettings
                {
                    Enabled = ReadBool(json, "enabled", false),
                    DisableVoiceRecognition = ReadBool(json, "disableVoiceRecognition", true),
                    DisableAssetAutoload = ReadBool(json, "disableAssetAutoload", true),
                    DisableBodycamWeaponPolling = ReadBool(json, "disableBodycamWeaponPolling", true),
                    DisableRadioStreaming = ReadBool(json, "disableRadioStreaming", true),
                    DisableNpcAiScans = ReadBool(json, "disableNpcAiScans", true)
                };
            }
            catch
            {
                _current = new PerformanceSettings();
            }
        }
    }

    private static bool ReadBool(string json, string name, bool defaultValue)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(name)}\"\\s*:\\s*(true|false)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? bool.Parse(match.Groups[1].Value) : defaultValue;
    }

    private static string GetSettingsPath()
    {
        if (_settingsPath != null)
            return _settingsPath;

        string root = FindGameRoot();
        _settingsPath = Path.Combine(root, "UserData", "FLMods", "PerformanceMode.json");
        return _settingsPath;
    }

    private static void EnsureTemplate(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                return;

            File.WriteAllText(path,
                "{\n" +
                "  \"enabled\": false,\n" +
                "  \"disableVoiceRecognition\": true,\n" +
                "  \"disableAssetAutoload\": true,\n" +
                "  \"disableBodycamWeaponPolling\": true,\n" +
                "  \"disableRadioStreaming\": true,\n" +
                "  \"disableNpcAiScans\": true\n" +
                "}\n");
        }
        catch
        {
        }
    }

    private static string FindGameRoot()
    {
        try
        {
            string? location = Assembly.GetExecutingAssembly().Location;
            var dir = new DirectoryInfo(Path.GetDirectoryName(location) ?? ".");
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "flashinglights.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
        }

        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            ".."));
    }
}
