using System.Globalization;
using System.IO;
using System.Reflection;
using FlashingLights.ModKit.Core;
using GrammarPoliceMod;
using MDTMod;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

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
    internal static GameEventLoggerMod Instance { get; private set; } = null!;

    // ── Weapon panic ──
    private float _weaponTimer;
    private const float WeaponInterval = 0.25f;
    private string? _lastWeapon;
    private bool _panicFired;

    // ── AI names → MDT ──
    private readonly HashSet<string> _seenAINames = new();
    private float _sceneTimer;
    private const float SceneInterval = 1.5f;

    // ── Name lists ──
    private string[] _firstNames = Array.Empty<string>();
    private string[] _lastNames = Array.Empty<string>();

    // ── Panic on-screen warning ──
    private float _panicFlashTimer;
    private float _panicFlashDuration = 0.1f;
    private bool _panicFlashOn;

    // ── Crash hooks ──
    private static string? _crashLogPath;
    private const string CrashLogName = "CrashLog.txt";
    private Action<string, string, LogType>? _logCallback;
    private static readonly List<string> CrashBuffer = new();
    private const int CrashBufferMax = 100;
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
        "NullReferenceException: Object reference not set to an instance of an object",
    };

    // ═══════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════

    protected override void OnModKitInitialized()
    {
        Instance = this;
        LoadNameFiles();
        SetupCrashHooks();
        MelonLogger.Msg("[GameEventLogger] Initialized. Panic alarm + MDT NPC records active.");
    }

    protected override void OnModKitUpdate()
    {
        if (Config.PanicAlarmEnabled)
            CheckWeaponPanic();

        if (Config.NpcRecordEnabled)
        {
            _sceneTimer -= Time.unscaledDeltaTime;
            if (_sceneTimer <= 0f)
            {
                _sceneTimer = SceneInterval;
                ScanForAINames();
            }
        }

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
        _lastWeapon = null;

        if (_logCallback != null)
            Application.remove_logMessageReceived(_logCallback);

        MelonLogger.Msg("[GameEventLogger] Disabled.");
    }

    // ═══════════════════════════════════════════════════
    //  NAME FILES
    // ═══════════════════════════════════════════════════

    private void LoadNameFiles()
    {
        try
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

            string fnPath = Path.Combine(dir, "AI_First_names.txt");
            string lnPath = Path.Combine(dir, "Ai_Last_Names.txt");

            if (File.Exists(fnPath))
                _firstNames = File.ReadAllLines(fnPath);
            if (File.Exists(lnPath))
                _lastNames = File.ReadAllLines(lnPath);

            MelonLogger.Msg($"[GameEventLogger] Loaded {_firstNames.Length} first names, {_lastNames.Length} last names");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] Could not load name files: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════
    //  WEAPON PANIC
    // ═══════════════════════════════════════════════════

    private void CheckWeaponPanic()
    {
        _weaponTimer -= Time.unscaledDeltaTime;
        if (_weaponTimer > 0f) return;
        _weaponTimer = WeaponInterval;

        try
        {
            string? found = null;
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;
                var n = obj.name;
                if (n.Contains("Gun_AP58") || n.Contains("Wep_Pistol_01"))
                {
                    found = n;
                    break;
                }
            }

            if (found != null && found != _lastWeapon && !_panicFired)
                FirePanic(found);

            if (found == null)
            {
                _lastWeapon = null;
                _panicFired = false;
            }
            else
            {
                _lastWeapon = found;
            }
        }
        catch { }
    }

    private void FirePanic(string weapon)
    {
        _panicFired = true;
        _panicFlashTimer = _panicFlashDuration;

        MelonLogger.Msg($"[GameEventLogger] ⚠ PANIC: {weapon} drawn — dispatching backup");

        // 1. Audio panic via GrammarPoliceMod
        try
        {
            var gp = GrammarPoliceMod.GrammarPoliceMod.Instance;
            if (gp != null)
                gp.DispatchAudio.PlayPanicTone("");
        }
        catch { }

        // 2. On-screen warning
        _panicFlashTimer = 5f;

        // 3. Native Flashing Lights AI backup dispatch
        DispatchNativeBackup();
    }

    private void DispatchNativeBackup()
    {
        try
        {
            // Try common dispatch/backup manager names
            string[] targets =
            {
                "DispatchManager", "BackupManager", "AIDispatcher",
                "DispatchSystem", "PoliceDispatch", "BackupSystem"
            };

            foreach (var target in targets)
            {
                var go = GameObject.Find(target);
                if (go == null) continue;

                // Try SendMessage or BroadcastMessage to any component
                go.SendMessage("DispatchBackup", SendMessageOptions.DontRequireReceiver);
                go.SendMessage("RequestBackup", SendMessageOptions.DontRequireReceiver);
                go.SendMessage("OnPanic", SendMessageOptions.DontRequireReceiver);
                MelonLogger.Msg($"[GameEventLogger] Sent dispatch to {target}");
                break;
            }

            // Also try finding objects with "Dispatch" or "Backup" in name
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;
                var n = obj.name;
                if (n.Contains("Dispatch", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Backup", StringComparison.OrdinalIgnoreCase))
                {
                    obj.SendMessage("DispatchBackup", SendMessageOptions.DontRequireReceiver);
                    obj.SendMessage("RequestBackup", SendMessageOptions.DontRequireReceiver);
                    MelonLogger.Msg($"[GameEventLogger] Sent dispatch to {obj.name}");
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] Native dispatch failed: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════
    //  AI NAMES → MDT NPC RECORDS
    // ═══════════════════════════════════════════════════

    private void ScanForAINames()
    {
        try
        {
            if (_seenAINames.Count > 500) _seenAINames.Clear();

            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;
                var name = obj.name;

                if ((name.Contains("AI-Names-M generated") || name.Contains("AI-Names-F generated")) &&
                    _seenAINames.Add(name))
                {
                    MelonLogger.Msg($"[GameEventLogger] AI Names generated: \"{name}\"");
                    CreateNpcRecord(name);
                }
            }
        }
        catch { }
    }

    private void CreateNpcRecord(string sourceName)
    {
        string npcName = GenerateNpcName();
        if (string.IsNullOrEmpty(npcName)) return;

        string reg = "";
        bool insurance = false;
        bool firearmsLicense = false;
        bool wanted = false;
        bool missing = false;

        if (Config.RandomizeNpcData)
        {
            reg = GeneratePlate();
            insurance = Random.value > 0.15f;
            firearmsLicense = Random.value > 0.65f;
            wanted = Random.value < 0.10f;
            missing = Random.value < 0.04f;
        }

        var info = new NPCInfo(npcName, reg, insurance, firearmsLicense, wanted, missing, DateTime.Now);
        NPCDataStore.AddNpcRecord(info);
    }

    private string GenerateNpcName()
    {
        if (_firstNames.Length == 0 || _lastNames.Length == 0)
            return $"Civilian_{Random.Range(100, 999)}";

        string fn = _firstNames[Random.Range(0, _firstNames.Length)];
        string ln = _lastNames[Random.Range(0, _lastNames.Length)];
        return $"{fn} {ln}";
    }

    private static string GeneratePlate()
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string nums = "0123456789";
        char l1 = letters[Random.Range(0, 26)];
        char l2 = letters[Random.Range(0, 26)];
        char l3 = letters[Random.Range(0, 26)];
        char n1 = nums[Random.Range(0, 10)];
        char n2 = nums[Random.Range(0, 10)];
        char n3 = nums[Random.Range(0, 10)];
        char n4 = nums[Random.Range(0, 10)];
        return $"{l1}{l2}{l3}-{n1}{n2}{n3}{n4}";
    }

    // ═══════════════════════════════════════════════════
    //  PANIC OVERLAY
    // ═══════════════════════════════════════════════════

    private static GUIStyle? _panicStyle;
    private static GUIStyle? _panicSubStyle;
    private static Texture2D? _panicOverlay;

    private void DrawPanicOverlay()
    {
        if (_panicOverlay == null)
        {
            _panicOverlay = new Texture2D(1, 1);
            _panicOverlay.SetPixel(0, 0, new Color(1f, 0f, 0f, 0.3f));
            _panicOverlay.Apply();
        }

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _panicOverlay, ScaleMode.StretchToFill);

        if (_panicStyle == null)
        {
            _panicStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.red }
            };
            _panicSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.yellow }
            };
        }

        float pulse = Mathf.PingPong(Time.unscaledTime * 4f, 1f);
        _panicStyle.normal.textColor = Color.Lerp(Color.red, Color.white, pulse);

        GUI.Label(new Rect(0, Screen.height * 0.28f, Screen.width, 60), "⚠ PANIC ALARM ⚠", _panicStyle);
        GUI.Label(new Rect(0, Screen.height * 0.28f + 70, Screen.width, 40), "BACKUP DISPATCHED", _panicSubStyle);
    }

    // ═══════════════════════════════════════════════════
    //  CRASH HOOKS (kept from original)
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
