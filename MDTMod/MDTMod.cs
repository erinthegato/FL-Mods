using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlashingLights.ModKit.Core;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MDTMod;

[ModKitManifest(
    Id = "mdt-mod",
    DisplayName = "MDT Terminal",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class MDTMod : ModKitMelonMod<MDTConfig>
{
    protected override string ModId => "mdt-mod";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(1);
    internal const string KeyBindFile = "MDT.keybinds";

    internal static MDTMod Instance { get; private set; } = null!;
    internal static bool InputsBlocked;
    internal static KeyCode ToggleKey = KeyCode.F11;

    internal int CourtIntervalMinutesConfig => Config.CourtIntervalMinutes;

    private MDTUI _mdtUI = null!;
    private bool _mdtVisible;
    private bool _cursorWasLocked;
    private float _courtTimer;
    private float _keyBindReloadTimer;
    private int _courtIntervalMinutes;
    private HarmonyLib.Harmony? _harmony;

    protected override void OnModKitInitialized()
    {
        Instance = this;
        LoadKeyBinds();
        _mdtUI = new MDTUI();
        NPCDataStore.LoadPhotos();
        LoadCourtInterval();

        _harmony = new HarmonyLib.Harmony("mdt-mod.input");
        _harmony.PatchAll();

        LogInfo("MDT Terminal initialized.");
    }

    private void LoadCourtInterval()
    {
        string configPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "config.txt");
        try
        {
            if (File.Exists(configPath))
            {
                string text = File.ReadAllText(configPath).Trim();
                if (int.TryParse(text, out int minutes) && minutes >= 1)
                {
                    _courtIntervalMinutes = minutes;
                    LogInfo($"Court interval loaded from config.txt: {minutes} min");
                    return;
                }
            }
        }
        catch { }
        _courtIntervalMinutes = Config.CourtIntervalMinutes;
        LogInfo($"Court interval defaulting to config value: {_courtIntervalMinutes} min");
    }

    protected override void OnModKitEnabled()
    {
        _courtTimer = 0f;
        LogInfo("MDT Terminal enabled. Press F11 to open.");
    }

    protected override void OnModKitDisabled()
    {
        _mdtVisible = false;
        InputsBlocked = false;
        RestoreCursor();
        _mdtUI.Cleanup();
        NPCDataStore.SavePhotos();
        LogInfo("MDT Terminal disabled.");
    }

    protected override void OnModKitUpdate()
    {
        _keyBindReloadTimer -= Time.unscaledDeltaTime;
        if (_keyBindReloadTimer <= 0f)
        {
            _keyBindReloadTimer = 1f;
            LoadKeyBinds();
        }

        HandleMDTToggle();
        UpdateCourtTimer();
    }

    private void HandleMDTToggle()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            _mdtVisible = !_mdtVisible;
            InputsBlocked = _mdtVisible;
            UpdateCursor();
            LogDebug(_mdtVisible ? "MDT opened." : "MDT dismissed.");
        }
    }

    private void LoadKeyBinds()
    {
        ToggleKey = KeyBindStore.Load(KeyBindFile, nameof(ToggleKey), ToggleKey);
        ToggleKey = KeyBindStore.Load(KeyBindFile, "MDTToggleKey", ToggleKey);
    }

    private void UpdateCourtTimer()
    {
        _courtTimer += Time.unscaledDeltaTime;
        if (_courtTimer >= _courtIntervalMinutes * 60)
        {
            _courtTimer = 0f;
            MDTCourt.RunSession();
        }
    }

    protected override void OnModKitGui()
    {
        if (_mdtVisible)
            _mdtUI.Render();
    }

    private void UpdateCursor()
    {
        if (_mdtVisible)
        {
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
        else
        {
            RestoreCursor();
        }
    }

    private void RestoreCursor()
    {
        Time.timeScale = 1f;
        if (_cursorWasLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
internal static class InputGetKeyPatch
{
    internal static bool Prefix(KeyCode key, ref bool __result)
    {
        if (MDTMod.InputsBlocked && key != MDTMod.ToggleKey)
        {
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
internal static class InputGetKeyDownPatch
{
    internal static bool Prefix(KeyCode key, ref bool __result)
    {
        if (MDTMod.InputsBlocked && key != MDTMod.ToggleKey)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
