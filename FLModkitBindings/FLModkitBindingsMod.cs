using FlashingLights.ModKit.Core;
using UnityEngine;

namespace FLModkitBindings;

[ModKitManifest(
    Id = "fl-modkit-bindings",
    DisplayName = "FL Modkit Bindings",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Utility")]
public sealed class FLModkitBindingsMod : ModKitMelonMod<FLModkitBindingsConfig>
{
    protected override string ModId => "fl-modkit-bindings";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(0.5);

    private float _saveTimer;
    private string _lastSnapshot = "";
    private string _lastConflictSummary = "";

    private sealed record BindingEntry(string Label, Func<KeyCode> Get, Action<KeyCode> Set);

    protected override void OnModKitInitialized()
    {
        ApplyConfigToBindFiles(force: true);
        LogInfo("Central key bindings initialized. Edit keys in this ModKit page.");
    }

    protected override void OnModKitUpdate()
    {
        _saveTimer -= Time.unscaledDeltaTime;
        if (_saveTimer > 0f) return;

        _saveTimer = 1f;
        ApplyConfigToBindFiles(force: false);
    }

    protected override void OnConfigApplied(FLModkitBindingsConfig currentConfig)
    {
        ApplyConfigToBindFiles(force: true);
    }

    protected override void OnConfigReloaded(FLModkitBindingsConfig previous, FLModkitBindingsConfig current)
    {
        ApplyConfigToBindFiles(force: true);
    }

    private void ApplyConfigToBindFiles(bool force)
    {
        DetectConflicts();

        string snapshot = BuildSnapshot();
        if (!force && snapshot == _lastSnapshot)
            return;

        _lastSnapshot = snapshot;

        Save("AssetLoader.keybinds", "ReloadKey", Config.AssetLoaderReloadKey);

        Save("BackgroundRadio.keybinds", "ToggleKey", Config.BackgroundRadioToggleKey);
        Save("BackgroundRadio.keybinds", "NavigateUpKey", Config.BackgroundRadioNavigateUpKey);
        Save("BackgroundRadio.keybinds", "NavigateDownKey", Config.BackgroundRadioNavigateDownKey);
        Save("BackgroundRadio.keybinds", "SelectKey", Config.BackgroundRadioSelectKey);
        Save("BackgroundRadio.keybinds", "StopKey", Config.BackgroundRadioStopKey);

        Save("BodyCamOverlay.keybinds", "ToggleKey", Config.BodyCamToggleKey);
        Save("BodyCamOverlay.keybinds", "EmergencyTriggerKey", Config.BodyCamEmergencyTriggerKey);
        Save("BodyCamOverlay.keybinds", "BookmarkKey", Config.BodyCamBookmarkKey);
        Save("BodyCamOverlay.keybinds", "LicenseScanKey", Config.BodyCamLicenseScanKey);

        Save("GrammarPolice.keybinds", "PushToTalkKey", Config.GrammarPolicePushToTalkKey);
        Save("GrammarPolice.keybinds", "RadioUIToggleKey", Config.GrammarPoliceRadioUIToggleKey);
        Save("GrammarPolice.keybinds", "RadioNavigateUpKey", Config.GrammarPoliceRadioNavigateUpKey);
        Save("GrammarPolice.keybinds", "RadioNavigateDownKey", Config.GrammarPoliceRadioNavigateDownKey);
        Save("GrammarPolice.keybinds", "RadioSelectKey", Config.GrammarPoliceRadioSelectKey);
        Save("GrammarPolice.keybinds", "PanicTriggerKey", Config.GrammarPolicePanicTriggerKey);

        Save("MDT.keybinds", "MDTToggleKey", Config.MDTToggleKey);

        Save("NPCAI.keybinds", "ToggleKey", Config.NPCAIToggleKey);
        Save("NPCAI.keybinds", "SendKey", Config.NPCAISendKey);
    }

    private void Save(string fileName, string id, KeyCode value)
    {
        KeyBindStore.Save(fileName, id, value);
    }

    private void DetectConflicts()
    {
        var entries = GetEntries();
        var conflicts = entries
            .Where(e => e.Get() != KeyCode.None)
            .GroupBy(e => e.Get())
            .Where(g => g.Count() > 1)
            .ToList();

        if (conflicts.Count == 0)
        {
            _lastConflictSummary = "";
            return;
        }

        if (Config.AutoResolveConflicts)
        {
            AutoResolve(conflicts);
            LogWarning("Keybind conflicts detected and auto-resolved. Review FL Modkit Bindings for the final keys.");
            _lastConflictSummary = "";
            return;
        }

        string summary = string.Join("; ", conflicts.Select(g =>
            $"{g.Key}: {string.Join(", ", g.Select(e => e.Label))}"));

        if (summary != _lastConflictSummary)
        {
            _lastConflictSummary = summary;
            LogWarning($"Keybind conflict detected: {summary}. Enable Auto Resolve Key Conflicts to assign unused fallback keys.");
        }
    }

    private void AutoResolve(List<IGrouping<KeyCode, BindingEntry>> conflicts)
    {
        var used = new HashSet<KeyCode>(GetEntries().Select(e => e.Get()).Where(k => k != KeyCode.None));
        foreach (var group in conflicts)
        {
            bool keepFirst = true;
            foreach (var entry in group)
            {
                if (keepFirst)
                {
                    keepFirst = false;
                    continue;
                }

                var next = FindUnusedKey(used);
                entry.Set(next);
                used.Add(next);
            }
        }
    }

    private static KeyCode FindUnusedKey(HashSet<KeyCode> used)
    {
        KeyCode[] preferred =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.B, KeyCode.G, KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L,
            KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Semicolon, KeyCode.Backslash
        };

        foreach (var key in preferred)
            if (!used.Contains(key))
                return key;

        return KeyCode.None;
    }

    private List<BindingEntry> GetEntries() => new()
    {
        new("Asset Loader Reload", () => Config.AssetLoaderReloadKey, v => Config.AssetLoaderReloadKey = v),
        new("Background Radio Toggle", () => Config.BackgroundRadioToggleKey, v => Config.BackgroundRadioToggleKey = v),
        new("Background Radio Up", () => Config.BackgroundRadioNavigateUpKey, v => Config.BackgroundRadioNavigateUpKey = v),
        new("Background Radio Down", () => Config.BackgroundRadioNavigateDownKey, v => Config.BackgroundRadioNavigateDownKey = v),
        new("Background Radio Select", () => Config.BackgroundRadioSelectKey, v => Config.BackgroundRadioSelectKey = v),
        new("Background Radio Stop", () => Config.BackgroundRadioStopKey, v => Config.BackgroundRadioStopKey = v),
        new("Bodycam Toggle", () => Config.BodyCamToggleKey, v => Config.BodyCamToggleKey = v),
        new("Bodycam Emergency", () => Config.BodyCamEmergencyTriggerKey, v => Config.BodyCamEmergencyTriggerKey = v),
        new("Bodycam Bookmark", () => Config.BodyCamBookmarkKey, v => Config.BodyCamBookmarkKey = v),
        new("Bodycam License Scan", () => Config.BodyCamLicenseScanKey, v => Config.BodyCamLicenseScanKey = v),
        new("Grammar Police PTT", () => Config.GrammarPolicePushToTalkKey, v => Config.GrammarPolicePushToTalkKey = v),
        new("Grammar Police Radio UI", () => Config.GrammarPoliceRadioUIToggleKey, v => Config.GrammarPoliceRadioUIToggleKey = v),
        new("Grammar Police Up", () => Config.GrammarPoliceRadioNavigateUpKey, v => Config.GrammarPoliceRadioNavigateUpKey = v),
        new("Grammar Police Down", () => Config.GrammarPoliceRadioNavigateDownKey, v => Config.GrammarPoliceRadioNavigateDownKey = v),
        new("Grammar Police Select", () => Config.GrammarPoliceRadioSelectKey, v => Config.GrammarPoliceRadioSelectKey = v),
        new("Grammar Police Panic", () => Config.GrammarPolicePanicTriggerKey, v => Config.GrammarPolicePanicTriggerKey = v),
        new("MDT Toggle", () => Config.MDTToggleKey, v => Config.MDTToggleKey = v),
        new("NPC AI Toggle", () => Config.NPCAIToggleKey, v => Config.NPCAIToggleKey = v),
        new("NPC AI Send", () => Config.NPCAISendKey, v => Config.NPCAISendKey = v),
    };

    private string BuildSnapshot() =>
        string.Join("|",
            Config.AssetLoaderReloadKey,
            Config.BackgroundRadioToggleKey,
            Config.BackgroundRadioNavigateUpKey,
            Config.BackgroundRadioNavigateDownKey,
            Config.BackgroundRadioSelectKey,
            Config.BackgroundRadioStopKey,
            Config.BodyCamToggleKey,
            Config.BodyCamEmergencyTriggerKey,
            Config.BodyCamBookmarkKey,
            Config.BodyCamLicenseScanKey,
            Config.GrammarPolicePushToTalkKey,
            Config.GrammarPoliceRadioUIToggleKey,
            Config.GrammarPoliceRadioNavigateUpKey,
            Config.GrammarPoliceRadioNavigateDownKey,
            Config.GrammarPoliceRadioSelectKey,
            Config.GrammarPolicePanicTriggerKey,
            Config.MDTToggleKey,
            Config.NPCAIToggleKey,
            Config.NPCAISendKey);
}
