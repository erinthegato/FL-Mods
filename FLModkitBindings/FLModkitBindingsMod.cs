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
