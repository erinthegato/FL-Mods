using System.Collections.Generic;
using System.Linq;
using FLMods.Shared;
using FlashingLights.ModKit.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPCAI;

internal sealed class NpcIdentity
{
    public string Name { get; set; }
    public string Role { get; set; }
    public string Personality { get; set; }
    public string DateOfBirth { get; set; }
    public string Address { get; set; }

    public NpcIdentity(string name, string role, string personality, string dob, string address)
    {
        Name = name;
        Role = role;
        Personality = personality;
        DateOfBirth = dob;
        Address = address;
    }
}

[ModKitManifest(
    Id = "npc-ai",
    DisplayName = "NPC AI",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Gameplay")]
public sealed class NPCAIMod : ModKitMelonMod<NPCAIConfig>
{
    protected override string ModId => "npc-ai";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(1);
    internal const string KeyBindFile = "NPCAI.keybinds";

    internal static NPCAIMod Instance { get; private set; } = null!;

    private ChatService _chat = null!;
    private NPCInteractionUI _ui = null!;
    private bool _uiVisible;
    private bool _cursorWasLocked;
    private bool _isWaiting;
    private CancellationTokenSource? _cts;
    private GameObject? _nearbyNpc;
    private GameObject? _cachedPlayer;
    private float _keyBindReloadTimer;
    private float _nextNpcScanTime;
    private float _nextNpcCacheRefreshTime;
    private readonly List<GameObject> _npcCandidates = new();
    private Vector3 _lastInteractionPosition;
    private bool _hasLastInteractionPosition;
    private const float SamePositionTolerance = 1.5f;
    internal KeyCode ToggleKey { get; private set; } = KeyCode.F9;
    internal KeyCode SendKey { get; private set; } = KeyCode.Return;
    private HarmonyLib.Harmony? _inputHarmony;

    private readonly Dictionary<string, NpcIdentity> _npcIdentityCache = new();

    private static readonly string[] _personalities = new[]
    {
        "nervous and anxious, speaking quickly, easily intimidated",
        "cooperative and calm, answers questions directly",
        "hostile and aggressive, curses frequently, resists authority",
        "confused and disoriented, gives unreliable information",
        "smug and uncooperative, evasive answers",
        "scared and crying, barely coherent",
        "professional and measured (off-duty cop or military)",
        "drunk and slurring, rambling and unhelpful"
    };

    private const string InjuredPersonality = "injured and in pain, groaning, slow and strained speech, may ask for medical help";
    private const string FleeingPersonality = "breathing heavily, panicked, defensive, trying to get away, constantly looking over shoulder";
    private const string HostilePersonality = "angry, aggressive, challenging authority, using profanity, ready to fight";

    private static readonly string[] _names = new[]
    {
        "James Wilson", "Maria Garcia", "Robert Chen", "Patricia Davis",
        "Michael Brown", "Jennifer Miller", "David Martinez", "Sarah Johnson",
        "Christopher Lee", "Amanda Taylor", "Thomas Anderson", "Jessica White"
    };

    private static readonly string[] _roles = new[]
    {
        "witness", "victim", "suspect", "bystander",
        "reporting party", "store clerk", "neighbor", "security guard"
    };

    private string _currentNpcName = "Civilian";
    private string _currentNpcRole = "witness";
    private string _currentPersonality = "cooperative and calm";
    private string _currentNpcDob = "01/01/1980";
    private string _currentNpcAddress = "123 Main St, Anytown";

    protected override void OnModKitInitialized()
    {
        Instance = this;
        _inputHarmony = new HarmonyLib.Harmony("npc-ai.input-shield");
        _inputHarmony.PatchAll();
        _chat = new ChatService();
        _ui = new NPCInteractionUI();
        LoadKeyBinds();
        LogInfo($"NPC AI initialized. Press {ToggleKey} to interact.");
    }

    protected override void OnModKitEnabled() => LogInfo("NPC AI enabled.");

    protected override void OnModKitDisabled()
    {
        _uiVisible = false;
        _cts?.Cancel();
        _chat.StopTts();
        ModInputShield.SetBlocked(false);
        RestoreCursor();
        LogInfo("NPC AI disabled.");
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.ModEnabled) return;
        _keyBindReloadTimer -= Time.unscaledDeltaTime;
        if (_keyBindReloadTimer <= 0f)
        {
            _keyBindReloadTimer = 1f;
            LoadKeyBinds();
        }
        UpdateInputShield();

        if (!_isWaiting && Input.GetKeyDown(ToggleKey))
        {
            if (_uiVisible)
            {
                CloseUi(clearConversation: false);
                return;
            }

            _nearbyNpc = FindNearbyNpc(force: true);
            if (_nearbyNpc == null)
            {
                LogInfo("No nearby NPC/AI to interact with.");
                return;
            }

            _uiVisible = true;
            UpdateCursor();
            OnUiOpened(_nearbyNpc);
        }

        if (!LowPerformanceScanning && _uiVisible && FindNearbyNpc() == null)
        {
            CloseUi(clearConversation: false);
            return;
        }

        if (!_uiVisible) return;

        if (!_isWaiting && Input.GetKeyDown(SendKey))
        {
            var text = _ui.InputText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _ui.AddDialogue("Player", text);
                _ui.SetStatus("");
                _ = HandleInteractionAsync(text);
            }
        }
    }

    private void CloseUi(bool clearConversation)
    {
        _uiVisible = false;
        _cts?.Cancel();
        if (clearConversation) _chat.ClearHistory();
        _ui.OnGuiLostFocus();
        RestoreCursor();
    }

    private void LoadKeyBinds()
    {
        ToggleKey = KeyBindStore.Load(KeyBindFile, nameof(ToggleKey), ToggleKey);
        SendKey = KeyBindStore.Load(KeyBindFile, nameof(SendKey), SendKey);
    }

    private GameObject? FindNearbyNpc(bool force = false)
    {
        if (!force && LowPerformanceScanning && !_uiVisible)
            return null;

        if (!force && Time.unscaledTime < _nextNpcScanTime && IsValidNpc(_nearbyNpc))
            return _nearbyNpc;

        _nextNpcScanTime = Time.unscaledTime + Math.Max(0.25f, Config.NearbyScanIntervalSeconds);
        if (force || Time.unscaledTime >= _nextNpcCacheRefreshTime)
            RefreshNpcCache();

        var player = FindPlayer();
        if (player == null)
        {
            _nearbyNpc = null;
            return null;
        }

        float maxDist = Math.Max(1f, Config.InteractionRange);
        float best = maxDist * maxDist;
        GameObject? bestNpc = null;

        for (int i = _npcCandidates.Count - 1; i >= 0; i--)
        {
            var go = _npcCandidates[i];
            if (!IsValidNpc(go))
            {
                _npcCandidates.RemoveAt(i);
                continue;
            }

            float d = (go.transform.position - player.transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestNpc = go;
            }
        }

        _nearbyNpc = bestNpc;
        return _nearbyNpc;
    }

    private static bool IsValidNpc(GameObject? go) =>
        go != null && go.scene.IsValid() && go.activeInHierarchy && IsNpcCandidate(go) && HasNpcBody(go);

    private bool LowPerformanceScanning =>
        Config.PerformanceMode || !PerformanceSettings.Current.NpcAiScansAllowed;

    private void RefreshNpcCache()
    {
        _nextNpcCacheRefreshTime = Time.unscaledTime + Math.Max(2f, Config.NpcCacheRefreshIntervalSeconds);
        _npcCandidates.Clear();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null || !root.activeInHierarchy) continue;
            CollectNpcCandidates(root.transform);
        }
    }

    private void CollectNpcCandidates(Transform root)
    {
        if (root == null) return;
        var go = root.gameObject;
        if (IsValidNpc(go))
        {
            _npcCandidates.Add(go);
            return;
        }

        int count = root.childCount;
        for (int i = 0; i < count; i++)
            CollectNpcCandidates(root.GetChild(i));
    }

    private static bool IsNpcCandidate(GameObject go)
    {
        string n = go.name;
        if (n.Contains("Terrain", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Marker", StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith("-Pos", StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith("_Pos", StringComparison.OrdinalIgnoreCase))
            return false;

        if (n.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Civilian", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Ped", StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith("AI-", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("AI", StringComparison.OrdinalIgnoreCase))
            return true;

        var parent = go.transform.parent;
        while (parent != null)
        {
            string pn = parent.name;
            if (pn.Equals("Npcs", StringComparison.OrdinalIgnoreCase) ||
                pn.Contains("NPC", StringComparison.OrdinalIgnoreCase))
                return true;
            parent = parent.parent;
        }
        return false;
    }

    private static bool HasNpcBody(GameObject go) =>
        go.GetComponent<Collider>() != null ||
        go.GetComponent<Rigidbody>() != null ||
        go.GetComponent<Renderer>() != null;

    private static GameObject? FindPlayer()
    {
        if (Instance._cachedPlayer != null && Instance._cachedPlayer.activeInHierarchy)
            return Instance._cachedPlayer;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Instance._cachedPlayer = player;
            return player;
        }

        var camera = GameObject.Find("Main Camera");
        Instance._cachedPlayer = camera;
        return camera;
    }

    private enum NpcContextState { Normal, Injured, Fleeing, Hostile, Cooperative }
    private NpcContextState DetectNpcState(GameObject npc)
    {
        if (IsNpcInjured(npc)) return NpcContextState.Injured;
        if (IsNpcFleeing(npc)) return NpcContextState.Fleeing;
        if (IsNpcHostile(npc)) return NpcContextState.Hostile;
        return NpcContextState.Cooperative;
    }

    private bool IsNpcInjured(GameObject npc) =>
        npc.name.Contains("Injured", StringComparison.OrdinalIgnoreCase);

	private bool IsNpcFleeing(GameObject npc)
{
    var rb = npc.GetComponent<Rigidbody>();
    if (rb != null && rb.velocity.magnitude > 3f) return true;

    var anim = npc.GetComponentInChildren<Animator>();
    if (anim != null)
    {
        // GetBool returns false if the parameter doesn't exist, safe to use directly
        return anim.GetBool("isRunning");
    }
    return false;
}

    private bool IsNpcHostile(GameObject npc) =>
        npc.name.Contains("Hostile", StringComparison.OrdinalIgnoreCase) || npc.CompareTag("Enemy");

    private string GenerateConsistentDob() =>
        $"{UnityEngine.Random.Range(1, 13)}/{UnityEngine.Random.Range(1, 29)}/{UnityEngine.Random.Range(1960, 2005)}";

    private string GenerateConsistentAddress() =>
        $"{UnityEngine.Random.Range(100, 9999)} {new[] { "Main", "Oak", "Pine", "Maple", "Cedar" }[UnityEngine.Random.Range(0, 5)]} St, {new[] { "Springfield", "Lakewood", "Hillcrest", "Fairview", "Riverside" }[UnityEngine.Random.Range(0, 5)]}";

    private string GetCacheKey(GameObject npc) =>
        $"{SceneManager.GetActiveScene().buildIndex}_{npc.GetInstanceID()}";

    private void OnUiOpened(GameObject npc)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _chat.Cancellation = _cts.Token;

        var player = FindPlayer();
        Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
        bool samePosition = _hasLastInteractionPosition &&
                            (playerPos - _lastInteractionPosition).sqrMagnitude <= SamePositionTolerance * SamePositionTolerance;

        string key = GetCacheKey(npc);
        if (!_npcIdentityCache.TryGetValue(key, out var identity))
        {
            string name = ResolveNpcName(npc);
            string role = _roles[UnityEngine.Random.Range(0, _roles.Length)];
            string personality = _personalities[UnityEngine.Random.Range(0, _personalities.Length)];
            string dob = GenerateConsistentDob();
            string address = GenerateConsistentAddress();

            NpcContextState state = DetectNpcState(npc);
            switch (state)
            {
                case NpcContextState.Injured:
                    personality = InjuredPersonality;
                    role = "victim";
                    break;
                case NpcContextState.Fleeing:
                    personality = FleeingPersonality;
                    role = "suspect";
                    break;
                case NpcContextState.Hostile:
                    personality = HostilePersonality;
                    role = "suspect";
                    break;
                case NpcContextState.Cooperative:
                    if (role == "suspect") role = "bystander";
                    break;
            }

            identity = new NpcIdentity(name, role, personality, dob, address);
            _npcIdentityCache[key] = identity;
        }

        _currentNpcName = identity.Name;
        _currentNpcRole = identity.Role;
        _currentPersonality = identity.Personality;
        _currentNpcDob = identity.DateOfBirth;
        _currentNpcAddress = identity.Address;

        _lastInteractionPosition = playerPos;
        _hasLastInteractionPosition = true;

        _ui.SetNpcContext(_currentNpcName, _currentNpcRole, _currentPersonality, _currentNpcDob, _currentNpcAddress);
        _chat.SetSystemContext(_ui.GetSystemContext());

        if (!samePosition)
        {
            var greeting = $"A {_currentNpcRole} is nearby. You approach and begin speaking.";
            _ui.AddDialogue("System", greeting);
            _ = SendInitialGreetingAsync();
        }
    }

    private static string ResolveNpcName(GameObject npc)
    {
        string name = npc.name.Trim();
        if (LooksLikeName(name)) return name;
        return _names[UnityEngine.Random.Range(0, _names.Length)];
    }

    private static bool LooksLikeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains("NPC", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("AI", StringComparison.OrdinalIgnoreCase)) return false;
        return value.Contains(' ') && value.Any(char.IsLetter);
    }

    private async Task SendInitialGreetingAsync()
    {
        _isWaiting = true;
        _ui.IsThinking = true;
        _ui.SetStatus("Establishing connection...");

        _chat.Configure(Config.ApiKey, Config.ApiEndpoint, Config.ApiModel);
        _chat.AudioVolume = Config.AudioVolume;

        var response = await _chat.SendMessageAsync(
            $"An officer approaches you. Your name is {_currentNpcName}, your role is {_currentNpcRole}. " +
            $"Your personality: {_currentPersonality}. Respond naturally as this character. Say something appropriate for the situation.");

        if (!_cts!.IsCancellationRequested)
        {
            _ui.AddDialogue(_currentNpcName, response);
            _chat.SpeakText(response);
        }

        _isWaiting = false;
        _ui.IsThinking = false;
    }

    private async Task HandleInteractionAsync(string message)
    {
        _isWaiting = true;
        _ui.IsThinking = true;
        _ui.SetStatus("");

        if (message == "[[ASK_ID]]" && Config.EnableIdLookup)
        {
            string idMessage = $"The officer asks for your identification. You hand over your ID card showing Name: {_currentNpcName}, DOB: {_currentNpcDob}, Address: {_currentNpcAddress}. Respond as the character while providing the ID.";
            var idResponse = await _chat.SendMessageAsync(idMessage);
            if (!_cts!.IsCancellationRequested)
            {
                _ui.AddDialogue(_currentNpcName, idResponse);
                _chat.SpeakText(idResponse);
            }
        }
        else if (message == "[[MDT_LOOKUP]]" && Config.EnableIdLookup)
        {
            string mdtMessage = $"The officer runs your name ({_currentNpcName}) through MDT. " +
                               $"Generate a realistic criminal record or clean record (50/50 chance). " +
                               $"If clean: say 'No warrants, no priors.' If record exists: mention minor offenses (e.g., 'I had a DUI in 2018' or 'public intoxication last year'). " +
                               $"Respond as the character hearing the officer read the MDT results.";
            var mdtResponse = await _chat.SendMessageAsync(mdtMessage);
            if (!_cts!.IsCancellationRequested)
            {
                _ui.AddDialogue(_currentNpcName, mdtResponse);
                _chat.SpeakText(mdtResponse);
            }
        }
        else if (message == "[[DISPATCH]]" && Config.EnableDispatchIntegration)
        {
            NotifyDispatch();
            _ui.SetStatus("Dispatch notified.");
        }
        else
        {
            var response = await _chat.SendMessageAsync(message);
            if (!_cts!.IsCancellationRequested)
            {
                _ui.AddDialogue(_currentNpcName, response);
                _chat.SpeakText(response);
            }
        }

        _isWaiting = false;
        _ui.IsThinking = false;
    }

    private void NotifyDispatch()
    {
        LogInfo($"Dispatch notification: Officer interacting with {_currentNpcName} ({_currentNpcRole}) at approx {_lastInteractionPosition}");
    }

    protected override void OnModKitGui()
    {
        if (!_uiVisible) return;
        if (FindNearbyNpc() == null) return;

        float w = 420, h = 520;
        var rect = new Rect(Screen.width - w - 10, 60, w, h);

        var sent = _ui.Draw(rect, _isWaiting, Config, this);
        if (!string.IsNullOrEmpty(sent))
        {
            _ui.AddDialogue("Player", sent);
            _ = HandleInteractionAsync(sent);
        }
    }

    private void UpdateCursor()
    {
        if (_uiVisible)
        {
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
        else
        {
            RestoreCursor();
        }
    }

    private void RestoreCursor()
    {
        Time.timeScale = 1f;
        if (_cursorWasLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void UpdateInputShield()
    {
        ModInputShield.SetBlocked(_uiVisible, ToggleKey, SendKey);
    }
}
