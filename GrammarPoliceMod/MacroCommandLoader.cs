using System.Text.Json;
using MelonLoader;

namespace GrammarPoliceMod;

public sealed record MacroCommand(
    string Name,
    List<string> Triggers,
    List<MacroStep> Steps
);

public sealed record MacroStep(
    string Type,
    string Value,
    int DelayMs = 0,
    string Message = ""
);

public static class MacroCommandLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string ConfigPath = Path.Combine(
        Path.GetDirectoryName(typeof(GrammarPoliceMod).Assembly.Location) ?? ".",
        "GrammarPolice", "MacroCommands.json");

    public static List<MacroCommand> CurrentMacros { get; private set; } = new();

    public static List<MacroCommand> Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                CurrentMacros = Defaults();
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(CurrentMacros, JsonOptions));
                return CurrentMacros;
            }

            CurrentMacros = JsonSerializer.Deserialize<List<MacroCommand>>(File.ReadAllText(ConfigPath)) ?? Defaults();
            return CurrentMacros;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GrammarPolice] Macro load failed: {ex.Message}");
            CurrentMacros = Defaults();
            return CurrentMacros;
        }
    }

    public static MacroCommand? Match(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (var macro in CurrentMacros)
        {
            foreach (string trigger in macro.Triggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger) && lower.Contains(trigger.ToLowerInvariant()))
                    return macro;
            }
        }
        return null;
    }

    private static List<MacroCommand> Defaults() => new()
    {
        new MacroCommand(
            "request location",
            new List<string> { "request location", "location check", "what is my 10 20", "what is my ten twenty" },
            new List<MacroStep>
            {
                new("command", "10-20", 0, "Requesting Location")
            }),
        new MacroCommand(
            "traffic stop",
            new List<string> { "traffic stop", "start traffic stop", "vehicle stop" },
            new List<MacroStep>
            {
                new("command", "10-50", 0, "Traffic Stop / Accident"),
                new("keySequence", "F11", 250, "Open MDT for plate check"),
                new("command", "10-28", 500, "Vehicle Registration Check")
            })
    };
}
