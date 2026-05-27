using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using FlashingLights.ModKit.Core;
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

    private OfflineScannerPlayer _offlinePlayer = null!;
    private bool _prevOfflineMode;
    private string _offlineDir = null!;

    protected override void OnModKitInitialized()
    {
        Instance = this;

        _broadcastify = new BroadcastifyService();
        _audioPlayer = new AudioPlayer();
        _radioUI = new RadioUI();
        LoadKeyBinds();

        _offlineDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(BackgroundRadioMod).Assembly.Location)!,
            "..", "OfflineScanner"));

        _offlinePlayer = new OfflineScannerPlayer(_audioPlayer);
        _offlinePlayer.FileChanged += (file) => LogDebug($"Offline Scanner: now playing {file}");
        _prevOfflineMode = Config.OfflineMode;

        _audioPlayer.PlaybackStarted += () =>
        {
            _currentStation = _radioUI.ActiveFeedName;
            LogDebug($"Playback started: {_currentStation}");
        };
        _audioPlayer.PlaybackStopped += () =>
        {
            _currentStation = null;
            LogDebug("Playback stopped.");
        };

        GrammarPoliceMod.GrammarPoliceMod.PanicTriggered += OnPanicTriggered;

        if (Config.OfflineMode)
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
        LogInfo("Background Radio enabled. Press F10 to open.");
    }

    protected override void OnModKitDisabled()
    {
        _offlinePlayer.Stop();
        _audioPlayer.Stop();
        _uiVisible = false;
        RestoreCursor();
        LogInfo("Background Radio disabled.");
    }

    private void LoadKeyBinds()
    {
        Config.ToggleKey = KeyBindStore.Load(KeyBindFile, nameof(Config.ToggleKey), Config.ToggleKey);
        Config.NavigateUpKey = KeyBindStore.Load(KeyBindFile, nameof(Config.NavigateUpKey), Config.NavigateUpKey);
        Config.NavigateDownKey = KeyBindStore.Load(KeyBindFile, nameof(Config.NavigateDownKey), Config.NavigateDownKey);
        Config.SelectKey = KeyBindStore.Load(KeyBindFile, nameof(Config.SelectKey), Config.SelectKey);
        Config.StopKey = KeyBindStore.Load(KeyBindFile, nameof(Config.StopKey), Config.StopKey);
    }

    protected override void OnModKitUpdate()
    {
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

        if (Input.GetKeyDown(Config.ToggleKey) && !_panicBlocked)
        {
            _uiVisible = !_uiVisible;
            UpdateCursor();
            LogDebug($"Radio UI toggled: {_uiVisible}");
        }

        if (!_uiVisible) return;
        if (KeyBindWidget.IsCapturing) return;

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
        if (!_uiVisible) return;

        if (_noInternet)
        {
            float nw = 380, nh = 100;
            var nRect = new Rect(Screen.width - nw - 10, 60, nw, nh);
            GUI.Box(nRect, "Background Radio\n\nNo internet connection.\nThe mod will retry automatically.");
            return;
        }

        float w = 380, h = 500;
        var rect = new Rect(Screen.width - w - 10, 60, w, h);
        _radioUI.Draw(rect, _broadcastify, _audioPlayer, Config, _isLoading, _currentStation, _offlinePlayer);
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
