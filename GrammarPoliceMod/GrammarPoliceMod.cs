using System.Collections.Generic;
using FlashingLights.ModKit.Core;
using UnityEngine;

namespace GrammarPoliceMod;

[ModKitManifest(
    Id = "grammar-police",
    DisplayName = "Grammar Police",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class GrammarPoliceMod : ModKitMelonMod<GrammarPoliceConfig>
{
    protected override string ModId => "grammar-police";
    protected override bool EnableConfigHotReload => Config.EnableHotReload;
    protected override TimeSpan ConfigReloadInterval =>
        TimeSpan.FromSeconds(Math.Clamp(Config.HotReloadIntervalSeconds, 0.25, 30));
    internal const string KeyBindFile = "GrammarPolice.keybinds";

    internal static GrammarPoliceMod Instance { get; private set; } = null!;
    public static event Action? PanicTriggered;

    internal double ConfidenceThreshold => Config.ConfidenceThreshold;
    internal bool VerboseLogging => Config.VerboseLogging;
    internal bool VoiceRecognitionEnabled => Config.VoiceRecognitionEnabled && PerformanceSettings.Current.VoiceRecognitionAllowed;
    internal bool AutoDispatchEnabled => Config.AutoDispatchBackup;
    internal bool KeyEmulationEnabled => Config.KeyEmulationEnabled;
    internal DispatchAudio DispatchAudio => _dispatchAudio;
    internal KeyCode PushToTalkKey { get; private set; } = KeyCode.LeftControl;
    internal KeyCode RadioUIToggleKey { get; private set; } = KeyCode.F12;
    internal KeyCode RadioNavigateUpKey { get; private set; } = KeyCode.UpArrow;
    internal KeyCode RadioNavigateDownKey { get; private set; } = KeyCode.DownArrow;
    internal KeyCode RadioSelectKey { get; private set; } = KeyCode.Return;
    internal KeyCode PanicTriggerKey { get; private set; } = KeyCode.F;

    internal string GetKeySequence(int num) => num switch
    {
        1 => Config.KeySequence_10_1,
        2 => Config.KeySequence_10_2,
        3 => Config.KeySequence_10_3,
        4 => Config.KeySequence_10_4,
        5 => Config.KeySequence_10_5,
        6 => Config.KeySequence_10_6,
        7 => Config.KeySequence_10_7,
        8 => Config.KeySequence_10_8,
        9 => Config.KeySequence_10_9,
        10 => Config.KeySequence_10_10,
        _ => "",
    };

    private int _panicPressCount;
    private float _panicTimer;
    private float _keyBindReloadTimer;

    private CommandEngine _commandEngine = null!;
    private DispatchAudio _dispatchAudio = null!;
    private RadioUI _radioUI = null!;
    private bool _uiVisible;
    private bool _pttShown;
    private bool _pttWasHeld;
    private bool _cursorWasLocked;

    protected override void OnModKitInitialized()
    {
        Instance = this;
        _commandEngine = new CommandEngine();
        _dispatchAudio = new DispatchAudio();
        _radioUI = new RadioUI(_commandEngine, _dispatchAudio);
        LoadKeyBinds();
        _radioUI.SetOverlayDuration(Config.OverlayDisplaySeconds);

        if (!CommandEngine.IsSpeechRuntimeAvailable(out string voiceReason))
        {
            _commandEngine.DisableVoice("Windows speech runtime dependency missing");
            LogWarning($"Speech recognition disabled; button-only mode active. {voiceReason}");
        }

        if (_dispatchAudio.PanicAudioFiles.Count > 0)
            LogInfo($"Panic audio files available: {string.Join(", ", _dispatchAudio.PanicAudioFiles)}");
        else
            LogInfo("No panic audio files found in DispatchAudio/Panic Button/");

        LogInfo("Grammar Police initialized.");
    }

    protected override void OnModKitEnabled()
    {
        if (VoiceRecognitionEnabled && _commandEngine.IsVoiceAvailable)
            LogInfo("Grammar Police enabled. Hold PTT key to transmit.");
        else
            LogInfo("Grammar Police enabled in button-only mode. Use the radio UI for dispatch commands.");
    }

    protected override void OnModKitDisabled()
    {
        SafeStopVoice();
        _uiVisible = false;
        _pttShown = false;
        _pttWasHeld = false;
        RestoreCursor();
        LogInfo("Grammar Police disabled.");
    }

    protected override void OnModKitUpdate()
    {
        _keyBindReloadTimer -= Time.unscaledDeltaTime;
        if (_keyBindReloadTimer <= 0f)
        {
            _keyBindReloadTimer = 1f;
            LoadKeyBinds();
        }

        if (Config.PanicEnabled)
        {
            _panicTimer -= Time.unscaledDeltaTime;
            if (Input.GetKeyDown(PanicTriggerKey))
            {
                if (_panicTimer <= 0f)
                    _panicPressCount = 0;
                _panicPressCount++;
                _panicTimer = (float)Config.PanicTimeWindow;

                if (_panicPressCount >= Config.PanicPressCount)
                {
                    _panicPressCount = 0;
                    _panicTimer = 0f;
                    TriggerPanic();
                }
            }
            if (_panicTimer <= 0f)
                _panicPressCount = 0;
        }

        if (Input.GetKeyDown(RadioUIToggleKey))
        {
            _uiVisible = !_uiVisible;
            _pttShown = false;
            UpdateCursor();
            LogDebug($"Radio UI toggled: {_uiVisible}");
        }

        bool pttHeld = VoiceRecognitionEnabled && _commandEngine.IsVoiceAvailable && Input.GetKey(PushToTalkKey);
        if (pttHeld && !_pttWasHeld)
        {
            _commandEngine.Start();
            if (!_uiVisible)
            {
                _uiVisible = true;
                _pttShown = true;
                UpdateCursor();
            }
        }
        else if (!pttHeld && _pttWasHeld)
        {
            SafeStopVoice();
            if (_pttShown)
            {
                _uiVisible = false;
                _pttShown = false;
                RestoreCursor();
            }
        }
        _pttWasHeld = pttHeld;

        _radioUI.Update(Time.unscaledDeltaTime);
        _commandEngine.Update();
    }

    private void SafeStopVoice()
    {
        try
        {
            _commandEngine.Stop();
        }
        catch (FileNotFoundException)
        {
        }
    }

    protected override void OnModKitGui()
    {
        if (_radioUI == null || _commandEngine == null)
            return;

        if (_uiVisible)
        {
            var radioRect = new Rect(
                Screen.width - 420,
                Screen.height - 320,
                400,
                300);
            _radioUI.RenderPanel(radioRect, _commandEngine, Config);
        }

        if (Config.ShowTransmissionOverlay)
            _radioUI.RenderOverlay();
    }

    private string GetCurrentRole()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            if (player.name.Contains("Fire")) return "Fire";
            if (player.name.Contains("Ambulance") || player.name.Contains("EMS")) return "EMS";
        }
        return Config.CurrentRole;
    }

    private void TriggerPanic()
    {
        string role = GetCurrentRole();
        if (!Config.PanicProfiles.TryGetValue(role, out var profile))
            profile = Config.PanicProfiles["Law"];

        if (!profile.Enabled) return;

        string code = profile.DispatchCode;
        string message = profile.DispatchMessage;

        if (Config.PanicAudioEnabled && !string.IsNullOrWhiteSpace(profile.AudioFile))
            _dispatchAudio.PlayPanicTone(profile.AudioFile);
        else if (Config.PanicAudioEnabled && _dispatchAudio.PanicAudioFiles.Count > 0)
            _dispatchAudio.PlayPanicTone(_dispatchAudio.PanicAudioFiles[0]);

        if (Config.KeyEmulationEnabled && !string.IsNullOrWhiteSpace(profile.KeySequence))
            KeySimulator.PressSequence(profile.KeySequence, 1000);

        _commandEngine.ExecuteCommand(code, message);
        PanicTriggered?.Invoke();
        LogInfo($"Panic triggered ({role}): {code} - {message}");
    }

    private void LoadKeyBinds()
    {
        PushToTalkKey = KeyBindStore.Load(KeyBindFile, nameof(PushToTalkKey), PushToTalkKey);
        RadioUIToggleKey = KeyBindStore.Load(KeyBindFile, nameof(RadioUIToggleKey), RadioUIToggleKey);
        RadioNavigateUpKey = KeyBindStore.Load(KeyBindFile, nameof(RadioNavigateUpKey), RadioNavigateUpKey);
        RadioNavigateDownKey = KeyBindStore.Load(KeyBindFile, nameof(RadioNavigateDownKey), RadioNavigateDownKey);
        RadioSelectKey = KeyBindStore.Load(KeyBindFile, nameof(RadioSelectKey), RadioSelectKey);
        PanicTriggerKey = KeyBindStore.Load(KeyBindFile, nameof(PanicTriggerKey), PanicTriggerKey);
    }

    private void UpdateCursor()
    {
        if (_uiVisible)
        {
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            RestoreCursor();
        }
    }

    private void RestoreCursor()
    {
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

    protected override void OnConfigApplied(GrammarPoliceConfig currentConfig)
    {
        _radioUI.SetOverlayDuration(currentConfig.OverlayDisplaySeconds);
        LogInfo($"Config applied: voice={VoiceRecognitionEnabled} confidence={currentConfig.ConfidenceThreshold}");
    }

    protected override void OnConfigReloaded(GrammarPoliceConfig previous, GrammarPoliceConfig current)
    {
        LogInfo($"Config reloaded.");
    }
}
