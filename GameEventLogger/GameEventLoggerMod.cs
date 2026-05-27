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
    private GameObject[] _cachedRoots = Array.Empty<GameObject>();
    private int _rootsVersion;
    private int _lastRootCount;
    private bool _loggedPlayMakerSnapshotForScene;
    private float _nextPlayMakerNameSyncTime;
    private const float PlayMakerNameSyncInterval = 5f;
    private readonly HashSet<string> _syncedPlayMakerNames = new(StringComparer.OrdinalIgnoreCase);

    // ── Panic overlay ──
    private float _panicFlashTimer;

    // ── Reflection handles (cached) ──
    private MethodInfo? _playPanicTone;
    private MethodInfo? _triggerPanic;
    private object? _gpInstance;
    private readonly HarmonyLib.Harmony _playMakerNameHarmony = new("GameEventLogger.PlayMakerNames");

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
        InstallPlayMakerNameHooks();
        MelonLogger.Msg($"[GameEventLogger] persistentDataPath: {Application.persistentDataPath}");
        MelonLogger.Msg("[GameEventLogger] Initialized. Panic alarm + MDT NPC records active.");
    }

    protected override void OnModKitUpdate()
    {
        if (Config.PanicAlarmEnabled)
            PollPanicInput();

        if (Config.NpcRecordEnabled)
        {
            SyncNpcRecordsFromPlayMakerDatabase();

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
        _playMakerNameHarmony.UnpatchSelf();

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

            int handle = scene.handle;
            if (handle != _rootsVersion)
            {
                _rootsVersion = handle;
                _loggedPlayMakerSnapshotForScene = false;
                _cachedRoots = scene.GetRootGameObjects();
                if (_cachedRoots.Length != _lastRootCount)
                {
                    _lastRootCount = _cachedRoots.Length;
                    MelonLogger.Msg($"[GameEventLogger] Scene changed — {_lastRootCount} root objects");
                }
            }

            var roots = _cachedRoots;

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
            if ((name.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("npc", StringComparison.OrdinalIgnoreCase)) &&
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
        bool capturePmDebug = !_loggedPlayMakerSnapshotForScene;
        var pmData = TryExtractNpcDataFromPlayMaker(sourceObj, capturePmDebug);
        if (capturePmDebug)
        {
            LogPlayMakerSnapshot(sourceObj, pmData);
            _loggedPlayMakerSnapshotForScene = true;
        }

        string? npcName = pmData.Name;
        if (string.IsNullOrEmpty(npcName))
            npcName = ExtractNpcNameFromScene(sourceObj);
        if (string.IsNullOrEmpty(npcName))
            npcName = GenerateNpcName();
        if (string.IsNullOrEmpty(npcName))
            npcName = $"Civilian_{UnityEngine.Random.Range(100, 999)}";

        MelonLogger.Msg($"[GameEventLogger] Creating NPC record: {npcName}");

        string reg = pmData.Registration ?? "";
        bool insurance = pmData.HasInsurance ?? false;
        bool firearmsLicense = pmData.HasFirearmsLicense ?? false;
        bool wanted = pmData.IsWanted ?? false;
        bool missing = pmData.IsMissing ?? false;

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

    private sealed class PlayMakerNpcData
    {
        public string? Name;
        public string? Registration;
        public bool? HasInsurance;
        public bool? HasFirearmsLicense;
        public bool? IsWanted;
        public bool? IsMissing;
        public readonly List<string> StringVarDebug = new();
        public readonly List<string> BoolVarDebug = new();
    }

    private static PlayMakerNpcData TryExtractNpcDataFromPlayMaker(GameObject sourceObj, bool captureDebug)
    {
        var result = new PlayMakerNpcData();
        try
        {
            if (sourceObj == null) return result;
            VisitTransformsForPlayMaker(sourceObj.transform, 0, result, captureDebug);
        }
        catch { }
        return result;
    }

    private static void VisitTransformsForPlayMaker(Transform t, int depth, PlayMakerNpcData result, bool captureDebug)
    {
        if (depth > 4) return;
        TryExtractFromComponents(t.gameObject, result, captureDebug);
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            VisitTransformsForPlayMaker(child, depth + 1, result, captureDebug);
        }
    }

    private static void TryExtractFromComponents(GameObject go, PlayMakerNpcData result, bool captureDebug)
    {
        Component[] comps;
        try { comps = go.GetComponents<Component>(); }
        catch { return; }

        foreach (var comp in comps)
        {
            if (comp == null) continue;
            var ct = comp.GetType();
            string fullName = ct.FullName ?? "";
            string typeName = ct.Name;

            if (!fullName.Contains("PlayMaker", StringComparison.OrdinalIgnoreCase) &&
                !typeName.Contains("PlayMaker", StringComparison.OrdinalIgnoreCase) &&
                !typeName.Contains("Fsm", StringComparison.OrdinalIgnoreCase))
                continue;

            object? varsObj = ct.GetProperty("FsmVariables", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(comp)
                              ?? ct.GetField("FsmVariables", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(comp);
            if (varsObj == null) continue;

            ExtractStringVars(varsObj, result, captureDebug);
            ExtractBoolVars(varsObj, result, captureDebug);
        }
    }

    private static void ExtractStringVars(object varsObj, PlayMakerNpcData result, bool captureDebug)
    {
        foreach (var item in EnumeratePlayMakerCollection(varsObj, "StringVariables", "stringVariables", "FsmStrings"))
        {
            string key = GetMemberString(item, "Name", "name", "Key", "key");
            string value = GetMemberString(item, "Value", "value", "Text", "text");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

            string k = key.ToLowerInvariant();
            string v = value.Trim();
            if (captureDebug && result.StringVarDebug.Count < 20)
                result.StringVarDebug.Add($"{key}={v}");

            if (string.IsNullOrWhiteSpace(result.Registration) &&
                (k.Contains("registration") || k.Contains("licenseplate") || k.EndsWith("plate")))
                result.Registration = v;

            if (string.IsNullOrWhiteSpace(result.Name) &&
                (k.Contains("fullname") || k.Contains("npcname") || k.EndsWith("name")) &&
                LooksLikeNpcName(v))
                result.Name = v;
        }
    }

    private static void ExtractBoolVars(object varsObj, PlayMakerNpcData result, bool captureDebug)
    {
        foreach (var item in EnumeratePlayMakerCollection(varsObj, "BoolVariables", "boolVariables", "FsmBools"))
        {
            string key = GetMemberString(item, "Name", "name", "Key", "key");
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!TryGetMemberBool(item, out bool value, "Value", "value")) continue;

            string k = key.ToLowerInvariant();
            if (captureDebug && result.BoolVarDebug.Count < 20)
                result.BoolVarDebug.Add($"{key}={value}");
            if (result.HasInsurance == null && (k.Contains("insurance") || k.Contains("hasinsurance")))
                result.HasInsurance = value;
            else if (result.HasFirearmsLicense == null && (k.Contains("firearm") || k.Contains("weaponlicense")))
                result.HasFirearmsLicense = value;
            else if (result.IsWanted == null && k.Contains("wanted"))
                result.IsWanted = value;
            else if (result.IsMissing == null && k.Contains("missing"))
                result.IsMissing = value;
        }
    }

    private static void LogPlayMakerSnapshot(GameObject sourceObj, PlayMakerNpcData data)
    {
        string src = sourceObj != null ? sourceObj.name : "<null>";
        MelonLogger.Msg($"[GameEventLogger] PlayMaker snapshot source={src}");
        MelonLogger.Msg($"[GameEventLogger] PM resolved: name='{data.Name ?? ""}', plate='{data.Registration ?? ""}', ins={data.HasInsurance?.ToString() ?? "null"}, firearm={data.HasFirearmsLicense?.ToString() ?? "null"}, wanted={data.IsWanted?.ToString() ?? "null"}, missing={data.IsMissing?.ToString() ?? "null"}");

        if (data.StringVarDebug.Count == 0 && data.BoolVarDebug.Count == 0)
        {
            MelonLogger.Msg("[GameEventLogger] PM vars: none found on scanned components.");
            return;
        }

        foreach (var line in data.StringVarDebug)
            MelonLogger.Msg($"[GameEventLogger] PM string: {line}");
        foreach (var line in data.BoolVarDebug)
            MelonLogger.Msg($"[GameEventLogger] PM bool: {line}");
    }

    private void InstallPlayMakerNameHooks()
    {
        try
        {
            var postfix = new HarmonyMethod(typeof(GameEventLoggerMod)
                .GetMethod(nameof(PlayMakerNameActionPostfix), BindingFlags.NonPublic | BindingFlags.Static));

            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.GetName2", postfix,
                "OnEnter", "OnUpdate", "DoGetGameObjectName");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.SetName", postfix,
                "OnEnter", "DoSetLayer");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.BuildString", postfix,
                "OnEnter", "OnUpdate", "DoBuildString");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.SetStringValue", postfix,
                "OnEnter", "OnUpdate", "DoSetStringValue");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.SelectRandomString", postfix,
                "OnEnter", "DoSelectRandomString");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.ArrayListGetRandom", postfix,
                "OnEnter", "GetRandomItem");
            PatchPlayMakerAction("HutongGames.PlayMaker.Actions.ArrayGetRandom", postfix,
                "OnEnter", "OnUpdate", "DoGetRandomValue");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] PlayMaker name hooks failed: {ex.Message}");
        }
    }

    private void PatchPlayMakerAction(string typeName, HarmonyMethod postfix, params string[] methodNames)
    {
        var type = System.Type.GetType($"{typeName}, Assembly-CSharp");
        if (type == null) return;

        int patched = 0;
        foreach (string methodName in methodNames)
        {
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) continue;

            try
            {
                _playMakerNameHarmony.Patch(method, postfix: postfix);
                patched++;
            }
            catch { }
        }

        if (patched > 0)
            MelonLogger.Msg($"[GameEventLogger] PlayMaker name hook installed: {typeName} ({patched})");
    }

    private static void PlayMakerNameActionPostfix(object __instance)
    {
        try
        {
            var mod = Melon<GameEventLoggerMod>.Instance;
            if (mod == null || !mod.Config.NpcRecordEnabled || __instance == null) return;

            string source = __instance.GetType().FullName ?? __instance.GetType().Name;
            foreach (string fieldName in new[]
                     {
                         "storeResult", "storeName", "storeString", "name",
                         "stringVariable", "stringValue", "randomItem", "storeValue"
                     })
            {
                string value = GetMemberString(__instance, fieldName);
                if (LooksLikeNpcName(value))
                    mod.RegisterPlayMakerNpcName(value, source);
            }
        }
        catch { }
    }

    private void SyncNpcRecordsFromPlayMakerDatabase()
    {
        if (Time.unscaledTime < _nextPlayMakerNameSyncTime) return;
        _nextPlayMakerNameSyncTime = Time.unscaledTime + PlayMakerNameSyncInterval;

        try
        {
            int imported = 0;
            var objects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in objects)
            {
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;

                var pmData = TryExtractNpcDataFromPlayMaker(go, false);
                if (RegisterPlayMakerNpcName(pmData.Name, $"PlayMakerFSM:{go.name}", log: false))
                    imported++;

                if (IsNpcNameContext(go) && RegisterPlayMakerNpcName(go.name, $"GameObject:{go.name}", log: false))
                    imported++;
            }

            if (imported > 0)
                MelonLogger.Msg($"[GameEventLogger] Synced {imported} NPC names from PlayMaker scene state.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] PlayMaker name sync failed: {ex.Message}");
        }
    }

    private bool RegisterPlayMakerNpcName(string? npcName, string source, bool log = true)
    {
        npcName = NormalizeNpcName(npcName);
        if (!LooksLikeNpcName(npcName)) return false;
        if (!_syncedPlayMakerNames.Add(npcName)) return false;

        string reg = "";
        bool insurance = false;
        bool firearmsLicense = false;
        bool wanted = false;
        bool missing = false;

        if (Config.RandomizeNpcData)
        {
            reg = GeneratePlate();
            insurance = UnityEngine.Random.value > 0.15f;
            firearmsLicense = UnityEngine.Random.value > 0.65f;
            wanted = UnityEngine.Random.value < 0.10f;
            missing = UnityEngine.Random.value < 0.04f;
        }

        NPCDataStore.AddNpcRecord(new NPCInfo(npcName, reg, insurance, firearmsLicense, wanted, missing, DateTime.Now));
        if (log)
            MelonLogger.Msg($"[GameEventLogger] PlayMaker NPC name synced: {npcName} ({source})");
        return true;
    }

    private static string NormalizeNpcName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var parts = value.Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private static bool IsNpcNameContext(GameObject go)
    {
        try
        {
            var t = go.transform;
            for (int depth = 0; t != null && depth < 4; depth++, t = t.parent)
            {
                string n = t.name;
                if (n.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("AI-Names", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("generated", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Names", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Npcs", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private static IEnumerable<object> EnumeratePlayMakerCollection(object owner, params string[] names)
    {
        foreach (var name in names)
        {
            object? collection = owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner)
                               ?? owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner);
            if (collection is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (item != null) yield return item;
                yield break;
            }
        }
    }

    private static string GetMemberString(object obj, params string[] names)
    {
        foreach (var name in names)
        {
            object? val = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj)
                         ?? obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
            if (val == null) continue;

            // PlayMaker often wraps values in FsmString with a nested Value field/property.
            object? nested = val.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val)
                          ?? val.GetType().GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val);
            string text = (nested ?? val).ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return "";
    }

    private static bool TryGetMemberBool(object obj, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            object? val = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj)
                         ?? obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
            if (val == null) continue;

            if (val is bool b)
            {
                value = b;
                return true;
            }

            object? nested = val.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val)
                          ?? val.GetType().GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val);
            if (nested is bool nb)
            {
                value = nb;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool LooksLikeNpcName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length < 3 || value.Length > 50) return false;
        if (!value.Contains(' ')) return false;
        return value.Any(char.IsLetter);
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

#if false
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
        EnsureFlSaveDataDiscovery();

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

    private void EnsureFlSaveDataDiscovery()
    {
        if (_flSaveDataInstance != null && _flSaveDataType != null) return;
        if (Time.unscaledTime < _nextFlSaveDataSearchTime) return;

        _nextFlSaveDataSearchTime = Time.unscaledTime + 5f;
        DiscoverFlSaveData();
    }

    private sealed class FlNpcSeed
    {
        public string Name = "";
        public string Registration = "";
        public bool HasInsurance;
        public bool HasFirearmsLicense;
        public bool IsWanted;
        public bool IsMissing;
    }

    private void SyncNpcRecordsFromFlSaveData()
    {
        if (_flSaveDataInstance == null || _flSaveDataType == null) return;
        if (Time.unscaledTime < _nextFlSaveDataSyncTime) return;
        _nextFlSaveDataSyncTime = Time.unscaledTime + 5f;

        try
        {
            var map = new Dictionary<string, FlNpcSeed>(StringComparer.OrdinalIgnoreCase);

            var stringsField = _flSaveDataType.GetField("serializableStrings", BindingFlags.Public | BindingFlags.Instance);
            if (stringsField?.GetValue(_flSaveDataInstance) is System.Collections.IList stringsList)
            {
                foreach (var entry in stringsList)
                {
                    if (entry == null) continue;
                    var et = entry.GetType();
                    var key = et.GetField("Key")?.GetValue(entry) as string ?? "";
                    var val = et.GetField("Value")?.GetValue(entry) as string ?? "";
                    if (!TryParseNpcSaveKey(key, out string npcName, out string fieldName)) continue;

                    var seed = GetOrCreateSeed(map, npcName);
                    if ((fieldName.Contains("registration") || fieldName.Contains("licenseplate") || fieldName.EndsWith("plate")) &&
                        !string.IsNullOrWhiteSpace(val))
                    {
                        seed.Registration = val;
                    }
                }
            }

            var boolsField = _flSaveDataType.GetField("serializableBools", BindingFlags.Public | BindingFlags.Instance);
            if (boolsField?.GetValue(_flSaveDataInstance) is System.Collections.IList boolsList)
            {
                foreach (var entry in boolsList)
                {
                    if (entry == null) continue;
                    var et = entry.GetType();
                    var key = et.GetField("Key")?.GetValue(entry) as string ?? "";
                    bool val = et.GetField("Value")?.GetValue(entry) is bool b && b;
                    if (!TryParseNpcSaveKey(key, out string npcName, out string fieldName)) continue;

                    var seed = GetOrCreateSeed(map, npcName);
                    if (fieldName.EndsWith("insurance") || fieldName.EndsWith("hasinsurance")) seed.HasInsurance = val;
                    else if (fieldName.EndsWith("firearmslicense") || fieldName.EndsWith("hasfirearm")) seed.HasFirearmsLicense = val;
                    else if (fieldName.EndsWith("wanted") || fieldName.EndsWith("iswanted")) seed.IsWanted = val;
                    else if (fieldName.EndsWith("missing") || fieldName.EndsWith("ismissing")) seed.IsMissing = val;
                }
            }

            int imported = 0;
            foreach (var seed in map.Values)
            {
                if (string.IsNullOrWhiteSpace(seed.Name)) continue;
                NPCDataStore.AddNpcRecord(new NPCInfo(
                    seed.Name,
                    seed.Registration,
                    seed.HasInsurance,
                    seed.HasFirearmsLicense,
                    seed.IsWanted,
                    seed.IsMissing,
                    DateTime.Now));
                imported++;
            }

            if (imported > 0)
                MelonLogger.Msg($"[GameEventLogger] Synced {imported} NPC records from FlSaveData.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[GameEventLogger] FlSaveData sync failed: {ex.Message}");
        }
    }

    private static FlNpcSeed GetOrCreateSeed(Dictionary<string, FlNpcSeed> map, string npcName)
    {
        if (!map.TryGetValue(npcName, out var seed))
        {
            seed = new FlNpcSeed { Name = npcName };
            map[npcName] = seed;
        }
        return seed;
    }

    private static bool TryParseNpcSaveKey(string key, out string npcName, out string fieldName)
    {
        npcName = "";
        fieldName = "";
        if (string.IsNullOrWhiteSpace(key)) return false;

        string lower = key.ToLowerInvariant();
        if (!lower.StartsWith("npc_")) return false;

        int lastUnderscore = key.LastIndexOf('_');
        if (lastUnderscore <= 4 || lastUnderscore >= key.Length - 1) return false;

        fieldName = key[(lastUnderscore + 1)..].ToLowerInvariant();
        string rawName = key.Substring(4, lastUnderscore - 4);
        npcName = rawName.Replace('_', ' ').Trim();
        return npcName.Length > 0;
    }

    private void DiscoverFlSaveData()
    {
        try
        {
            // 1. Quick scene search — FlSaveData may be in Game2
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

            // 2. Resources search (catches DontDestroyOnLoad objects)
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

            // 3. Find type in Assembly-CSharp and check for singleton Instance
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (assembly != null)
            {
                System.Type[] types;
                try { types = assembly.GetTypes(); } catch { types = Array.Empty<System.Type>(); }

                foreach (var t in types)
                {
                    if (!t.Name.Equals("FlSaveData", StringComparison.Ordinal)) continue;

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

                    // Install Harmony hook to catch future creations
                    var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(c => c.GetParameters().Length == 0);
                    if (ctor != null)
                    {
                        _flSaveDataHarmony.Unpatch(ctor, HarmonyPatchType.Postfix, _flSaveDataHarmony.Id);
                        _flSaveDataHarmony.Patch(ctor, postfix: new HarmonyMethod(typeof(GameEventLoggerMod)
                            .GetMethod(nameof(FlSaveDataCtorPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
                        MelonLogger.Msg($"[GameEventLogger] Harmony hook installed on FlSaveData.ctor()");
                        return;
                    }

                    MelonLogger.Warning("[GameEventLogger] FlSaveData type found — no singleton or constructor to hook");
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

#endif

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
