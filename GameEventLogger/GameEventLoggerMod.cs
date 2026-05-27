using System.IO;
using System.Reflection;
using FlashingLights.ModKit.Core;
using HarmonyLib;
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
    private GameObject? _cachedPlayer;
    private Transform? _cachedWeapon;
    private int _shotCount;
    private bool _panicFired;

    // ── AI names → MDT ──
    private readonly HashSet<int> _seenAINames = new();
    private float _sceneTimer;
    private const float SceneInterval = 1.5f;
    private string[] _firstNames = Array.Empty<string>();
    private string[] _lastNames = Array.Empty<string>();
    private int _loggedRoots;

    // ── Panic overlay ──
    private float _panicFlashTimer;

    // ── Reflection handles (cached) ──
    private MethodInfo? _playPanicTone;
    private MethodInfo? _triggerPanic;
    private object? _gpInstance;
    private object? _flSaveDataInstance;
    private System.Type? _flSaveDataType;
    private bool _flSaveDataSearched;
    private readonly HarmonyLib.Harmony _flSaveDataHarmony = new("GameEventLogger.FlSaveData");

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
            PollPanicInput();

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
        _cachedWeapon = null;
        _cachedPlayer = null;
        _shotCount = 0;
        _flSaveDataHarmony.UnpatchSelf();

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
            string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string gameRootDir = Path.GetDirectoryName(modDir) ?? ".";

            string[] searchPaths = new[]
            {
                Path.Combine(modDir, "AI_First_names.txt"),
                Path.Combine(gameRootDir, "AI_First_names.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                             "CivIDs-Flashing Lights", "AI_First_names.txt")
            };

            string[] searchPathsLast = new[]
            {
                Path.Combine(modDir, "Ai_Last_Names.txt"),
                Path.Combine(gameRootDir, "Ai_Last_Names.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                             "CivIDs-Flashing Lights", "Ai_Last_Names.txt")
            };

            string? fnPath = searchPaths.FirstOrDefault(File.Exists);
            string? lnPath = searchPathsLast.FirstOrDefault(File.Exists);

            if (fnPath != null)
                _firstNames = File.ReadAllLines(fnPath);
            if (lnPath != null)
                _lastNames = File.ReadAllLines(lnPath);

            if (_firstNames.Length == 0)
            {
                _firstNames = new[] { "James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda",
                                      "David", "Elizabeth", "William", "Barbara", "Richard", "Susan", "Joseph", "Jessica" };
            }
            if (_lastNames.Length == 0)
            {
                _lastNames = new[] { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
                                     "Rodriguez", "Martinez", "Hernandez", "Lopez", "Wilson", "Anderson", "Thomas", "Taylor" };
            }

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
    //  AI NAMES → MDT NPC RECORDS
    // ═══════════════════════════════════════════════════

    private void ScanForAINames()
    {
        try
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            var roots = scene.GetRootGameObjects();
            if (_loggedRoots == 0)
            {
                _loggedRoots = 1;
                MelonLogger.Msg($"[GameEventLogger] Scanning {roots.Length} root objects for NPCs...");
                foreach (var r in roots)
                {
                    if (r == null) continue;
                    MelonLogger.Msg($"[GameEventLogger]   Root: {r.name}");
                    LogChildNames(r.transform, 1);
                }
            }

            // Find NPC containers ("Npcs", "Names") and iterate their children
            foreach (var root in roots)
            {
                if (root == null) continue;
                string rn = root.name;
                if (!rn.Equals("Npcs", StringComparison.OrdinalIgnoreCase) &&
                    !rn.Equals("Names", StringComparison.OrdinalIgnoreCase)) continue;
                WalkNpcsContainer(root.transform);
            }

            // Also scan all objects for NPC-containing names (like Smoke_Diner_NPC-Pos)
            foreach (var root in roots)
            {
                if (root == null) continue;
                WalkForNpcMarkers(root.transform);
            }
        }
        catch { }
    }

    private void WalkNpcsContainer(Transform container)
    {
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            if (child == null) continue;
            if (!child.gameObject.activeInHierarchy) continue;

            int id = child.gameObject.GetInstanceID();
            if (!_seenAINames.Add(id)) continue;

            CreateNpcRecord(child.gameObject);
        }
    }

    private void WalkForNpcMarkers(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            if (!child.gameObject.activeInHierarchy) continue;

            string name = child.name;
            if (name.Contains("NPC", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Manager", StringComparison.OrdinalIgnoreCase))
            {
                int id = child.gameObject.GetInstanceID();
                if (_seenAINames.Add(id))
                    CreateNpcRecord(child.gameObject);
            }

            WalkForNpcMarkers(child);
        }
    }

    private static void LogChildNames(Transform t, int depth)
    {
        if (depth > 3) return;
        for (int i = 0; i < t.childCount && i < 10; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            string prefix = new string(' ', depth * 2);
            MelonLogger.Msg($"[GameEventLogger]   {prefix}{child.name}");
            LogChildNames(child, depth + 1);
        }
    }

    private void CreateNpcRecord(GameObject sourceObj)
    {
        string? npcName = ExtractNpcNameFromScene(sourceObj);
        if (string.IsNullOrEmpty(npcName))
            npcName = GenerateNpcName();
        if (string.IsNullOrEmpty(npcName))
            npcName = $"Civilian_{UnityEngine.Random.Range(100, 999)}";

        MelonLogger.Msg($"[GameEventLogger] Creating NPC record: {npcName}");

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
            MelonLogger.Msg($"[GameEventLogger] FlSaveData found for {npcName}: plate={reg}");
        }
        else
            MelonLogger.Msg($"[GameEventLogger] No FlSaveData for {npcName}");

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
        if (_flSaveDataInstance == null && !_flSaveDataSearched)
        {
            _flSaveDataSearched = true;
            DiscoverFlSaveData();
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

    private void DiscoverFlSaveData()
    {
        try
        {
            // 1. Search all loaded scenes (FlSaveData may be in Game2, not active scene)
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (!s.IsValid() || !s.isLoaded) continue;
                foreach (var go in s.GetRootGameObjects())
                {
                    if (go == null) continue;
                    _flSaveDataInstance = FindComponentByName(go.transform, "FlSaveData");
                    if (_flSaveDataInstance != null)
                    {
                        _flSaveDataType = _flSaveDataInstance.GetType();
                        MelonLogger.Msg($"[GameEventLogger] Found FlSaveData on: {go.name} (scene={s.name})");
                        return;
                    }
                }
            }

            // 2. Search all loaded GameObjects via Resources
            try
            {
                var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var go in allObjects)
                {
                    if (go == null || go.scene.name == null) continue;
                    _flSaveDataInstance = FindComponentByName(go.transform, "FlSaveData");
                    if (_flSaveDataInstance != null)
                    {
                        _flSaveDataType = _flSaveDataInstance.GetType();
                        MelonLogger.Msg($"[GameEventLogger] Found FlSaveData via Resources on: {go.name}");
                        return;
                    }
                }
            }
            catch { }

            // 3. Assembly scanning (last resort — wrap per-assembly in try-catch)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                System.Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (!t.Name.Equals("FlSaveData", StringComparison.Ordinal)) continue;
                    MelonLogger.Msg($"[GameEventLogger] Found FlSaveData type in {asm.GetName().Name}: {t.FullName}");

                    // Check for static Instance property/field (singleton pattern)
                    var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        _flSaveDataInstance = prop.GetValue(null, null);
                        if (_flSaveDataInstance != null)
                        {
                            _flSaveDataType = t;
                            MelonLogger.Msg($"[GameEventLogger] FlSaveData found via Instance property");
                            return;
                        }
                    }

                    var field = t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        _flSaveDataInstance = field.GetValue(null);
                        if (_flSaveDataInstance != null)
                        {
                            _flSaveDataType = t;
                            MelonLogger.Msg($"[GameEventLogger] FlSaveData found via Instance field");
                            return;
                        }
                    }

                    // Try non-generic FindObjectsOfType (doesn't require Component constraint)
                    var nonGenericMethod = typeof(UnityEngine.Object)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "FindObjectsOfType"
                                          && !m.IsGenericMethodDefinition
                                          && m.GetParameters().Length == 1
                                          && m.GetParameters()[0].ParameterType == typeof(System.Type));
                    if (nonGenericMethod != null)
                    {
                        var results = nonGenericMethod.Invoke(null, new object[] { t }) as Array;
                        if (results != null && results.Length > 0)
                        {
                            _flSaveDataInstance = results.GetValue(0);
                            _flSaveDataType = t;
                            MelonLogger.Msg($"[GameEventLogger] FlSaveData instance found via FindObjectsOfType(Type)");
                            return;
                        }
                    }

                    // Try creating a new instance (data may populate from asset bundle)
                    try
                    {
                        var instance = Activator.CreateInstance(t, nonPublic: true);
                        if (instance != null)
                        {
                            _flSaveDataInstance = instance;
                            _flSaveDataType = t;
                            MelonLogger.Msg($"[GameEventLogger] FlSaveData instance created via Activator");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[GameEventLogger] FlSaveData Activator.CreateInstance failed: {ex.Message}");
                    }

                    // Try Il2Cpp zero-arg constructor
                    try
                    {
                        var ctor = t.GetConstructor(Type.EmptyTypes);
                        if (ctor != null)
                        {
                            var instance = ctor.Invoke(null);
                            if (instance != null)
                            {
                                _flSaveDataInstance = instance;
                                _flSaveDataType = t;
                                MelonLogger.Msg($"[GameEventLogger] FlSaveData instance created via default ctor");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[GameEventLogger] FlSaveData default ctor failed: {ex.Message}");
                    }

                    // Static field scan: search every type for a static FlSaveData field
                    MelonLogger.Msg($"[GameEventLogger] Scanning all types for static FlSaveData fields...");
                    foreach (var scanAsm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (scanAsm.IsDynamic) continue;
                        System.Type[] scanTypes;
                        try { scanTypes = scanAsm.GetTypes(); }
                        catch { continue; }

                        foreach (var scanType in scanTypes)
                        {
                            try
                            {
                                foreach (var fi in scanType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    if (fi.FieldType != t) continue;
                                    var val = fi.GetValue(null);
                                    if (val != null)
                                    {
                                        _flSaveDataInstance = val;
                                        _flSaveDataType = t;
                                        MelonLogger.Msg($"[GameEventLogger] FlSaveData found in {scanType.FullName}.{fi.Name}");
                                        return;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // Harmony hook: intercept constructor when game creates FlSaveData
                    MelonLogger.Msg($"[GameEventLogger] Installing Harmony hook on FlSaveData constructor...");
                    try
                    {
                        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var ctor in ctors)
                        {
                            if (ctor.GetParameters().Length > 0) continue;
                            _flSaveDataHarmony.Unpatch(ctor, HarmonyPatchType.Postfix, _flSaveDataHarmony.Id);
                            _flSaveDataHarmony.Patch(ctor, postfix: new HarmonyMethod(typeof(GameEventLoggerMod)
                                .GetMethod(nameof(FlSaveDataCtorPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
                            MelonLogger.Msg($"[GameEventLogger] Harmony hook installed on FlSaveData.ctor()");
                            _flSaveDataSearched = false; // allow retry
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[GameEventLogger] Harmony hook failed: {ex.Message}");
                    }

                    MelonLogger.Warning($"[GameEventLogger] FlSaveData: no instance exists at runtime. Game stores NPC data in level2 AssetBundle; using generated data.");
                    break;
                }
            }

            MelonLogger.Warning("[GameEventLogger] FlSaveData not found in scene");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] FlSaveData discovery failed: {ex.Message}");
        }
    }

    private static void FlSaveDataCtorPostfix(object __instance)
    {
        if (__instance == null) return;
        var mod = Melon<GameEventLoggerMod>.Instance;
        if (mod == null) return;
        if (mod._flSaveDataInstance != null) return;
        mod._flSaveDataInstance = __instance;
        mod._flSaveDataType = __instance.GetType();
        MelonLogger.Msg($"[GameEventLogger] FlSaveData instance captured via Harmony hook");
    }

    private static object? FindComponentByName(Transform t, string typeName)
    {
        try
        {
            var comp = t.gameObject.GetComponent(typeName);
            if (comp != null) return comp;
        }
        catch { }
        for (int i = 0; i < t.childCount; i++)
        {
            var found = FindComponentByName(t.GetChild(i), typeName);
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
