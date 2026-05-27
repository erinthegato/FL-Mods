using System.IO;
using FlashingLights.ModKit.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using Object = UnityEngine.Object;

namespace GameEventLogger;

[ModKitManifest(
    Id = "game-event-logger",
    DisplayName = "Event Logger",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Developer")]
public sealed class GameEventLoggerMod : ModKitMelonMod<LoggerConfig>
{
    protected override string ModId => "game-event-logger";

    // ── Log file ──
    private static readonly string LogFileName = "EventLog.txt";
    private static StreamWriter? _logWriter;
    private static readonly object LogLock = new();
    private static int _writeCount;
    private const int FlushInterval = 20;

    // ── Harmony ──
    private HarmonyLib.Harmony? _harmony;

    // ── Singleton ──
    internal static GameEventLoggerMod Instance { get; private set; } = null!;

    // ── Game root caching ──
    private static string _gameRoot = "";

    internal static string GameRoot
    {
        get
        {
            if (string.IsNullOrEmpty(_gameRoot))
            {
                try
                {
                    var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (loc != null)
                    {
                        var dir = new DirectoryInfo(Path.GetDirectoryName(loc)!);
                        while (dir != null)
                        {
                            if (File.Exists(Path.Combine(dir.FullName, "Flashing Lights.exe")))
                            {
                                _gameRoot = dir.FullName;
                                break;
                            }
                            dir = dir.Parent;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(_gameRoot))
                {
                    _gameRoot = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                }
            }
            return _gameRoot;
        }
    }

    // ── Weapon scanning ──
    private float _weaponScanTimer;
    private const float WeaponScanInterval = 1.5f;
    private GameObject? _cachedPlayer;
    private float _playerCacheTimer;
    private const float PlayerCacheTimeout = 5f;
    private GameObject? _cachedCamera;
    private string? _lastWeapon;
    private bool _firstScan = true;

    // ── NPC interaction ──
    private bool _lastNpcInteraction;
    private float _npcInteractionTimer;
    private const float NpcInteractionInterval = 1f;

    // ── NPC name generation ──
    private float _npcNameTimer;
    private const float NpcNameInterval = 3f;
    private string? _lastNpcNameGenerated;

    // ── Database tracking ──
    private float _dbTimer;
    private const float DbCheckInterval = 5f;

    // ── Generation tracking ──
    private string? _lastGenerationEvent;
    private float _genTimer;
    private const float GenCheckInterval = 3f;

    // ── Call tracking ──
    internal readonly HashSet<string> ActiveCalls = new();
    internal int CallSequence;

    // ── Log viewer ──
    private bool _logViewerVisible;
    private Vector2 _logViewerScroll;
    private string _logContent = "";
    private float _logRefreshTimer;

    // ── Game freeze ──
    private bool _cursorWasLocked;
    private bool _gameFrozen;
    private float _modUiCheckTimer;
    private const float ModUiCheckInterval = 0.5f;

    // ── Crash hooks ──
    private const string CrashLogName = "CrashLog.txt";
    internal static readonly List<string> CrashBuffer = new();
    internal const int CrashBufferMax = 100;
    private static bool _crashHooksSet;

    // ── Heartbeat ──
    private static int _heartbeatCount;
    private const int HeartbeatInterval = 300;

    // ── Config accessors ──
    internal bool LogScenes => Config.LogScenes;
    internal bool LogCalls => Config.LogCalls;
    internal bool LogWeapons => Config.LogWeapons;
    internal bool LogNpcInteractions => Config.LogNpcInteractions;
    internal bool LogNpcNames => Config.LogNpcNames;
    internal bool LogDatabase => Config.LogDatabase;
    internal bool LogGeneration => Config.LogGeneration;

    // ═══════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════

    protected override void OnModKitInitialized()
    {
        Instance = this;
        OpenLogFile();
        WriteLog("[Logger] Initialized.");
        MelonLoader.MelonLogger.Msg($"[EventLogger] Game root: {GameRoot}");

        _harmony = new HarmonyLib.Harmony("game-event-logger");

        try
        {
            if (Config.LogScenes)
                PatchSceneEvents();

            if (Config.LogCalls || Config.LogWeapons)
                PatchPoolManager();

            if (Config.LogCalls)
                WriteLog("[Logger] Call dispatch tracking ACTIVE — logging all pool activations/deactivations");

            if (Config.LogWeapons)
                WriteLog("[Logger] Weapon tracking ACTIVE — scanning every 1.5s for equipped items");

            if (Config.LogNpcInteractions)
                WriteLog("[Logger] NPC interaction tracking ACTIVE");

            if (Config.LogNpcNames)
                WriteLog("[Logger] NPC name generation tracking ACTIVE");

            if (Config.LogDatabase)
                WriteLog("[Logger] Database update tracking ACTIVE");

            if (Config.LogGeneration)
                WriteLog("[Logger] Generation event tracking ACTIVE");

            WriteLog("[Logger] Mod UI freeze ACTIVE — pauses game when mod UI is open");

            SetupCrashHooks();

            MelonLoader.MelonLogger.Msg("[EventLogger] Patches applied.");
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[EventLogger] Init patch error: {ex}");
        }
    }

    protected override void OnModKitUpdate()
    {
        // ── Weapon scan ──
        if (Config.LogWeapons)
        {
            _weaponScanTimer -= Time.unscaledDeltaTime;
            if (_weaponScanTimer <= 0f)
            {
                _weaponScanTimer = WeaponScanInterval;
                ScanForWeapon();
            }
        }

        // ── NPC interaction scan ──
        if (Config.LogNpcInteractions)
        {
            _npcInteractionTimer -= Time.unscaledDeltaTime;
            if (_npcInteractionTimer <= 0f)
            {
                _npcInteractionTimer = NpcInteractionInterval;
                ScanForNpcInteraction();
            }
        }

        // ── NPC name generation scan ──
        if (Config.LogNpcNames)
        {
            _npcNameTimer -= Time.unscaledDeltaTime;
            if (_npcNameTimer <= 0f)
            {
                _npcNameTimer = NpcNameInterval;
                ScanForNpcNameGeneration();
            }
        }

        // ── Database update check ──
        if (Config.LogDatabase)
        {
            _dbTimer -= Time.unscaledDeltaTime;
            if (_dbTimer <= 0f)
            {
                _dbTimer = DbCheckInterval;
                CheckDatabaseUpdates();
            }
        }

        // ── Generation event scan ──
        if (Config.LogGeneration)
        {
            _genTimer -= Time.unscaledDeltaTime;
            if (_genTimer <= 0f)
            {
                _genTimer = GenCheckInterval;
                ScanForGenerationEvents();
            }
        }

        // ── Log viewer toggle ──
        if (Input.GetKeyDown(Config.LogViewerKey))
        {
            _logViewerVisible = !_logViewerVisible;
            RefreshLogContent();
        }

        // ── Mod UI freeze check ──
        _modUiCheckTimer -= Time.unscaledDeltaTime;
        if (_modUiCheckTimer <= 0f)
        {
            _modUiCheckTimer = ModUiCheckInterval;
            bool anyUiOpen = _logViewerVisible || IsOtherModUIOpen();

            if (anyUiOpen && !_gameFrozen)
            {
                FreezeGame();
                return;
            }

            if (!anyUiOpen && _gameFrozen)
            {
                UnfreezeGame();
            }
        }
    }

    // ═══════════════════════════════════════════════════
    //  GAME FREEZE
    // ═══════════════════════════════════════════════════

    private void FreezeGame()
    {
        _gameFrozen = true;
        _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    private void UnfreezeGame()
    {
        _gameFrozen = false;
        Time.timeScale = 1f;
        if (_cursorWasLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private bool IsOtherModUIOpen()
    {
        try
        {
            if (GUI.GetNameOfFocusedControl() == "npcInput")
                return true;
        }
        catch { }
        return false;
    }

    // ═══════════════════════════════════════════════════
    //  MOD GUI (log viewer)
    // ═══════════════════════════════════════════════════

    protected override void OnModKitGui()
    {
        if (!_logViewerVisible) return;
        var rect = new Rect(Screen.width - 520, 50, 500, Screen.height - 100);
        DrawLogViewer(rect);
    }

    private void DrawLogViewer(Rect rect)
    {
        GUI.DrawTexture(rect, MakeTex(new Color(0.06f, 0.06f, 0.12f, 0.95f)));

        var y = rect.y + 6;

        // Title
        var titleRect = new Rect(rect.x + 6, y, rect.width - 12, 22);
        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(0.2f, 0.6f, 1f);
        GUI.Label(titleRect, "Event Log Viewer", titleStyle);
        y += 26;

        // Buttons style
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        btnStyle.normal.textColor = Color.white;
        btnStyle.normal.background = MakeTex(new Color(0.25f, 0.25f, 0.35f));
        btnStyle.hover.textColor = Color.white;
        btnStyle.hover.background = MakeTex(new Color(0.35f, 0.35f, 0.45f));

        if (GUI.Button(new Rect(rect.x + 6, y, 60, 20), "Refresh", btnStyle))
            RefreshLogContent();

        if (GUI.Button(new Rect(rect.x + 70, y, 60, 20), "Clear", btnStyle))
        {
            ClearLog();
            RefreshLogContent();
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 66, y, 60, 20), "Close", btnStyle))
            _logViewerVisible = false;

        y += 26;

        // Auto-refresh
        _logRefreshTimer -= Time.unscaledDeltaTime;
        if (_logRefreshTimer <= 0f)
        {
            _logRefreshTimer = 2f;
            RefreshLogContent();
        }

        // Log text style
        var logStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, richText = true };
        logStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        // Scroll view
        var scrollRect = new Rect(rect.x + 4, y, rect.width - 8, rect.height - (y - rect.y) - 8);
        GUI.BeginGroup(scrollRect);

        var lines = _logContent.Split('\n', StringSplitOptions.None);
        float lineHeight = 14;
        float contentHeight = lines.Length * lineHeight + 4;
        float viewWidth = scrollRect.width - 20;
        float viewHeight = scrollRect.height;
        _logViewerScroll = GUI.BeginScrollView(
            new Rect(0, 0, viewWidth, viewHeight),
            _logViewerScroll,
            new Rect(0, 0, viewWidth - 16, contentHeight));

        float drawY = 2;
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                GUI.Label(new Rect(4, drawY, viewWidth - 24, lineHeight), line, logStyle);
            }
            drawY += lineHeight;
        }

        GUI.EndScrollView();
        GUI.EndGroup();
    }

    private static Texture2D MakeTex(Color c)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
        return tex;
    }

    // ═══════════════════════════════════════════════════
    //  LOG FILE
    // ═══════════════════════════════════════════════════

    private void RefreshLogContent()
    {
        var path = Path.Combine(GameRoot, LogFileName);
        if (!File.Exists(path))
        {
            _logContent = "(log file not found)";
            return;
        }

        try
        {
            var lines = new List<string>(500);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                    if (lines.Count > 500)
                        lines.RemoveAt(0);
                }
            }
            _logContent = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logContent = $"Error reading log: {ex.Message}";
        }
    }

    private void ClearLog()
    {
        try
        {
            var path = Path.Combine(GameRoot, LogFileName);
            if (File.Exists(path))
            {
                File.WriteAllText(path, $"=== Event Log Cleared: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
        }
        catch { }
    }

    private static void OpenLogFile()
    {
        try
        {
            var path = Path.Combine(GameRoot, LogFileName);
            MelonLoader.MelonLogger.Msg($"[EventLogger] Log path: {path}");

            _logWriter = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            _logWriter.WriteLine($"=== Event Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _logWriter.Flush();
            _writeCount = 0;

            MelonLoader.MelonLogger.Msg("[EventLogger] Log file opened successfully (auto-cleared on launch).");
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[EventLogger] Could not open log file: {ex.Message}");
        }
    }

    internal static void WriteLog(string message)
    {
        if (_logWriter == null) return;

        lock (LogLock)
        {
            _logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            _writeCount++;
            _heartbeatCount++;

            if (_writeCount % FlushInterval == 0)
                _logWriter.Flush();

            if (_heartbeatCount >= HeartbeatInterval)
            {
                _heartbeatCount = 0;
                _logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Heartbeat] {_writeCount} events logged so far");
                _logWriter.Flush();
            }
        }
    }

    internal static void ClearCrashLog()
    {
        try
        {
            File.WriteAllText(Path.Combine(GameRoot, CrashLogName), $"=== Crash Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
    }

    internal static void WriteCrashLog(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(GameRoot, CrashLogName), message + "\n");
        }
        catch { }
    }

    private static void CloseLog()
    {
        lock (LogLock)
        {
            if (_logWriter != null)
            {
                _logWriter.WriteLine($"=== Event Log Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.Flush();
                _logWriter.Close();
                _logWriter = null;
            }
        }
    }

    // ═══════════════════════════════════════════════════
    //  CRASH HOOKS
    // ═══════════════════════════════════════════════════

    private void SetupCrashHooks()
    {
        if (_crashHooksSet) return;
        _crashHooksSet = true;

        try
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            ClearCrashLog();
            WriteLog("[Logger] Crash hooks ACTIVE — errors logged to CrashLog.txt (auto-cleared on launch)");
        }
        catch (Exception ex)
        {
            WriteLog($"[Logger] Failed to set crash hooks: {ex.Message}");
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;

        WriteCrashLog("=== UNHANDLED EXCEPTION (FATAL) ===");
        WriteCrashLog($"IsTerminating: {e.IsTerminating}");
        WriteCrashLog($"Exception: {ex?.GetType().FullName}");
        WriteCrashLog($"Message: {ex?.Message}");
        WriteCrashLog($"Stack: {ex?.StackTrace}");

        lock (CrashBuffer)
        {
            WriteCrashLog("=== Recent Event Log ===");
            foreach (var entry in CrashBuffer)
            {
                WriteCrashLog(entry);
            }
        }

        WriteCrashLog("=== Crash Log Ended ===");
    }

    // ═══════════════════════════════════════════════════
    //  PLAYER / WEAPON SCANNING
    // ═══════════════════════════════════════════════════

    private static readonly string[] PlayerExact = {
        "Player", "FirstPersonCharacter", "FPSPlayer", "FPSController",
        "PlayerController", "Character", "LocalPlayer"
    };

    private GameObject? FindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) { WriteLog($"[Weapon] FindPlayer: tag 'Player' -> \"{go.name}\""); return go; }

        foreach (var name in PlayerExact)
        {
            go = GameObject.Find(name);
            if (go != null) { WriteLog($"[Weapon] FindPlayer: exact \"{name}\" -> \"{go.name}\""); return go; }
        }

        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null)
        {
            WriteLog($"[Weapon] FindPlayer: CharacterController -> \"{cc.gameObject.name}\"");
            return cc.gameObject;
        }

        var cam = Camera.main;
        if (cam != null)
        {
            var root = cam.transform.root;
            if (root != null && root.gameObject != cam.gameObject)
            {
                WriteLog($"[Weapon] FindPlayer: Camera.root -> \"{root.name}\"");
                return root.gameObject;
            }
        }

        foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
        {
            if (obj == null) continue;
            try { if (obj.CompareTag("Player")) { WriteLog($"[Weapon] FindPlayer: fallback tag 'Player' -> \"{obj.name}\""); return obj; } }
            catch { }

            var lower = obj.name.ToLowerInvariant();
            if (lower.Contains("player") || lower.Contains("fps") || lower.Contains("character") ||
                lower.Contains("firstperson") || lower.Contains("cop") || lower.Contains("police") ||
                lower.Contains("sheriff") || lower.Contains("ems") || lower.Contains("name_gen") ||
                lower.StartsWith("fd"))
            {
                WriteLog($"[Weapon] FindPlayer: fallback name \"{obj.name}\" matched \"{lower}\"");
                return obj;
            }
        }

        WriteLog("[Weapon] FindPlayer: all checks exhausted, returning null");
        return null;
    }

    private void UpdatePlayerAndCameraCache()
    {
        if (_cachedPlayer == null || _cachedPlayer.name == "SaveMsg")
        {
            var found = FindPlayer();
            if (found != null)
            {
                _cachedPlayer = found;
                _playerCacheTimer = PlayerCacheTimeout;
            }
        }
        else
        {
            _playerCacheTimer -= Time.unscaledDeltaTime;
            if (_playerCacheTimer <= 0f)
            {
                _playerCacheTimer = PlayerCacheTimeout;
                var camRoot = Camera.main?.transform.root;
                if (camRoot != null && camRoot.gameObject != Camera.main?.gameObject &&
                    camRoot.gameObject != _cachedPlayer)
                {
                    WriteLog($"[Weapon] Camera.root changed to \"{camRoot.name}\" — re-acquiring player");
                    _cachedPlayer = null;
                    _cachedPlayer = FindPlayer();
                    if (_cachedPlayer != null)
                        _playerCacheTimer = PlayerCacheTimeout;
                }
            }
        }

        if (_cachedCamera == null)
        {
            _cachedCamera = Camera.main?.gameObject;
            if (_cachedCamera == null)
                _cachedCamera = GameObject.Find("MainCamera");
            if (_cachedCamera == null)
                _cachedCamera = GameObject.Find("Camera");
        }
    }

    private void ScanForWeapon()
    {
        try
        {
            UpdatePlayerAndCameraCache();

            var player = _cachedPlayer;
            string? weaponName = null;

            if (player != null)
                weaponName = FindWeaponInHierarchy(player.transform);

            if (weaponName == null && _cachedCamera != null)
                weaponName = FindWeaponInHierarchy(_cachedCamera.transform);

            if (weaponName == null)
            {
                foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
                {
                    if (obj == null) continue;
                    if (obj.transform.parent == null) continue;
                    if (obj.name.StartsWith("Coll_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsWeaponName(obj.name) && obj.activeInHierarchy)
                    {
                        weaponName = obj.name;
                        break;
                    }
                }
            }

            // ── First scan: debug output ──
            if (_firstScan)
            {
                _firstScan = false;

                if (player != null)
                {
                    WriteLog($"[Weapon] Player found: \"{player.name}\", children: {player.transform.childCount}");
                }
                else
                {
                    WriteLog("[Weapon] Player not found — dumping root hierarchy:");
                    foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
                    {
                        if (obj == null || obj.transform.parent != null) continue;
                        WriteLog($"[Weapon] - \"{obj.name}\" [A] ({obj.transform.childCount} children)");
                    }
                }

                WriteLog(_cachedCamera != null ? "[Weapon] MainCamera found" : "[Weapon] MainCamera not found (name=MainCamera)");
            }

            // ── State change detection ──
            if (weaponName != _lastWeapon)
            {
                var oldWep = _lastWeapon ?? "(none)";
                var newWep = weaponName ?? "(none)";

                if (weaponName == null)
                    WriteLog($"[Weapon] Holstered (was {oldWep})");
                else if (_lastWeapon == null)
                    WriteLog($"[Weapon] Drawn: {newWep}");
                else
                    WriteLog($"[Weapon] Switched: {oldWep} -> {newWep}");

                _lastWeapon = weaponName;

                // Count weapon objects attached to player
                int weaponCount = 0;
                if (player != null)
                {
                    for (int i = 0; i < player.transform.childCount; i++)
                    {
                        if (IsWeaponName(player.transform.GetChild(i).name))
                            weaponCount++;
                    }
                }

                if (weaponCount > 0)
                    WriteLog($"[Weapon] Carrier has {weaponCount} weapon objects attached");
            }
        }
        catch { }
    }

    private static string? FindWeaponInHierarchy(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (IsWeaponName(child.name) && child.gameObject.activeInHierarchy)
                return child.name;

            var found = FindWeaponInHierarchy(child);
            if (found != null)
                return found;
        }
        return null;
    }

    private static readonly string[] WeaponContains = {
        "weapon", "firearm", "holster", "rifle", "pistol", "shotgun",
        "taser", "tazer", "flashlight", "stun", "wep_", "gun_"
    };

    private static readonly string[] WeaponExact = { "bat", "knife", "tool", "item", "equipment", "gear", "gun" };

    private static readonly string[] WeaponExcludeContains = {
        "wheel", "pos_", "coll_", "mixamorig", "container", "group",
        "pool", "npc", "name", "database", "generation", "spawner",
        "char", "body", "bomb", "flame", "lod_", "level", "bip",
        "muzzle", "muzzlefire", "weapons", "img_", "ui_", "icon_"
    };

    private static readonly string[] WeaponExcludeExact = {
        "weapon", "weapon2"
    };

    private static bool IsWeaponName(string name)
    {
        var lower = name.ToLowerInvariant();

        foreach (var x in WeaponExcludeContains)
            if (lower.Contains(x)) return false;

        foreach (var x in WeaponExcludeExact)
            if (lower == x) return false;

        foreach (var p in WeaponContains)
            if (lower.Contains(p)) return true;

        foreach (var p in WeaponExact)
            if (lower == p) return true;

        return false;
    }

    // ═══════════════════════════════════════════════════
    //  NPC INTERACTION SCANNING
    // ═══════════════════════════════════════════════════

    private void ScanForNpcInteraction()
    {
        try
        {
            bool interacting = false;

            string[] canvasNames = { "DialogueCanvas", "DialoguePanel", "ConversationPanel", "InteractionCanvas", "DialogueUI" };
            foreach (var name in canvasNames)
            {
                var go = GameObject.Find(name);
                if (go != null && go.activeInHierarchy)
                {
                    interacting = true;
                    break;
                }
            }

            if (interacting != _lastNpcInteraction)
            {
                _lastNpcInteraction = interacting;
                WriteLog($"[NPC Interaction] {interacting}");
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════
    //  NPC NAME GENERATION SCANNING
    // ═══════════════════════════════════════════════════

    private void ScanForNpcNameGeneration()
    {
        try
        {
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;
                var lower = obj.name.ToLowerInvariant();
                if (lower.Contains("name") && (lower.Contains("generated") || lower.Contains("gen_") || lower.Contains("_name")))
                {
                    if (obj.name != _lastNpcNameGenerated)
                    {
                        _lastNpcNameGenerated = obj.name;
                        WriteLog($"[NPC Name] Generated: \"{obj.name}\"");
                    }
                    return;
                }
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════
    //  DATABASE UPDATE TRACKING
    // ═══════════════════════════════════════════════════

    private static readonly string[] DbFilePatterns = { "mdt_data.json", "mdt_photos.json", "config.txt" };
    private readonly Dictionary<string, long> _dbFileSizes = new();

    private void CheckDatabaseUpdates()
    {
        try
        {
            foreach (var file in DbFilePatterns)
            {
                var path = Path.Combine(GameRoot, "Mods", file);
                if (!File.Exists(path)) continue;

                var len = new FileInfo(path).Length;
                if (_dbFileSizes.TryGetValue(file, out var prev))
                {
                    if (len != prev)
                    {
                        _dbFileSizes[file] = len;
                        WriteLog($"[Database] Updated: \"{file}\" ({prev} -> {len} bytes)");
                    }
                }
                else
                {
                    _dbFileSizes[file] = len;
                }
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════
    //  GENERATION EVENT TRACKING
    // ═══════════════════════════════════════════════════

    private void ScanForGenerationEvents()
    {
        try
        {
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;
                var lower = obj.name.ToLowerInvariant();

                if (lower.Contains("generation") || lower.Contains("gen_") ||
                    lower.Contains("spawner") || lower.Contains("population") ||
                    lower.Contains("worldgen") || lower.Contains("procgen"))
                {
                    if (obj.name != _lastGenerationEvent)
                    {
                        _lastGenerationEvent = obj.name;
                        WriteLog($"[Generation] Active: \"{obj.name}\"");
                    }
                    return;
                }
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════
    //  HARMONY PATCHES
    // ═══════════════════════════════════════════════════

    private void PatchSceneEvents()
    {
        var sceneManager = typeof(SceneManager);

        var sceneLoaded = AccessTools.Method(sceneManager, "Internal_SceneLoaded", new[] { typeof(Scene), typeof(LoadSceneMode) }, null);
        if (sceneLoaded != null && _harmony != null)
        {
            _harmony.Patch(sceneLoaded, null, new HarmonyMethod(typeof(ScenePatches), nameof(ScenePatches.AfterSceneLoaded)));
        }

        var sceneUnloaded = AccessTools.Method(sceneManager, "Internal_SceneUnloaded", new[] { typeof(Scene) }, null);
        if (sceneUnloaded != null && _harmony != null)
        {
            _harmony.Patch(sceneUnloaded, null, new HarmonyMethod(typeof(ScenePatches), nameof(ScenePatches.AfterSceneUnloaded)));
        }
    }

    private void PatchPoolManager()
    {
        var poolManager = Type.GetType("Il2CppJinxterGames.Scripts.FlashingLights.PoolGroupManager, Assembly-CSharp");
        if (poolManager == null)
        {
            MelonLoader.MelonLogger.Warning("[EventLogger] PoolGroupManager type not found.");
            return;
        }

        var activatePool = AccessTools.Method(poolManager, "ActivatePoolInGroup");
        if (activatePool != null && _harmony != null)
        {
            _harmony.Patch(activatePool, null, new HarmonyMethod(typeof(PoolPatches), nameof(PoolPatches.AfterActivatePool)));
        }

        var activateGroup = AccessTools.Method(poolManager, "ActivateGroup");
        if (activateGroup != null && _harmony != null)
        {
            _harmony.Patch(activateGroup, null, new HarmonyMethod(typeof(PoolPatches), nameof(PoolPatches.AfterActivateGroup)));
        }

        var deactivatePool = AccessTools.Method(poolManager, "DeactivatePoolInGroup");
        if (deactivatePool != null && _harmony != null)
        {
            _harmony.Patch(deactivatePool, null, new HarmonyMethod(typeof(PoolPatches), nameof(PoolPatches.AfterDeactivatePool)));
        }
    }

    protected override void OnModKitDisabled()
    {
        WriteLog("[Logger] Disabled.");

        if (_gameFrozen)
            UnfreezeGame();

        _harmony?.UnpatchSelf();
        CloseLog();
    }
}
