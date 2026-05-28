using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using FLMods.Shared;
using FlashingLights.ModKit.Core;
using HarmonyLib;
using UnityEngine;

namespace BackgroundRadio;

[ModKitManifest(
    Id = "background-radio",
    DisplayName = "Background Radio",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class BackgroundRadioMod : ModKitMelonMod<BackgroundRadioConfig>
{
    protected override string ModId => "background-radio";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(1);
    internal const string KeyBindFile = "BackgroundRadio.keybinds";

    internal static BackgroundRadioMod Instance { get; private set; } = null!;

    private BroadcastifyService _broadcastify = null!;
    private AudioPlayer _audioPlayer = null!;
    private RadioUI _radioUI = null!;
    private bool _uiVisible;
    private bool _cursorWasLocked;
    private bool _isLoading = true;
    private string? _currentStation;

    private bool _panicBlocked;
    private float _panicTimer;
    private string? _panicStation;
    private bool _wasPlayingBeforePanic;
    private bool _wasOfflineBeforePanic;

    private bool _noInternet;
    private float _keyBindReloadTimer;
    internal KeyCode ToggleKey { get; private set; } = KeyCode.F10;
    internal KeyCode NavigateUpKey { get; private set; } = KeyCode.UpArrow;
    internal KeyCode NavigateDownKey { get; private set; } = KeyCode.DownArrow;
    internal KeyCode SelectKey { get; private set; } = KeyCode.Return;
    internal KeyCode StopKey { get; private set; } = KeyCode.Space;

    private OfflineScannerPlayer _offlinePlayer = null!;
    private bool _prevOfflineMode;
    private string _offlineDir = null!;
    private HarmonyLib.Harmony? _inputHarmony;

    protected override void OnModKitInitialized()
    {
        Instance = this;
        _inputHarmony = new HarmonyLib.Harmony("background-radio.input-shield");
        _inputHarmony.PatchAll();

        _broadcastify = new BroadcastifyService();
        _audioPlayer = new AudioPlayer();
        _radioUI = new RadioUI();
        LoadKeyBinds();

        _offlineDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(BackgroundRadioMod).Assembly.Location)!,
            "..", "OfflineScanner"));

        _offlinePlayer = new OfflineScannerPlayer(_audioPlayer);
        _offlinePlayer.FileChanged += (file) => DebugLog($"Offline Scanner: now playing {file}");
        _prevOfflineMode = Config.OfflineMode;

        _audioPlayer.PlaybackStarted += () =>
        {
            _currentStation = _radioUI.ActiveFeedName;
            DebugLog($"Playback started: {_currentStation}");
        };
        _audioPlayer.Error += message =>
        {
            LogWarning(message);
            _radioUI.SetStatus(message);
        };
        _audioPlayer.PlaybackStopped += () =>
        {
            _currentStation = null;
            DebugLog("Playback stopped.");
        };

        GrammarPoliceMod.GrammarPoliceMod.PanicTriggered += OnPanicTriggered;

        if (!PerformanceSettings.Current.RadioStreamingAllowed)
        {
            _isLoading = false;
            LogInfo("Background radio streaming disabled by PerformanceMode.json.");
        }
        else if (Config.OfflineMode)
        {
            StartOfflinePlayback();
        }
        else if (NetworkInterface.GetIsNetworkAvailable())
        {
            _ = LoadFeedsAsync();
        }
        else
        {
            _noInternet = true;
            LogWarning("No internet connection detected. Will retry later.");
        }

        LogInfo("Background Radio initialized.");
    }

    private async Task LoadFeedsAsync()
    {
        _isLoading = true;
        var feeds = await _broadcastify.GetTopFeedsAsync();
        if (feeds.Count > 0)
        {
            LogInfo($"Loaded {feeds.Count} top feeds from Broadcastify.");
            _radioUI.ResetSelection();
        }
        else
        {
            LogWarning("No feeds could be loaded from Broadcastify.");
        }
        _isLoading = false;
    }

    private void OnPanicTriggered()
    {
        if (_audioPlayer.IsPlaying)
        {
            _wasPlayingBeforePanic = true;
            _wasOfflineBeforePanic = Config.OfflineMode;
            _panicStation = _currentStation;
            _offlinePlayer.Stop();
            _audioPlayer.Stop();
            _panicBlocked = true;
            _panicTimer = Config.PanicResumeDelaySeconds;
            LogInfo("Background radio stopped due to panic.");
        }
    }

    protected override void OnModKitEnabled()
    {
        LogInfo($"Background Radio enabled. Press {ToggleKey} to open.");
    }

    protected override void OnModKitDisabled()
    {
        _offlinePlayer.Stop();
        _audioPlayer.Stop();
        _uiVisible = false;
        ModInputShield.SetBlocked(false);
        RestoreCursor();
        LogInfo("Background Radio disabled.");
    }

    private void LoadKeyBinds()
    {
        ToggleKey = KeyBindStore.Load(KeyBindFile, nameof(ToggleKey), ToggleKey);
        NavigateUpKey = KeyBindStore.Load(KeyBindFile, nameof(NavigateUpKey), NavigateUpKey);
        NavigateDownKey = KeyBindStore.Load(KeyBindFile, nameof(NavigateDownKey), NavigateDownKey);
        SelectKey = KeyBindStore.Load(KeyBindFile, nameof(SelectKey), SelectKey);
        StopKey = KeyBindStore.Load(KeyBindFile, nameof(StopKey), StopKey);
    }

    protected override void OnModKitUpdate()
    {
        _keyBindReloadTimer -= Time.unscaledDeltaTime;
        if (_keyBindReloadTimer <= 0f)
        {
            _keyBindReloadTimer = 1f;
            LoadKeyBinds();
        }
        UpdateInputShield();

        bool radioAllowed = PerformanceSettings.Current.RadioStreamingAllowed;
        if (!radioAllowed)
        {
            _noInternet = false;
            _panicBlocked = false;
            _wasPlayingBeforePanic = false;
            _offlinePlayer.Stop();
            _audioPlayer.Stop();
            _currentStation = null;

            if (Input.GetKeyDown(ToggleKey))
            {
                _uiVisible = !_uiVisible;
                UpdateCursor();
                DebugLog($"Radio UI toggled: {_uiVisible}");
            }

            return;
        }

        if (Config.OfflineMode)
        {
            _noInternet = false;
        }
        else if (_noInternet)
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                _noInternet = false;
                LogInfo("Network now available. Loading feeds...");
                _ = LoadFeedsAsync();
            }
            return;
        }

        if (_panicBlocked)
        {
            _panicTimer -= Time.unscaledDeltaTime;
            if (_panicTimer <= 0f)
            {
                _panicBlocked = false;
                if (_wasPlayingBeforePanic)
                {
                    if (_wasOfflineBeforePanic)
                    {
                        LogInfo("Resuming offline scanner after panic cooldown.");
                        _ = _offlinePlayer.PlayAsync();
                    }
                    else if (!string.IsNullOrEmpty(_panicStation))
                    {
                        LogInfo("Resuming background radio after panic cooldown.");
                        _ = ResumeFeedAsync();
                    }
                }
                _wasPlayingBeforePanic = false;
                _wasOfflineBeforePanic = false;
                _panicStation = null;
            }
        }

        _offlinePlayer.SetPauseSeconds(Config.OfflinePauseSeconds);

        bool currentMode = Config.OfflineMode;
        if (currentMode != _prevOfflineMode)
        {
            _prevOfflineMode = currentMode;
            if (currentMode)
            {
                _audioPlayer.Stop();
                _currentStation = null;
                StartOfflinePlayback();
            }
            else
            {
                _offlinePlayer.Stop();
            }
        }

        if (Input.GetKeyDown(ToggleKey) && !_panicBlocked)
        {
            _uiVisible = !_uiVisible;
            UpdateCursor();
            DebugLog($"Radio UI toggled: {_uiVisible}");
        }

        if (!_uiVisible) return;
        if (!radioAllowed) return;

        _radioUI.HandleKeyboard(_broadcastify, _audioPlayer, Config, _offlinePlayer);
    }

    private async Task ResumeFeedAsync()
    {
        if (_panicStation == null) return;
        var feeds = _broadcastify.GetCachedFeeds();
        var feed = feeds.Find(f => f.Name == _panicStation);
        if (feed != null)
        {
            string? url = await _broadcastify.GetStreamUrlAsync(feed.Id);
            if (url != null)
                await _audioPlayer.PlayAsync(url);
        }
    }

    private void StartOfflinePlayback()
    {
        _offlinePlayer.Stop();
        _offlinePlayer.ScanDirectory(_offlineDir);
        if (_offlinePlayer.FileCount > 0)
        {
            LogInfo($"Offline Scanner: loaded {_offlinePlayer.FileCount} files from {_offlineDir}");
            _ = _offlinePlayer.PlayAsync();
        }
        else
        {
            LogWarning($"Offline Scanner: no audio files found in {_offlineDir}");
        }
    }

    protected override void OnModKitGui()
    {
        if (Config.ShowNowPlayingOverlay)
            DrawNowPlayingOverlay();

        if (!_uiVisible) return;

        if (_noInternet)
        {
            float nw = 380, nh = 100;
            var nRect = new Rect(Screen.width - nw - 10, 60, nw, nh);
            GUI.Box(nRect, "Background Radio\n\nNo internet connection.\nThe mod will retry automatically.");
            return;
        }

        if (!PerformanceSettings.Current.RadioStreamingAllowed)
        {
            float pw = 380, ph = 100;
            var pRect = new Rect(Screen.width - pw - 10, 60, pw, ph);
            GUI.Box(pRect, "Background Radio\n\nStreaming is disabled by PerformanceMode.json.");
            return;
        }

        float w = 380, h = 500;
        var rect = new Rect(Screen.width - w - 10, 60, w, h);
        _radioUI.Draw(rect, _broadcastify, _audioPlayer, Config, _isLoading, _currentStation, _offlinePlayer);
    }

    private void DrawNowPlayingOverlay()
    {
        string? text = null;
        if (Config.OfflineMode && _offlinePlayer.IsPlaying)
            text = _offlinePlayer.CurrentFileName;
        else if (_audioPlayer.IsPlaying)
            text = _currentStation;

        if (string.IsNullOrWhiteSpace(text)) return;

        float w = 360f, h = 44f;
        var rect = new Rect(18, Screen.height - h - 18, w, h);
        GUI.Box(rect, "");
        GUI.Label(new Rect(rect.x + 10, rect.y + 5, w - 20, 16), "BACKGROUND RADIO");
        GUI.Label(new Rect(rect.x + 10, rect.y + 22, w - 20, 18), text);
    }

    private void UpdateCursor()
    {
        if (_uiVisible)
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

    private void UpdateInputShield()
    {
        ModInputShield.SetBlocked(_uiVisible, ToggleKey, NavigateUpKey, NavigateDownKey, SelectKey, StopKey);
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

    private void DebugLog(string message)
    {
        if (Config.DebugLogging)
            LogDebug(message);
    }
}
