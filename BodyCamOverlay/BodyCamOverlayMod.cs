using System.IO;
using System.Text;
using System.Reflection;
using FLMods.Shared;
using FlashingLights.ModKit.Core;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodyCamOverlay;

[ModKitManifest(
    Id = "bodycam-overlay",
    DisplayName = "Bodycam Overlay",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class BodyCamOverlayMod : ModKitMelonMod<BodyCamConfig>
{
    protected override string ModId => "bodycam-overlay";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(1);
    internal const string KeyBindFile = "BodyCamOverlay.keybinds";
    private static readonly HashSet<string> WeaponNames = new(StringComparer.OrdinalIgnoreCase) { "Gun_AP58", "Wep_Pistol_01" };

    private GameObject? _cachedPlayer;
    private Transform? _cachedWeapon;
    private readonly List<Transform> _weaponCandidates = new();
    private bool _weaponWasDrawn;
    private bool _overlayActive;
    private float _weaponPollTimer;
    private float _nextWeaponCacheRefreshTime;
    private float _signalTimer;
    private float _emergencyTriggerTimer;
    private float _keyBindReloadTimer;
    private float _idleTimer;
    private int _emergencyTriggerPresses;
    private KeyCode _toggleKey = KeyCode.F8;
    private KeyCode _emergencyTriggerKey = KeyCode.Alpha2;
    private KeyCode _bookmarkKey = KeyCode.B;
    private KeyCode _licenseScanKey = KeyCode.L;
    private string _signalPath = "";
    private bool _signalWavExists;
    private RuntimeAudioPlayer? _audioPlayer;
    private BodyCamScreenReader? _screenReader;
    private string _cameraLabel = "";
    private string _agencyLabel = "";
    private string _metaLabel = "";
    private string _gpsLabel = "GPS UNKNOWN";
    private string _resourceLabel = "";
    private string _clockLabel = "";
    private int _lastClockSecond = -1;
    private int _lastResourceSecond = -1;
    private float _nextGpsRefreshTime;
    private float _nextPlayerCacheTime;

    private static GUIStyle? _topStyle;
    private static GUIStyle? _smallStyle;
    private static GUIStyle? _recStyle;
    private static Texture2D? _barTex;
    private static Texture2D? _redTex;
    private const string SignalVersion = "raspy-vibrato-v2";
    private static readonly string ClockFormat = "yyyy-MM-dd HH:mm:ss";

    protected override void OnModKitInitialized()
    {
        _signalPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "AxonSignal.wav");
        LoadKeyBinds();
        RefreshOverlayLabels();
        EnsureSignalWav(_signalPath);
        _signalWavExists = File.Exists(_signalPath);
        _screenReader = new BodyCamScreenReader();
        _audioPlayer = new RuntimeAudioPlayer(
            debugLog: msg => { if (Config.DebugLogging) LogDebug(msg); },
            warningLog: msg => MelonLogger.Warning($"[BodyCamOverlay] {msg}"));
        GrammarPoliceMod.GrammarPoliceMod.PanicTriggered += OnPanicTriggered;
    }

    protected override void OnModKitDisabled()
    {
        _overlayActive = false;
        _weaponWasDrawn = false;
        _cachedWeapon = null;
        _cachedPlayer = null;
        _weaponCandidates.Clear();
        _audioPlayer?.Dispose();
        _audioPlayer = null;
        _screenReader = null;
        GrammarPoliceMod.GrammarPoliceMod.PanicTriggered -= OnPanicTriggered;
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.Enabled) return;
        _keyBindReloadTimer -= Time.unscaledDeltaTime;
        if (_keyBindReloadTimer <= 0f)
        {
            _keyBindReloadTimer = 1f;
            LoadKeyBinds();
        }

        if (Input.GetKeyDown(_toggleKey))
        {
            if (_overlayActive)
                DeactivateOverlay();
            else
                ActivateOverlay();
        }

        if (Config.TriggerOnWeaponDraw && PerformanceSettings.Current.BodycamWeaponPollingAllowed)
            PollWeaponDraw();
        else
            _weaponWasDrawn = false;

        if (!_overlayActive)
        {
            PollEmergencyTrigger();
            return;
        }

        PollEmergencyTrigger();
        if (Input.GetKeyDown(_bookmarkKey))
            AddBookmark("manual", "Manual bookmark");
        if (Config.EnableLicenseScanBridge && Input.GetKeyDown(_licenseScanKey))
            TryScanDriverLicense();

        _idleTimer += Time.unscaledDeltaTime;
        if (Config.IdleAutoOffSeconds > 0 && _idleTimer >= Config.IdleAutoOffSeconds)
        {
            AddBookmark("idle-stop", "Auto-deactivated after idle timer");
            DeactivateOverlay();
            return;
        }

        _signalTimer -= Time.unscaledDeltaTime;
        if (_signalTimer <= 0f)
            PlaySignalAndResetTimer();
    }

    protected override void OnModKitGui()
    {
        if (!Config.Enabled || !_overlayActive) return;
        EnsureStyles();
        UpdateOverlayCachedText();
        DrawOverlay();
    }

    private void ActivateOverlay()
    {
        _overlayActive = true;
        _idleTimer = 0f;
        AddBookmark("activation", "Bodycam activated");
        PlaySignalAndResetTimer();
    }

    private void OnPanicTriggered()
    {
        if (!Config.TriggerOnPanic) return;
        if (!_overlayActive)
            ActivateOverlay();
        else
            AddBookmark("panic", "Panic button event");
    }

    private void PollEmergencyTrigger()
    {
        if (_emergencyTriggerTimer > 0f)
            _emergencyTriggerTimer -= Time.unscaledDeltaTime;
        else
            _emergencyTriggerPresses = 0;

        if (!Input.GetKeyDown(_emergencyTriggerKey)) return;

        if (_emergencyTriggerTimer <= 0f)
            _emergencyTriggerPresses = 0;

        _emergencyTriggerPresses++;
        _emergencyTriggerTimer = Math.Max(0.5f, Config.EmergencyTriggerWindowSeconds);

        if (_emergencyTriggerPresses >= Math.Max(1, Config.EmergencyTriggerPressCount))
        {
            _emergencyTriggerPresses = 0;
            _emergencyTriggerTimer = 0f;
            if (!_overlayActive)
                ActivateOverlay();
            else
                PlaySignalAndResetTimer();
            AddBookmark("emergency-key", "Emergency trigger key sequence");
        }
    }

    private void DeactivateOverlay()
    {
        _overlayActive = false;
        _signalTimer = Math.Max(10f, Config.SignalIntervalSeconds);
    }

    protected override void OnConfigApplied(BodyCamConfig currentConfig)
    {
        RefreshOverlayLabels();
    }

    protected override void OnConfigReloaded(BodyCamConfig previous, BodyCamConfig current)
    {
        RefreshOverlayLabels();
    }

    private void LoadKeyBinds()
    {
        _toggleKey = KeyBindStore.Load(KeyBindFile, "ToggleKey", _toggleKey);
        _emergencyTriggerKey = KeyBindStore.Load(KeyBindFile, "EmergencyTriggerKey", _emergencyTriggerKey);
        _bookmarkKey = KeyBindStore.Load(KeyBindFile, "BookmarkKey", _bookmarkKey);
        _licenseScanKey = KeyBindStore.Load(KeyBindFile, "LicenseScanKey", _licenseScanKey);
    }

    private void PlaySignalAndResetTimer()
    {
        _signalTimer = Math.Max(10f, Config.SignalIntervalSeconds);
        if (!_signalWavExists) return;
        _audioPlayer?.Play(_signalPath);
    }

    private void PollWeaponDraw()
    {
        _weaponPollTimer -= Time.unscaledDeltaTime;
        if (_weaponPollTimer <= 0f)
        {
            _weaponPollTimer = Math.Max(0.25f, Config.WeaponPollIntervalSeconds);
            DiscoverWeapon();
        }

        bool drawn = _cachedWeapon != null && _cachedWeapon.gameObject.activeInHierarchy;
        if (drawn && !_weaponWasDrawn && !_overlayActive)
        {
            ActivateOverlay();
            AddBookmark("weapon-draw", "Weapon draw detected");
        }
        _weaponWasDrawn = drawn;
    }

    private void DiscoverWeapon()
    {
        try
        {
            if (_cachedPlayer == null || !_cachedPlayer.activeInHierarchy)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");

            if (_cachedWeapon != null && _cachedWeapon.gameObject.activeInHierarchy && WeaponNames.Contains(_cachedWeapon.name))
                return;

            _cachedWeapon = null;
            if (_cachedPlayer != null)
                _cachedWeapon = FindWeapon(_cachedPlayer.transform);

            if (_cachedWeapon == null)
            {
                var cam = GameObject.Find("Main Camera");
                if (cam != null)
                    _cachedWeapon = FindWeapon(cam.transform);
            }

            if (_cachedWeapon == null)
                _cachedWeapon = FindCachedSceneWeapon();
        }
        catch { }
    }

    private Transform? FindCachedSceneWeapon()
    {
        if (Time.unscaledTime >= _nextWeaponCacheRefreshTime)
            RefreshWeaponCache();

        for (int i = _weaponCandidates.Count - 1; i >= 0; i--)
        {
            var weapon = _weaponCandidates[i];
            if (weapon == null)
            {
                _weaponCandidates.RemoveAt(i);
                continue;
            }

            if (weapon.gameObject.activeInHierarchy && WeaponNames.Contains(weapon.name))
                return weapon;
        }

        return null;
    }

    private void RefreshWeaponCache()
    {
        _nextWeaponCacheRefreshTime = Time.unscaledTime + Math.Max(5f, Config.WeaponCacheRefreshSeconds);
        _weaponCandidates.Clear();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null || !root.activeInHierarchy) continue;
            CollectWeapons(root.transform);
        }
    }

    private void CollectWeapons(Transform root)
    {
        if (WeaponNames.Contains(root.name))
        {
            _weaponCandidates.Add(root);
            return;
        }

        int count = root.childCount;
        for (int i = 0; i < count; i++)
            CollectWeapons(root.GetChild(i));
    }

    private static Transform? FindWeapon(Transform t)
    {
        int count = t.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = t.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            if (WeaponNames.Contains(child.name)) return child;
            var found = FindWeapon(child);
            if (found != null) return found;
        }
        return null;
    }

    private void DrawOverlay()
    {
        float w = 390f;
        float h = Config.ShowGpsLocation || Config.SimulateBatteryAndStorage ? 154f : 118f;
        float x = Screen.width - w - 24f;
        float y = 24f;

        GUI.DrawTexture(new Rect(x, y, w, 2), _barTex!);
        GUI.DrawTexture(new Rect(x, y + h, w, 2), _barTex!);

        GUI.Label(new Rect(x, y + 8, w - 52, 26), _cameraLabel, _topStyle);
        GUI.DrawTexture(new Rect(x + w - 44, y + 13, 10, 10), _redTex!);
        GUI.Label(new Rect(x + w - 30, y + 6, 30, 24), "REC", _recStyle);

        GUI.Label(new Rect(x, y + 38, w, 20), _clockLabel, _smallStyle);
        GUI.Label(new Rect(x, y + 61, w, 20), _agencyLabel, _smallStyle);
        GUI.Label(new Rect(x, y + 84, w, 20), _metaLabel, _smallStyle);
        if (Config.SimulateBatteryAndStorage)
            GUI.Label(new Rect(x, y + 105, w, 20), _resourceLabel, _smallStyle);
        if (Config.ShowGpsLocation)
            GUI.Label(new Rect(x, y + 126, w, 20), _gpsLabel, _smallStyle);
    }

    private void RefreshOverlayLabels()
    {
        _cameraLabel = Config.CameraId.ToUpperInvariant();
        _agencyLabel = Config.Agency.ToUpperInvariant();
        _metaLabel = $"{Config.OfficerName.ToUpperInvariant()} | {Config.UnitId.ToUpperInvariant()} | {Config.CameraMode.ToUpperInvariant()}";
        _lastClockSecond = -1;
        _lastResourceSecond = -1;
        _nextGpsRefreshTime = 0f;
    }

    private void UpdateOverlayCachedText()
    {
        int nowSecond = (int)Time.realtimeSinceStartup;
        if (nowSecond != _lastClockSecond)
        {
            _lastClockSecond = nowSecond;
            _clockLabel = DateTime.Now.ToString(ClockFormat);
        }

        if (Config.SimulateBatteryAndStorage && nowSecond != _lastResourceSecond)
        {
            _lastResourceSecond = nowSecond;
            _resourceLabel = $"BAT {SimBattery()}% | STORAGE {SimStorage()} GB";
        }

        if (Config.ShowGpsLocation && Time.unscaledTime >= _nextGpsRefreshTime)
        {
            _nextGpsRefreshTime = Time.unscaledTime + 0.5f;
            _gpsLabel = "GPS " + GetLocationLabel();
        }
    }

    private void AddBookmark(string eventType, string note)
    {
        try
        {
            float gpsX = 0f, gpsZ = 0f;
            var player = GetCachedPlayer();
            if (player != null)
            {
                var p = player.transform.position;
                gpsX = p.x;
                gpsZ = p.z;
            }

            BodyCamEvidenceStore.AddBookmark(new BodyCamBookmark(
                DateTime.Now,
                Config.UnitId,
                Config.CameraId,
                Config.CameraMode,
                eventType,
                note,
                GetLocationLabel(),
                gpsX,
                gpsZ));
            if (Config.DebugLogging)
                LogDebug($"Bodycam bookmark: {eventType} - {note}");
        }
        catch { }
    }

    private void TryScanDriverLicense()
    {
        if (!_overlayActive)
            return;

        var scan = _screenReader?.Scan();
        if (scan == null || string.IsNullOrWhiteSpace(scan.RawText))
        {
            AddBookmark("license-scan-empty", "No readable screen text found while bodycam was active");
            return;
        }

        BodyCamEvidenceStore.SaveLicenseScan(scan);
        AddBookmark("license-scan", $"Scanned license for {scan.FirstName} {scan.LastName}".Trim());
    }

    private string GetLocationLabel()
    {
        var player = GetCachedPlayer();
        if (player == null) return "UNKNOWN";
        var p = player.transform.position;
        return $"{p.x:0.0},{p.z:0.0}";
    }

    private GameObject? GetCachedPlayer()
    {
        if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy)
        {
            if (Time.unscaledTime < _nextPlayerCacheTime)
                return _cachedPlayer;
        }

        _cachedPlayer = GameObject.FindGameObjectWithTag("Player");
        _nextPlayerCacheTime = Time.unscaledTime + 2f;
        return _cachedPlayer;
    }

    private static int SimBattery() =>
        Math.Max(5, 100 - (int)(Time.realtimeSinceStartup / 180f));

    private static int SimStorage() =>
        Math.Max(1, 64 - (int)(Time.realtimeSinceStartup / 300f));

    private static void EnsureStyles()
    {
        if (_topStyle != null) return;
        _topStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        _recStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(1f, 0.18f, 0.12f) }
        };
        _barTex = MakeTex(new Color(1f, 1f, 1f, 0.85f));
        _redTex = MakeTex(new Color(1f, 0.05f, 0.02f, 1f));
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.hideFlags = HideFlags.DontSave;
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private static void EnsureSignalWav(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            string versionPath = path + ".version";
            if (File.Exists(path) &&
                File.Exists(versionPath) &&
                File.ReadAllText(versionPath).Trim().Equals(SignalVersion, StringComparison.Ordinal))
                return;

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            const int sampleRate = 44100;
            short[] samples = BuildSignalSamples(sampleRate);
            int dataBytes = samples.Length * sizeof(short);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (short sample in samples)
                writer.Write(sample);
            File.WriteAllText(versionPath, SignalVersion);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[BodyCamOverlay] Could not create signal wav: {ex.Message}");
        }
    }

    private static short[] BuildSignalSamples(int sampleRate)
    {
        var samples = new List<short>();
        AddRaspyTone(samples, sampleRate, 820, 0.13, 6.5, 18);
        AddSilence(samples, sampleRate, 0.045);
        AddRaspyTone(samples, sampleRate, 1120, 0.12, 7.5, 26);
        AddSilence(samples, sampleRate, 0.035);
        AddRaspyTone(samples, sampleRate, 760, 0.10, 8.5, 22);
        return samples.ToArray();
    }

    private static void AddRaspyTone(List<short> samples, int sampleRate, double frequency, double seconds, double vibratoHz, double vibratoDepth)
    {
        int count = (int)(sampleRate * seconds);
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)sampleRate;
            double envelope = Math.Min(1.0, Math.Min(i / 300.0, (count - i) / 300.0));
            double wobble = Math.Sin(2 * Math.PI * vibratoHz * t) * vibratoDepth;
            double f = frequency + wobble;
            double fundamental = Math.Sin(2 * Math.PI * f * t);
            double bite = Math.Sin(2 * Math.PI * f * 2.02 * t) * 0.34;
            double grit = Math.Sign(Math.Sin(2 * Math.PI * f * 0.51 * t)) * 0.10;
            double tremolo = 0.80 + 0.20 * Math.Sin(2 * Math.PI * 18 * t);
            double value = (fundamental + bite + grit) * 0.26 * envelope * tremolo;
            samples.Add((short)(value * short.MaxValue));
        }
    }

    private static void AddSilence(List<short> samples, int sampleRate, double seconds)
    {
        int count = (int)(sampleRate * seconds);
        for (int i = 0; i < count; i++)
            samples.Add(0);
    }
}
