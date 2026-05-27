using System.Collections.Generic;
using FlashingLights.ModKit.Core;
using UnityEngine;

namespace NPCAI;

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

    internal static NPCAIMod Instance { get; private set; } = null!;

    private ChatService _chat = null!;
    private NPCInteractionUI _ui = null!;
    private bool _uiVisible;
    private bool _cursorWasLocked;
    private bool _isWaiting;
    private CancellationTokenSource? _cts;
    private GameObject? _nearbyNpc;
    private GameObject? _cachedPlayer;
    private float _nextNpcScanTime;
    private Vector3 _lastInteractionPosition;
    private bool _hasLastInteractionPosition;
    private const float SamePositionTolerance = 1.5f;
    private const float NearbyScanInterval = 0.35f;

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

    protected override void OnModKitInitialized()
    {
        Instance = this;

        _chat = new ChatService();
        _ui = new NPCInteractionUI();

        LogInfo("NPC AI initialized. Press F9 to interact.");
    }

    protected override void OnModKitEnabled()
    {
        LogInfo("NPC AI enabled.");
    }

    protected override void OnModKitDisabled()
    {
        _uiVisible = false;
        _cts?.Cancel();
        RestoreCursor();
        LogInfo("NPC AI disabled.");
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.ModEnabled) return;

        if (!_isWaiting && Input.GetKeyDown(Config.ToggleKey))
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

        if (_uiVisible && FindNearbyNpc() == null)
        {
            CloseUi(clearConversation: false);
            return;
        }

        if (!_uiVisible) return;

        if (!_isWaiting && Input.GetKeyDown(KeyCode.Return))
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
        if (clearConversation)
            _chat.ClearHistory();
        _ui.OnGuiLostFocus();
        RestoreCursor();
    }

    private GameObject? FindNearbyNpc(bool force = false)
    {
        if (!force && Time.unscaledTime < _nextNpcScanTime && IsValidNpc(_nearbyNpc))
            return _nearbyNpc;

        _nextNpcScanTime = Time.unscaledTime + NearbyScanInterval;
        var player = FindPlayer();
        if (player == null)
        {
            _nearbyNpc = null;
            return null;
        }

        float maxDist = Math.Max(1f, Config.InteractionRange);
        float best = maxDist * maxDist;
        GameObject? bestNpc = null;

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !go.scene.IsValid() || !go.activeInHierarchy) continue;
            if (!IsNpcCandidate(go)) continue;

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
        go != null && go.scene.IsValid() && go.activeInHierarchy && IsNpcCandidate(go);

    private static bool IsNpcCandidate(GameObject go)
    {
        string n = go.name;
        if (n.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Civilian", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Ped", StringComparison.OrdinalIgnoreCase))
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

    private void OnUiOpened(GameObject npc)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _chat.Cancellation = _cts.Token;

        var player = FindPlayer();
        Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
        bool samePosition = _hasLastInteractionPosition &&
                            (playerPos - _lastInteractionPosition).sqrMagnitude <= SamePositionTolerance * SamePositionTolerance;

        if (!samePosition)
        {
            _chat.ClearHistory();
            _ui.Clear();
            _currentNpcName = ResolveNpcName(npc);
            _currentNpcRole = _roles[UnityEngine.Random.Range(0, _roles.Length)];
            _currentPersonality = _personalities[UnityEngine.Random.Range(0, _personalities.Length)];
        }

        _lastInteractionPosition = playerPos;
        _hasLastInteractionPosition = true;

        _ui.SetNpcContext(_currentNpcName, _currentNpcRole, _currentPersonality);
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
            "An officer approaches you. " +
            $"Your name is {_currentNpcName}, your role is {_currentNpcRole}. " +
            $"Respond naturally as this character. Say something appropriate for the situation.");

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

        var response = await _chat.SendMessageAsync(message);

        if (!_cts!.IsCancellationRequested)
        {
            _ui.AddDialogue(_currentNpcName, response);
            _chat.SpeakText(response);
        }

        _isWaiting = false;
        _ui.IsThinking = false;
    }

    protected override void OnModKitGui()
    {
        if (!_uiVisible) return;
        if (FindNearbyNpc() == null) return;

        float w = 420, h = 460;
        var rect = new Rect(Screen.width - w - 10, 60, w, h);

        var sent = _ui.Draw(rect, _isWaiting);
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
}
