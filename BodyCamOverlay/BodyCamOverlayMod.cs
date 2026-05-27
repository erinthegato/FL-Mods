using System.Diagnostics;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using FlashingLights.ModKit.Core;
using MelonLoader;
using UnityEngine;

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

    private const float WeaponPollInterval = 0.25f;
    private static readonly string[] WeaponNames = { "Gun_AP58", "Wep_Pistol_01" };

    private GameObject? _cachedPlayer;
    private Transform? _cachedWeapon;
    private bool _weaponWasDrawn;
    private bool _overlayActive;
    private float _weaponPollTimer;
    private float _signalTimer;
    private float _emergencyTriggerTimer;
    private int _emergencyTriggerPresses;
    private string _signalPath = "";
    private bool _isPlayingSignal;

    private static GUIStyle? _topStyle;
    private static GUIStyle? _smallStyle;
    private static GUIStyle? _recStyle;
    private static Texture2D? _barTex;
    private static Texture2D? _redTex;

    protected override void OnModKitInitialized()
    {
        Config.ToggleKey = KeyBindStore.Load(KeyBindFile, nameof(Config.ToggleKey), Config.ToggleKey);
        Config.EmergencyTriggerKey = KeyBindStore.Load(KeyBindFile, nameof(Config.EmergencyTriggerKey), Config.EmergencyTriggerKey);
        _signalPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "AxonSignal.wav");
        EnsureSignalWav(_signalPath);
    }

    protected override void OnModKitDisabled()
    {
        _overlayActive = false;
        _weaponWasDrawn = false;
        _cachedWeapon = null;
        _cachedPlayer = null;
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.Enabled) return;
        if (KeyBindWidget.IsCapturing) return;

        if (Input.GetKeyDown(Config.ToggleKey))
        {
            if (_overlayActive)
                DeactivateOverlay();
            else
                ActivateOverlay();
        }

        PollEmergencyTrigger();

        if (Config.TriggerOnWeaponDraw)
            PollWeaponDraw();

        if (!_overlayActive) return;

        _signalTimer -= Time.unscaledDeltaTime;
        if (_signalTimer <= 0f)
            PlaySignalAndResetTimer();
    }

    protected override void OnModKitGui()
    {
        if (!Config.Enabled) return;
        EnsureStyles();

        if (_overlayActive)
            DrawOverlay();
        else
            DrawCollapsedKeyBind();
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
        _ = PlaySignalAsync();
    }

    private async Task PlaySignalAsync()
    {
        _isPlayingSignal = true;
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell")
                {
                    Arguments = $"-NoProfile -Command \"(New-Object Media.SoundPlayer '{_signalPath.Replace("'", "''")}').PlaySync()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BodyCamOverlay] Signal audio failed: {ex.Message}");
            }
        });
        _isPlayingSignal = false;
    }

    private void PollWeaponDraw()
    {
        _weaponPollTimer -= Time.unscaledDeltaTime;
        if (_weaponPollTimer <= 0f)
        {
            _weaponPollTimer = WeaponPollInterval;
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
        }
        catch { }
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

        Config.ToggleKey = KeyBindWidget.Draw(
            new Rect(x, y + h + 8, 170, 22),
            KeyBindFile,
            nameof(Config.ToggleKey),
            "Bodycam Toggle",
            Config.ToggleKey);
        Config.EmergencyTriggerKey = KeyBindWidget.Draw(
            new Rect(x + 178, y + h + 8, 190, 22),
            KeyBindFile,
            nameof(Config.EmergencyTriggerKey),
            "Emergency Trigger",
            Config.EmergencyTriggerKey);
    }

    private void DrawCollapsedKeyBind()
    {
        Config.ToggleKey = KeyBindWidget.Draw(
            new Rect(Screen.width - 200, 24, 176, 22),
            KeyBindFile,
            nameof(Config.ToggleKey),
            "Bodycam Toggle",
            Config.ToggleKey);
        Config.EmergencyTriggerKey = KeyBindWidget.Draw(
            new Rect(Screen.width - 200, 50, 176, 22),
            KeyBindFile,
            nameof(Config.EmergencyTriggerKey),
            "Emergency Trigger",
            Config.EmergencyTriggerKey);
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
