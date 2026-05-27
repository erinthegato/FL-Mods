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
            _uiVisible = !_uiVisible;
            UpdateCursor();
            if (_uiVisible)
            {
                OnUiOpened();
            }
            else
            {
                _cts?.Cancel();
                _chat.ClearHistory();
                _ui.Clear();
                _ui.OnGuiLostFocus();
            }
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

    private void OnUiOpened()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _chat.Cancellation = _cts.Token;

        _chat.ClearHistory();
        _ui.Clear();

        _currentNpcName = _names[UnityEngine.Random.Range(0, _names.Length)];
        _currentNpcRole = _roles[UnityEngine.Random.Range(0, _roles.Length)];
        _currentPersonality = _personalities[UnityEngine.Random.Range(0, _personalities.Length)];

        _ui.SetNpcContext(_currentNpcName, _currentNpcRole, _currentPersonality);
        _chat.SetSystemContext(_ui.GetSystemContext());

        var greeting = $"A {_currentNpcRole} is nearby. You approach and begin speaking.";
        _ui.AddDialogue("System", greeting);

        _ = SendInitialGreetingAsync();
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
