using System.IO;
using System.Text;
using System.Reflection;
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
    private static readonly string[] WeaponNames = { "Gun_AP58", "Wep_Pistol_01" };

    private GameObject? _cachedPlayer;
    private Transform? _cachedWeapon;
    private readonly List<Transform> _weaponCandidates = new();
    private bool _weaponWasDrawn;
    private bool _overlayActive;
    private float _weaponPollTimer;
    private float _nextWeaponCacheRefreshTime;
    private float _signalTimer;
    private float _emergencyTriggerTimer;
    private int _emergencyTriggerPresses;
    private string _signalPath = "";
    private bool _isPlayingSignal;
    private RuntimeAudioPlayer? _audioPlayer;

    private static GUIStyle? _topStyle;
    private static GUIStyle? _smallStyle;
    private static GUIStyle? _recStyle;
    private static Texture2D? _barTex;
    private static Texture2D? _redTex;

    protected override void OnModKitInitialized()
    {
        _signalPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "AxonSignal.wav");
        EnsureSignalWav(_signalPath);
        _audioPlayer = new RuntimeAudioPlayer(
            debugLog: msg => { if (Config.DebugLogging) LogDebug(msg); },
            warningLog: msg => MelonLogger.Warning($"[BodyCamOverlay] {msg}"));
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
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.Enabled) return;

        if (Input.GetKeyDown(Config.ToggleKey))
        {
            if (_overlayActive)
                DeactivateOverlay();
            else
                ActivateOverlay();
        }

        PollEmergencyTrigger();

        if (Config.TriggerOnWeaponDraw && PerformanceSettings.Current.BodycamWeaponPollingAllowed)
            PollWeaponDraw();
        else
            _weaponWasDrawn = false;

        if (!_overlayActive) return;

        _signalTimer -= Time.unscaledDeltaTime;
        if (_signalTimer <= 0f)
            PlaySignalAndResetTimer();
    }

    protected override void OnModKitGui()
    {
        if (!Config.Enabled || !_overlayActive) return;
        EnsureStyles();
        DrawOverlay();
    }

    private void ActivateOverlay()
    {
        _overlayActive = true;
        PlaySignalAndResetTimer();
    }

    private void PollEmergencyTrigger()
    {
        if (_emergencyTriggerTimer > 0f)
            _emergencyTriggerTimer -= Time.unscaledDeltaTime;
        else
            _emergencyTriggerPresses = 0;

        if (!Input.GetKeyDown(Config.EmergencyTriggerKey)) return;

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
        }
    }

    private void DeactivateOverlay()
    {
        _overlayActive = false;
        _signalTimer = Math.Max(10f, Config.SignalIntervalSeconds);
    }

    private void PlaySignalAndResetTimer()
    {
        _signalTimer = Math.Max(10f, Config.SignalIntervalSeconds);
        if (_isPlayingSignal || !File.Exists(_signalPath)) return;
        _isPlayingSignal = true;
        _audioPlayer?.Play(_signalPath);
        _isPlayingSignal = false;
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
            ActivateOverlay();
        _weaponWasDrawn = drawn;
    }

    private void DiscoverWeapon()
    {
        try
        {
            if (_cachedPlayer == null || !_cachedPlayer.activeInHierarchy)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");

            if (_cachedWeapon != null && _cachedWeapon.gameObject.activeInHierarchy && IsWeaponName(_cachedWeapon.name))
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

            if (weapon.gameObject.activeInHierarchy && IsWeaponName(weapon.name))
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
        if (IsWeaponName(root.name))
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
            if (IsWeaponName(child.name)) return child;
            var found = FindWeapon(child);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsWeaponName(string name)
    {
        foreach (var weaponName in WeaponNames)
            if (name.Contains(weaponName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void DrawOverlay()
    {
        float w = 390f;
        float h = 118f;
        float x = Screen.width - w - 24f;
        float y = 24f;

        GUI.DrawTexture(new Rect(x, y, w, 2), _barTex!);
        GUI.DrawTexture(new Rect(x, y + h, w, 2), _barTex!);

        GUI.Label(new Rect(x, y + 8, w - 52, 26), Config.CameraId.ToUpperInvariant(), _topStyle);
        GUI.DrawTexture(new Rect(x + w - 44, y + 13, 10, 10), _redTex!);
        GUI.Label(new Rect(x + w - 30, y + 6, 30, 24), "REC", _recStyle);

        string clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        GUI.Label(new Rect(x, y + 38, w, 20), clock, _smallStyle);
        GUI.Label(new Rect(x, y + 61, w, 20), Config.Agency.ToUpperInvariant(), _smallStyle);
        GUI.Label(new Rect(x, y + 84, w, 20), $"{Config.OfficerName.ToUpperInvariant()}  |  {Config.UnitId.ToUpperInvariant()}", _smallStyle);

    }

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
        if (File.Exists(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
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
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[BodyCamOverlay] Could not create signal wav: {ex.Message}");
        }
    }

    private static short[] BuildSignalSamples(int sampleRate)
    {
        var samples = new List<short>();
        AddTone(samples, sampleRate, 880, 0.11);
        AddSilence(samples, sampleRate, 0.055);
        AddTone(samples, sampleRate, 1175, 0.11);
        AddSilence(samples, sampleRate, 0.04);
        AddTone(samples, sampleRate, 880, 0.08);
        return samples.ToArray();
    }

    private static void AddTone(List<short> samples, int sampleRate, double frequency, double seconds)
    {
        int count = (int)(sampleRate * seconds);
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)sampleRate;
            double envelope = Math.Min(1.0, Math.Min(i / 300.0, (count - i) / 300.0));
            double value = Math.Sin(2 * Math.PI * frequency * t) * 0.32 * envelope;
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
