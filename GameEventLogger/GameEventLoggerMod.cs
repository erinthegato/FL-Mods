using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using FlashingLights.ModKit.Core;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameEventLogger;

[ModKitManifest(
    Id = "game-event-logger",
    DisplayName = "Game Event Logger",
    Version = "2.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class GameEventLoggerMod : ModKitMelonMod<LoggerConfig>
{
    protected override string ModId => "game-event-logger";

    // ── Weapon panic ──
    private float _weaponTimer;
    private const float WeaponInterval = 0.5f;
    private GameObject? _cachedPlayer;
    private Transform? _cachedWeapon;
    private int _shotCount;
    private bool _panicFired;
    private float _panicCooldownTimer;
    private string _panicButtonDir = "";
    private string _code99Dir = "";
    private string[] _panicToneFiles = Array.Empty<string>();
    private string[] _code99Files = Array.Empty<string>();
    private bool _loggedMissingPanicTone;
    private bool _loggedMissingCode99;
    private float _nextWeaponCacheRefreshTime;
    private readonly List<Transform> _weaponCandidates = new();
    private MethodInfo? _triggerPanic;
    private object? _gpInstance;
    private static readonly string[] WeaponNames = { "Gun_AP58", "Wep_Pistol_01" };

    // ═══════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════

    protected override void OnModKitInitialized()
    {
        CacheReflectionHandles();
        CachePanicAudioPaths();
    }

    protected override void OnModKitUpdate()
    {
        if (Config.PanicAlarmEnabled)
            PollPanicInput();

        if (_panicCooldownTimer > 0f)
        {
            _panicCooldownTimer -= Time.unscaledDeltaTime;
            if (_panicCooldownTimer <= 0f)
                _panicFired = false;
        }
    }

    protected override void OnModKitDisabled()
    {
        _panicFired = false;
        _panicCooldownTimer = 0f;
        _cachedWeapon = null;
        _cachedPlayer = null;
        _shotCount = 0;
        _weaponCandidates.Clear();
    }

    // ═══════════════════════════════════════════════════
    //  REFLECTION CACHE (GrammarPoliceMod)
    // ═══════════════════════════════════════════════════

    private void CacheReflectionHandles()
    {
        try
        {
            var gpType = System.Type.GetType("GrammarPoliceMod.GrammarPoliceMod, GrammarPoliceMod");
            if (gpType == null) return;

            var instProp = gpType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            _gpInstance = instProp?.GetValue(null);
            if (_gpInstance == null) return;

            _triggerPanic = gpType.GetMethod("TriggerPanic",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }
        catch { }
    }


    // ═══════════════════════════════════════════════════
    //  WEAPON PANIC
    // ═══════════════════════════════════════════════════

    private void PollPanicInput()
    {
        _weaponTimer -= Time.unscaledDeltaTime;
        if (_weaponTimer <= 0f)
        {
            _weaponTimer = WeaponInterval;
            DiscoverWeapon();
        }

        if (_panicFired) return;

        if (_cachedWeapon != null && _cachedWeapon.gameObject.activeInHierarchy)
        {
            if (Input.GetMouseButtonDown(0))
            {
                _shotCount++;
                if (_shotCount >= Config.ShotsToPanic)
                    FirePanic(_cachedWeapon.name);
            }
        }
        else
        {
            _shotCount = 0;
        }
    }

    private void DiscoverWeapon()
    {
        try
        {
            if (_cachedPlayer == null)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");

            if (_cachedWeapon != null && _cachedWeapon.gameObject.activeInHierarchy)
            {
                var n = _cachedWeapon.name;
                if (IsWeaponName(n))
                    return;
            }
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

            if (_cachedWeapon == null)
                _panicFired = false;
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
        _nextWeaponCacheRefreshTime = Time.unscaledTime + 2.5f;
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
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            var n = child.name;
            if (IsWeaponName(n))
                return child;
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

    private void FirePanic(string weapon)
    {
        _panicFired = true;
        _panicCooldownTimer = Math.Max(3f, Config.PanicDuration);

        MelonLogger.Msg($"[GameEventLogger] PANIC: {weapon} fired {Config.ShotsToPanic} rounds - dispatching backup");

        _ = PlayPanicSequenceAsync();
        try { _triggerPanic?.Invoke(_gpInstance, null); } catch { }

        DispatchNativeBackup();
    }

    private void CachePanicAudioPaths()
    {
        string root = FindGameRoot();
        _panicButtonDir = Path.Combine(root, "DispatchAudio", "Panic Button");
        _code99Dir = Path.Combine(_panicButtonDir, "Code99");
        _panicToneFiles = LoadWavs(_panicButtonDir);
        _code99Files = LoadWavs(_code99Dir);
    }

    private async Task PlayPanicSequenceAsync()
    {
        string? tone = PickRandomWav(_panicToneFiles);
        if (tone != null)
            await PlayWavSyncAsync(tone);
        else if (!_loggedMissingPanicTone)
        {
            _loggedMissingPanicTone = true;
            MelonLogger.Warning($"[GameEventLogger] No panic button tones found in {_panicButtonDir}");
        }

        string? code99 = PickRandomWav(_code99Files);
        if (code99 != null)
            await PlayWavSyncAsync(code99);
        else if (!_loggedMissingCode99)
        {
            _loggedMissingCode99 = true;
            MelonLogger.Warning($"[GameEventLogger] No Code99 files found in {_code99Dir}");
        }
    }

    private static string[] LoadWavs(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? PickRandomWav(string[] files)
    {
        if (files.Length == 0) return null;
        return files[UnityEngine.Random.Range(0, files.Length)];
    }

    private static async Task PlayWavSyncAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell")
                {
                    Arguments = $"-NoProfile -Command \"(New-Object Media.SoundPlayer '{path.Replace("'", "''")}').PlaySync()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameEventLogger] Panic audio failed: {ex.Message}");
            }
        });
    }


    private void DispatchNativeBackup()
    {
        try
        {
            string[] targets =
            {
                "DispatchManager", "BackupManager", "AIDispatcher",
                "DispatchSystem", "PoliceDispatch", "BackupSystem"
            };

            bool dispatched = false;
            foreach (var target in targets)
            {
                var go = GameObject.Find(target);
                if (go == null) continue;

                go.SendMessage("DispatchBackup", SendMessageOptions.DontRequireReceiver);
                go.SendMessage("RequestBackup", SendMessageOptions.DontRequireReceiver);
                go.SendMessage("OnPanic", SendMessageOptions.DontRequireReceiver);
                dispatched = true;
                break;
            }

            if (!dispatched)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (root == null) continue;
                        var n = root.name;
                        if (n.Contains("Dispatch", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("Backup", StringComparison.OrdinalIgnoreCase))
                        {
                            root.SendMessage("DispatchBackup", SendMessageOptions.DontRequireReceiver);
                            root.SendMessage("RequestBackup", SendMessageOptions.DontRequireReceiver);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] Native dispatch failed: {ex.Message}");
        }
    }

    private static string FindGameRoot()
    {
        try
        {
            var loc = Assembly.GetExecutingAssembly().Location;
            if (loc != null)
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(loc)!);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "flashinglights.exe")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
        }
        catch { }
        return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
    }
}
