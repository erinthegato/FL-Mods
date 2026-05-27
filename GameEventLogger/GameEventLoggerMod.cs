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

    // Static constructor — runs at assembly load, before any game code
    static GameEventLoggerMod()
    {
        try
        {
            _crashLogPath = Path.Combine(FindGameRoot(), CrashLogName);

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

#if NET6_0_OR_GREATER
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                if (e.Exception != null)
                    LogCrashBuffer(e.Exception);
            };
#endif

            ClearCrashLog();
        }
        catch { }
    }

    private static string FindGameRoot()
    {
        try
        {
            var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
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
        return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
    }

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

    // ── Game root caching (written once at init, thread-safe for crash hooks) ──
    private static string _gameRoot = "";

    internal static string GameRoot
    {
        get
        {
            if (string.IsNullOrEmpty(_gameRoot))
                _gameRoot = FindGameRoot();
            return _gameRoot;
        }
    }

    // Thread-safe cached root for crash hooks (accessed from Unity render thread)
    private static string _crashLogPath = "";

    // ── Weapon scanning ──
    private float _weaponScanTimer;
    private const float WeaponScanInterval = 1.5f;
    private float _sceneScanTimer;
    private const float SceneScanInterval = 0.8f;
    private GameObject? _cachedPlayer;
    private float _playerCacheTimer;
    private const float PlayerCacheTimeout = 5f;
    private GameObject? _cachedCamera;
    private string? _lastWeapon;
    private readonly HashSet<string> _seenFireEffects = new();
    private float _fireEffectCleanupTimer;
    private const float FireEffectCleanupInterval = 10f;
    private bool _firstScan = true;
    private bool _didPlayerDump;

    // ── NPC interaction ──
    private bool _lastNpcInteraction;
    private float _npcInteractionTimer;
    private const float NpcInteractionInterval = 1f;

    // ── NPC name generation ──
    private readonly HashSet<string> _seenNpcNames = new();

    // ── Database tracking ──
    private float _dbTimer;
    private const float DbCheckInterval = 5f;

    // ── Generation tracking ──
    private readonly HashSet<string> _seenGenerationEvents = new();

    // ── Call tracking ──
    internal readonly HashSet<string> ActiveCalls = new();
    internal int CallSequence;

    // ── Log viewer ──
    private bool _logViewerVisible;
    private Vector2 _logViewerScroll;
    private string _logContent = "";
    private float _logRefreshTimer;
    private static GUIStyle? _viewerTitleStyle;
    private static GUIStyle? _viewerBtnStyle;
    private static GUIStyle? _viewerLogStyle;
    private static bool _stylesInitialized;

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
    private static Action<string, string, LogType>? _logCallback;

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
                WriteLog("[Logger] Weapon tracking ACTIVE — scanning every 1.5s for equipped items, 0.8s for fire/effects");

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
        // ── Weapon scan (equipped state changes) ──
        if (Config.LogWeapons)
        {
            _weaponScanTimer -= Time.unscaledDeltaTime;
            if (_weaponScanTimer <= 0f)
            {
                _weaponScanTimer = WeaponScanInterval;
                ScanForWeapon();
            }

            _sceneScanTimer -= Time.unscaledDeltaTime;
            if (_sceneScanTimer <= 0f)
            {
                _sceneScanTimer = SceneScanInterval;
                BatchSceneScan();
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
        if (!_stylesInitialized || _viewerTitleStyle == null)
        {
            _viewerTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _viewerTitleStyle.normal.textColor = new Color(0.2f, 0.6f, 1f);

            _viewerBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            _viewerBtnStyle.normal.textColor = Color.white;
            _viewerBtnStyle.normal.background = MakeTex(new Color(0.25f, 0.25f, 0.35f));
            _viewerBtnStyle.hover.textColor = Color.white;
            _viewerBtnStyle.hover.background = MakeTex(new Color(0.35f, 0.35f, 0.45f));

            _viewerLogStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, richText = true };
            _viewerLogStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _stylesInitialized = true;
        }

        var titleRect = new Rect(rect.x + 6, y, rect.width - 12, 22);
        GUI.Label(titleRect, "Event Log Viewer", _viewerTitleStyle);
        y += 26;

        if (GUI.Button(new Rect(rect.x + 6, y, 60, 20), "Refresh", _viewerBtnStyle))
            RefreshLogContent();

        if (GUI.Button(new Rect(rect.x + 70, y, 60, 20), "Clear", _viewerBtnStyle))
        {
            ClearLog();
            RefreshLogContent();
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 66, y, 60, 20), "Close", _viewerBtnStyle))
            _logViewerVisible = false;

        y += 26;

        // Auto-refresh
        _logRefreshTimer -= Time.unscaledDeltaTime;
        if (_logRefreshTimer <= 0f)
        {
            _logRefreshTimer = 2f;
            RefreshLogContent();
        }

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
                GUI.Label(new Rect(4, drawY, viewWidth - 24, lineHeight), line, _viewerLogStyle);
            }
            drawY += lineHeight;
        }

        GUI.EndScrollView();
        GUI.EndGroup();
    }

    private static readonly Dictionary<long, Texture2D> _texCache = new();
    private static Texture2D MakeTex(Color c)
    {
        var key = (long)(c.r * 255) << 24 | (long)(c.g * 255) << 16 | (long)(c.b * 255) << 8 | (long)(c.a * 255);
        if (_texCache.TryGetValue(key, out var cached))
            return cached;

        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
        _texCache[key] = tex;
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
            var path = _crashLogPath;
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(GameRoot, CrashLogName);
            File.WriteAllText(path, $"=== Crash Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
    }

    internal static void WriteCrashLog(string message)
    {
        try
        {
            var path = _crashLogPath;
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(GameRoot, CrashLogName);
            File.AppendAllText(path, message + "\n");
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
            // Unity-level hooks — need engine initialized
            _logCallback = new Action<string, string, LogType>(OnUnityLogMessage);
            Application.add_logMessageReceived(_logCallback);

            WriteLog("[Logger] Crash hooks ACTIVE — all errors logged to CrashLog.txt");
        }
        catch (Exception ex)
        {
            WriteLog($"[Logger] Failed to set Unity crash hooks: {ex.Message}");
        }
    }

    private const int MaxCrashLogRate = 50;
    private static int _crashLogCount;
    private static readonly TimeSpan CrashLogWindow = TimeSpan.FromSeconds(30);
    private static DateTime _crashLogWindowStart = DateTime.MinValue;

    private static readonly string[] NoisePatterns = {
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
        if (now - _crashLogWindowStart > CrashLogWindow)
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

        if (IsNoise(condition))
            return;

        if (IsRateLimited())
            return;

        WriteCrashLog($"[Unity {type}] {condition}");
        if (type == LogType.Exception || stackTrace.Length > 50)
            WriteCrashLog(stackTrace);
        else
            WriteCrashLog("(no stack trace)");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;

        WriteCrashLog("=== UNHANDLED EXCEPTION (FATAL) ===");
        WriteCrashLog($"IsTerminating: {e.IsTerminating}");
        WriteCrashLog($"Exception: {ex?.GetType().FullName}");
        WriteCrashLog($"Message: {ex?.Message}");
        WriteCrashLog($"Stack: {ex?.StackTrace}");

        FlushCrashBuffer();
        WriteCrashLog("=== Crash Log Ended ===");
    }

    private static void LogCrashBuffer(Exception ex)
    {
        lock (CrashBuffer)
        {
            CrashBuffer.Add($"[FirstChance] {ex.GetType().Name}: {ex.Message}");
            if (CrashBuffer.Count > CrashBufferMax)
                CrashBuffer.RemoveAt(0);
        }
    }

    private static void FlushCrashBuffer()
    {
        lock (CrashBuffer)
        {
            WriteCrashLog("=== Recent Event Log ===");
            foreach (var entry in CrashBuffer)
            {
                WriteCrashLog(entry);
            }
            CrashBuffer.Clear();
        }
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

            if (lower == "savemsg" || lower.Contains("img_") || lower.Contains("ui_")) continue;

            if (lower.Contains("player") || lower.Contains("fps") || lower.Contains("character") ||
                lower.Contains("firstperson") || lower.Contains("cop") || lower.Contains("police") ||
                lower.Contains("sheriff") || lower.StartsWith("ems") || lower.StartsWith("fd"))
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
                var wasNull = _cachedPlayer == null;
                _cachedPlayer = found;
                _playerCacheTimer = PlayerCacheTimeout;

                if (!_didPlayerDump || wasNull)
                {
                    _didPlayerDump = true;
                    WriteLog($"[Weapon] Player hierarchy: \"{found.name}\" ({found.transform.childCount} children)");
                    DumpTransformHierarchy(found.transform, "  ", 5);
                }
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
                    _didPlayerDump = false;
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
            string? equippedWeapon = null;

            // ── Find equipped weapon (player/camera hierarchy) ──
            if (player != null)
                equippedWeapon = FindWeaponInHierarchy(player.transform);

            if (equippedWeapon == null && _cachedCamera != null)
                equippedWeapon = FindWeaponInHierarchy(_cachedCamera.transform);

            // ── First scan: root hierarchy dump (only if player not found yet) ──
            if (_firstScan && player == null)
            {
                _firstScan = false;
                WriteLog("[Weapon] Player not found — dumping root hierarchy:");
                foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
                {
                    if (obj == null || obj.transform.parent != null) continue;
                    WriteLog($"[Weapon] - \"{obj.name}\" [A] ({obj.transform.childCount} children)");
                }
            }
            else
                _firstScan = false;

            // ── Equipped weapon state change ──
            if (equippedWeapon != _lastWeapon)
            {
                var oldWep = _lastWeapon ?? "(none)";
                var newWep = equippedWeapon ?? "(none)";

                if (equippedWeapon == null)
                    WriteLog($"[Weapon] Holstered (was {oldWep})");
                else if (_lastWeapon == null)
                    WriteLog($"[Weapon] Drawn: {newWep}");
                else
                    WriteLog($"[Weapon] Switched: {oldWep} -> {newWep}");

                _lastWeapon = equippedWeapon;
            }

        }
        catch { }
    }

    private void BatchSceneScan()
    {
        try
        {
            _fireEffectCleanupTimer -= Time.unscaledDeltaTime;
            if (_fireEffectCleanupTimer <= 0f)
            {
                _fireEffectCleanupTimer = FireEffectCleanupInterval;
                _seenFireEffects.Clear();

                if (_seenNpcNames.Count > 200)
                    _seenNpcNames.Clear();

                if (_seenGenerationEvents.Count > 200)
                    _seenGenerationEvents.Clear();
            }

            bool checkFire = Config.LogWeapons;
            bool checkNpcName = Config.LogNpcNames;
            bool checkGen = Config.LogGeneration;

            if (!checkFire && !checkNpcName && !checkGen) return;

            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj == null || !obj.activeInHierarchy) continue;

                var name = obj.name;
                var lower = name.ToLowerInvariant();

                if (checkFire && _seenFireEffects.Add(name) && IsFireEffect(name, lower))
                    WriteLog($"[Weapon] FIRED: {name}");

                if (checkNpcName)
                {
                    if ((lower.Contains("generatedname") || lower.Contains("npcname") || lower.Contains("_name")) &&
                        _seenNpcNames.Add(name))
                    {
                        WriteLog($"[NPC Name] Generated: \"{name}\"");
                    }
                }

                if (checkGen)
                {
                    if ((lower.Contains("generation") || lower.Contains("worldgen") ||
                         lower.Contains("procgen") || lower.Contains("population") ||
                         lower.StartsWith("gen_")) && _seenGenerationEvents.Add(name))
                    {
                        WriteLog($"[Generation] Active: \"{name}\"");
                    }
                }
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

    private static void DumpTransformHierarchy(Transform t, string indent, int maxDepth)
    {
        if (maxDepth <= 0) return;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            var active = child.gameObject.activeInHierarchy ? "A" : "I";
            var weaponTag = IsWeaponName(child.name) ? " [WPN]" : "";
            GameEventLoggerMod.WriteLog($"{indent}\"{child.name}\" [{active}]{weaponTag} ({child.childCount} children)");
            DumpTransformHierarchy(child, indent + "  ", maxDepth - 1);
        }
    }

    private static readonly string[] WeaponContains = {
        "weapon", "firearm", "holster", "rifle", "pistol", "shotgun",
        "taser", "tazer", "flashlight", "stun", "wep_", "gun_",
        "m4", "mp5", "ak47", "ar15", "deagle", "glock", "beretta",
        "revolver", "magnum", "uzi", "smg", "carbine"
    };

    private static readonly string[] WeaponExact = { "bat", "knife", "tool", "item", "equipment", "gear", "gun" };

    private static readonly string[] WeaponExcludeContains = {
        "wheel", "pos_", "coll_", "mixamorig", "container", "group",
        "pool", "npc", "name", "database", "generation", "spawner",
        "char", "body", "bomb", "flame", "lod_", "level", "bip",
        "weapons", "img_", "ui_", "icon_", "ammo-case",
        "background", "ground", "terrain", "building", "vehicle"
    };

    private static readonly string[] WeaponExcludeExact = {
        "weapon", "weapon2"
    };

    // Fire/projectile effect patterns — these indicate a weapon was FIRED, not just equipped
    private static readonly string[] FireEffectPrefixes = {
        "shot-", "bullet_", "bullet-", "muzzle", "shell", "tracer",
        "projectile", "casing", "explosion", "explode", "spark"
    };
    private static readonly string[] FireEffectContains = {
        "muzzlefire", "case(", "_trail", "-taser", "impact"
    };
    private static readonly string[] FireEffectExclusions = {
        "coll_", "ground", "background", "pos_", "data-",
        "terrain", "building", "road", "decal", "decoration"
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

    private static bool IsFireEffect(string name, string? lower = null)
    {
        lower ??= name.ToLowerInvariant();

        foreach (var x in FireEffectExclusions)
            if (lower.StartsWith(x) || lower.Contains("_" + x) || lower.Contains("-" + x))
                return false;

        foreach (var p in FireEffectPrefixes)
            if (lower.StartsWith(p) || lower.Contains("_" + p) || lower.Contains("-" + p))
                return true;

        foreach (var p in FireEffectContains)
            if (lower.Contains(p)) return true;

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

        if (_logCallback != null)
            UnityEngine.Application.remove_logMessageReceived(_logCallback);
        _harmony?.UnpatchSelf();
        CloseLog();

        foreach (var tex in _texCache.Values)
            Object.Destroy(tex);
        _texCache.Clear();
    }
}
