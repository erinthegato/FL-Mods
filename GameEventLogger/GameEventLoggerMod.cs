using System.IO;
using System.Reflection;
using FlashingLights.ModKit.Core;
using MelonLoader;
using UnityEngine;

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


    // ── Panic overlay ──
    private float _panicFlashTimer;

    // ── Reflection handles (cached) ──
    private MethodInfo? _playPanicTone;
    private MethodInfo? _triggerPanic;
    private object? _gpInstance;

    // ── Crash hooks ──
    private static string? _crashLogPath;
    private const string CrashLogName = "CrashLog.txt";
    private Action<string, string, LogType>? _logCallback;
    private static int _crashLogCount;
    private static DateTime _crashLogWindowStart = DateTime.MinValue;
    private const int MaxCrashLogRate = 50;

    private static readonly string[] NoisePatterns =
    {
        "Releasing render texture that is set as Camera.targetTexture",
        "Releasing render texture that is set to be Render Texture",
        "The character controller does not support swimming",
        "The referenced script on this Behaviour",
        "Can't add component",
        "Shader warning",
        "D3D11: ID3D11DeviceContext::DrawIndexed",
        "D3D11: Removing Device",
        "Trying to add InputManager",
        "The file '",
        "Could not find file",
        "Unable to find script",
        "NullReferenceException",
    };

    // ═══════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════

    protected override void OnModKitInitialized()
    {
        SetupCrashHooks();
        CacheReflectionHandles();
        MelonLogger.Msg($"[GameEventLogger] persistentDataPath: {Application.persistentDataPath}");
        MelonLogger.Msg("[GameEventLogger] Initialized. Panic alarm active.");
    }

    protected override void OnModKitUpdate()
    {
        if (Config.PanicAlarmEnabled)
            PollPanicInput();

        if (_panicFlashTimer > 0f)
            _panicFlashTimer -= Time.unscaledDeltaTime;
    }

    protected override void OnModKitGui()
    {
        if (_panicFlashTimer > 0f)
            DrawPanicOverlay();
    }

    protected override void OnModKitDisabled()
    {
        _panicFired = false;
        _panicFlashTimer = 0f;
        _cachedWeapon = null;
        _cachedPlayer = null;
        _shotCount = 0;
        if (_logCallback != null)
            Application.remove_logMessageReceived(_logCallback);

        MelonLogger.Msg("[GameEventLogger] Disabled.");
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

            var daProp = gpType.GetProperty("DispatchAudio",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var dispatchAudio = daProp?.GetValue(_gpInstance);
            if (dispatchAudio != null)
            {
                _playPanicTone = dispatchAudio.GetType().GetMethod("PlayPanicTone",
                    new[] { typeof(string) });
            }

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
                if (n.Contains("Gun_AP58") || n.Contains("Wep_Pistol_01"))
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
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (root == null) continue;
                        _cachedWeapon = FindWeapon(root.transform);
                        if (_cachedWeapon != null) break;
                    }
                }
            }

            if (_cachedWeapon == null)
                _panicFired = false;
        }
        catch { }
    }

    private static Transform? FindWeapon(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            var n = child.name;
            if (n.Contains("Gun_AP58") || n.Contains("Wep_Pistol_01"))
                return child;
            var found = FindWeapon(child);
            if (found != null) return found;
        }
        return null;
    }

    private void FirePanic(string weapon)
    {
        _panicFired = true;
        _panicFlashTimer = Config.PanicDuration;

        MelonLogger.Msg($"[GameEventLogger] PANIC: {weapon} fired {Config.ShotsToPanic} rounds — dispatching backup");

        try { _playPanicTone?.Invoke(null, new object[] { "" }); } catch { }
        try { _triggerPanic?.Invoke(_gpInstance, null); } catch { }

        DispatchNativeBackup();
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


    // ═══════════════════════════════════════════════════
    //  PANIC OVERLAY
    // ═══════════════════════════════════════════════════

    private static GUIStyle? _panicStyle;
    private static GUIStyle? _panicSubStyle;
    private static Texture2D? _panicOverlay;

    private static void EnsurePanicStyles()
    {
        if (_panicOverlay != null) return;

        _panicOverlay = new Texture2D(1, 1);
        _panicOverlay.SetPixel(0, 0, new Color(1f, 0f, 0f, 0.3f));
        _panicOverlay.Apply();

        _panicStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.red }
        };
        _panicSubStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.yellow }
        };
    }

    private void DrawPanicOverlay()
    {
        EnsurePanicStyles();

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _panicOverlay, ScaleMode.StretchToFill);

        if (_panicStyle == null) return;
        float pulse = Mathf.PingPong(Time.unscaledTime * 4f, 1f);
        _panicStyle.normal.textColor = Color.Lerp(Color.red, Color.white, pulse);

        float cy = Screen.height * 0.28f;
        GUI.Label(new Rect(0, cy, Screen.width, 60), "PANIC ALARM", _panicStyle);
        GUI.Label(new Rect(0, cy + 70, Screen.width, 40), "BACKUP DISPATCHED", _panicSubStyle);
    }

    // ═══════════════════════════════════════════════════
    //  CRASH HOOKS
    // ═══════════════════════════════════════════════════

    private void SetupCrashHooks()
    {
        try
        {
            _crashLogPath = Path.Combine(FindGameRoot(), CrashLogName);
            File.WriteAllText(_crashLogPath, $"=== Crash Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }

        try
        {
            _logCallback = new Action<string, string, LogType>(OnUnityLogMessage);
            Application.add_logMessageReceived(_logCallback);
        }
        catch { }
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

    private static bool IsNoise(string condition)
    {
        foreach (var p in NoisePatterns)
            if (condition.StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
                condition.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsRateLimited()
    {
        var now = DateTime.UtcNow;
        if (now - _crashLogWindowStart > TimeSpan.FromSeconds(30))
        {
            _crashLogWindowStart = now;
            _crashLogCount = 0;
        }
        _crashLogCount++;
        return _crashLogCount > MaxCrashLogRate;
    }

    private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            return;
        if (IsNoise(condition) || IsRateLimited()) return;

        try
        {
            File.AppendAllText(_crashLogPath!, $"[Unity {type}] {condition}\n");
            if (type == LogType.Exception || stackTrace.Length > 50)
                File.AppendAllText(_crashLogPath!, $"{stackTrace}\n");
        }
        catch { }
    }
}
