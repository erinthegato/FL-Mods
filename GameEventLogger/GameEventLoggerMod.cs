using System.IO;
using System.Reflection;
using FlashingLights.ModKit.Core;
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

    // ── Weapon panic ──
    private float _weaponTimer;
    private const float WeaponInterval = 0.5f;
    private string? _lastWeapon;
    private bool _panicFired;

    // ── AI names → MDT ──
    private readonly HashSet<string> _seenAINames = new();
    private float _sceneTimer;
    private const float SceneInterval = 1.5f;
    private string[] _firstNames = Array.Empty<string>();
    private string[] _lastNames = Array.Empty<string>();

    // ── Panic overlay ──
    private float _panicFlashTimer;

    // ── Reflection handles (cached) ──
    private MethodInfo? _playPanicTone;
    private MethodInfo? _triggerPanic;
    private object? _gpInstance;
    private bool _flSaveDataSearched;
    private object? _flSaveDataInstance;
    private System.Type? _flSaveDataType;

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
        LoadNameFiles();
        SetupCrashHooks();
        CacheReflectionHandles();
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
            string? found = ScanForWeapons();
            if (found != null && found != _lastWeapon && !_panicFired)
                FirePanic(found);

            if (found == null)
            {
                _lastWeapon = null;
                _panicFired = false;
            }
            else
                _lastWeapon = found;
        }
        catch { }
    }

    private static string? ScanForWeapons()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return null;
        return WalkForWeapon(player.transform);
    }

    private static string? WalkForWeapon(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            var n = child.name;
            if (n.Contains("Gun_AP58") || n.Contains("Wep_Pistol_01"))
                return n;
            var found = WalkForWeapon(child);
            if (found != null) return found;
        }
        return null;
    }

    private void FirePanic(string weapon)
    {
        _panicFired = true;
        _panicFlashTimer = 5f;

        MelonLogger.Msg($"[GameEventLogger] PANIC: {weapon} drawn — dispatching backup");

        // Audio panic via GrammarPoliceMod
        try { _playPanicTone?.Invoke(null, new object[] { "" }); } catch { }
        try { _triggerPanic?.Invoke(_gpInstance, null); } catch { }

        // Native Flashing Lights AI backup dispatch
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
    //  AI NAMES → MDT NPC RECORDS
    // ═══════════════════════════════════════════════════

    private void ScanForAINames()
    {
        try
        {
            if (_seenAINames.Count > 500) _seenAINames.Clear();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                WalkForAINames(root.transform);
            }
        }
        catch { }
    }

    private void WalkForAINames(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            if (!child.gameObject.activeInHierarchy) continue;
            var name = child.name;
            if ((name.Contains("AI-Names-M generated") || name.Contains("AI-Names-F generated")) &&
                _seenAINames.Add(name))
            {
                CreateNpcRecord(child.gameObject);
            }
            WalkForAINames(child);
        }
    }

    private void CreateNpcRecord(GameObject sourceObj)
    {
        string? npcName = ExtractNpcNameFromScene(sourceObj);
        if (string.IsNullOrEmpty(npcName))
            npcName = GenerateNpcName();
        if (string.IsNullOrEmpty(npcName))
            npcName = $"Civilian_{UnityEngine.Random.Range(100, 999)}";

        string reg = "";
        bool insurance = false;
        bool firearmsLicense = false;
        bool wanted = false;
        bool missing = false;

        var saveData = TryReadFlSaveData(npcName);
        if (saveData != null)
        {
            if (!string.IsNullOrEmpty(saveData.Registration)) reg = saveData.Registration;
            insurance = saveData.HasInsurance;
            firearmsLicense = saveData.HasFirearmsLicense;
            wanted = saveData.IsWanted;
            missing = saveData.IsMissing;
        }

        if (Config.RandomizeNpcData && string.IsNullOrEmpty(reg) && !insurance && !firearmsLicense && !wanted && !missing)
        {
            reg = GeneratePlate();
            insurance = UnityEngine.Random.value > 0.15f;
            firearmsLicense = UnityEngine.Random.value > 0.65f;
            wanted = UnityEngine.Random.value < 0.10f;
            missing = UnityEngine.Random.value < 0.04f;
        }

        NPCDataStore.AddNpcRecord(new NPCInfo(npcName, reg, insurance, firearmsLicense, wanted, missing, DateTime.Now));
    }

    private static string? ExtractNpcNameFromScene(GameObject sourceObj)
    {
        try
        {
            if (sourceObj.transform == null) return null;
            return WalkForNpcName(sourceObj.transform, sourceObj.name);
        }
        catch { }
        return null;
    }

    private static string? WalkForNpcName(Transform t, string sourceName)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            string tn = child.name;

            if (tn.Contains("generated", StringComparison.OrdinalIgnoreCase) ||
                tn.Contains("ai-names", StringComparison.OrdinalIgnoreCase) ||
                tn == sourceName)
                continue;

            var tm = child.GetComponent<TextMesh>();
            if (tm != null && !string.IsNullOrEmpty(tm.text) && tm.text.Length > 2)
                return tm.text.Trim();

            if (tn.Length >= 3 && tn.Length <= 40 && tn.Any(char.IsUpper) && !tn.Contains("generated"))
                return tn;

            var found = WalkForNpcName(child, sourceName);
            if (found != null) return found;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════
    //  FlSaveData READER
    // ═══════════════════════════════════════════════════

    private sealed class FlSaveDataResult
    {
        public string Registration = "";
        public bool HasInsurance;
        public bool HasFirearmsLicense;
        public bool IsWanted;
        public bool IsMissing;
    }

    private FlSaveDataResult? TryReadFlSaveData(string npcName)
    {
        if (!_flSaveDataSearched)
        {
            _flSaveDataSearched = true;
            try
            {
                _flSaveDataType = System.Type.GetType("FlSaveData");
                if (_flSaveDataType != null)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (scene.IsValid())
                    {
                        foreach (var go in scene.GetRootGameObjects())
                        {
                            if (go == null) continue;
                            try { _flSaveDataInstance = go.GetComponent("FlSaveData"); if (_flSaveDataInstance != null) break; }
                            catch { }
                            _flSaveDataInstance = FindFlSaveDataInChildren(go.transform);
                            if (_flSaveDataInstance != null) break;
                        }
                    }
                }
            }
            catch { }
        }

        if (_flSaveDataInstance == null || _flSaveDataType == null) return null;

        try
        {
            var result = new FlSaveDataResult();
            string prefix = $"NPC_{npcName.Replace(" ", "_")}_".ToLowerInvariant();

            var stringsField = _flSaveDataType.GetField("serializableStrings",
                BindingFlags.Public | BindingFlags.Instance);
            if (stringsField?.GetValue(_flSaveDataInstance) is System.Collections.IList stringsList)
            {
                foreach (var entry in stringsList)
                {
                    if (entry == null) continue;
                    var et = entry.GetType();
                    var key = et.GetField("Key")?.GetValue(entry) as string ?? "";
                    var val = et.GetField("Value")?.GetValue(entry) as string ?? "";
                    string k = key.ToLowerInvariant();

                    if (k.StartsWith(prefix) && k.EndsWith("registration"))
                        result.Registration = val;
                    if ((k.EndsWith("_plate") || k.EndsWith("licenseplate")) && !string.IsNullOrEmpty(val))
                        result.Registration = val;
                }
            }

            var boolsField = _flSaveDataType.GetField("serializableBools",
                BindingFlags.Public | BindingFlags.Instance);
            if (boolsField?.GetValue(_flSaveDataInstance) is System.Collections.IList boolsList)
            {
                foreach (var entry in boolsList)
                {
                    if (entry == null) continue;
                    var et = entry.GetType();
                    var key = et.GetField("Key")?.GetValue(entry) as string ?? "";
                    bool val = et.GetField("Value")?.GetValue(entry) is bool b && b;
                    string k = key.ToLowerInvariant();

                    if (!k.StartsWith(prefix)) continue;
                    if (k.EndsWith("insurance") || k.EndsWith("hasinsurance")) result.HasInsurance = val;
                    if (k.EndsWith("firearmslicense") || k.EndsWith("hasfirearm")) result.HasFirearmsLicense = val;
                    if (k.EndsWith("wanted") || k.EndsWith("iswanted")) result.IsWanted = val;
                    if (k.EndsWith("missing") || k.EndsWith("ismissing")) result.IsMissing = val;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] FlSaveData read failed: {ex.Message}");
            return null;
        }
    }

    private static object? FindFlSaveDataInChildren(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            try
            {
                var comp = child.gameObject.GetComponent("FlSaveData");
                if (comp != null) return comp;
            }
            catch { }
            var found = FindFlSaveDataInChildren(child);
            if (found != null) return found;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════
    //  NAME / PLATE GENERATORS
    // ═══════════════════════════════════════════════════

    private string GenerateNpcName()
    {
        if (_firstNames.Length == 0 || _lastNames.Length == 0)
            return $"Civilian_{UnityEngine.Random.Range(100, 999)}";

        return $"{_firstNames[UnityEngine.Random.Range(0, _firstNames.Length)]} " +
               $"{_lastNames[UnityEngine.Random.Range(0, _lastNames.Length)]}";
    }

    private static string GeneratePlate()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<char> plate = stackalloc char[8];
        for (int i = 0; i < 3; i++)
            plate[i] = chars[UnityEngine.Random.Range(0, 26)];
        plate[3] = '-';
        for (int i = 4; i < 8; i++)
            plate[i] = chars[26 + UnityEngine.Random.Range(0, 10)];
        return new string(plate);
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
