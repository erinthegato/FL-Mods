using System.IO;
using UnityEngine;

namespace NPCAI;

public sealed class NPCInteractionUI
{
    private static readonly string PromptPath =
        Path.Combine(Path.GetDirectoryName(typeof(NPCInteractionUI).Assembly.Location) ?? ".", "NPCI_GeminiPrompt.txt");

    private static string? _cachedPrompt;

    private static string LoadPrompt()
    {
        if (_cachedPrompt != null) return _cachedPrompt;
        try
        {
            if (File.Exists(PromptPath))
                _cachedPrompt = File.ReadAllText(PromptPath);
        }
        catch { }
        return _cachedPrompt ?? "";
    }

    private Vector2 _scrollPos;
    private string _inputText = "";
    private string _activeNpcName = "Civilian";
    private string _npcRole = "Unknown";
    private readonly List<string> _dialogue = new();
    private readonly List<float> _lineHeights = new();
    private bool _heightsDirty = true;
    private bool _inputActive;
    private string _statusText = "";
    private float _statusTimer;

    public bool IsThinking { get; set; }
    public string InputText => _inputText;

    private static GUIStyle? _titleStyle;
    private static GUIStyle? _chatStyle;
    private static GUIStyle? _inputStyle;
    private static GUIStyle? _statusStyle;
    private static GUIStyle? _btnStyle;
    private static Texture2D? _bgTex;
    private static bool _stylesReady;

    private static readonly string[] _quickActions = new[]
    {
        "Who are you?", "What happened here?",
        "Do you know the suspect?", "Are you injured?",
        "Stay calm.", "You're free to go."
    };

    private static void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        var dark = new Color(0.08f, 0.08f, 0.15f);
        var btnNormal = MakeTex(new Color(0.2f, 0.2f, 0.3f));
        var btnHover = MakeTex(new Color(0.3f, 0.3f, 0.4f));
        var inputBg = MakeTex(Color.white);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.2f, 0.6f, 1f) }
        };

        _chatStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, wordWrap = true,
            richText = true,
            normal = { textColor = Color.white }
        };

        _inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 13,
            normal = { textColor = Color.white, background = inputBg },
            focused = { textColor = Color.white, background = inputBg }
        };

        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.gray }
        };

        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            normal = { textColor = Color.white, background = btnNormal },
            hover = { textColor = Color.white, background = btnHover }
        };

        _bgTex = MakeTex(dark);
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    public void SetNpcContext(string name, string role, string personality)
    {
        _activeNpcName = name;
        _npcRole = role;
    }

    public string GetSystemContext()
    {
        var prompt = LoadPrompt();
        if (!string.IsNullOrEmpty(prompt))
            return $"{prompt}\n\nYour name is {_activeNpcName}.";
        return $"You are roleplaying as an NPC named {_activeNpcName} in a police simulation game. " +
               $"Your role is: {_npcRole}. " +
               $"Respond as this character would in a police interaction. Keep responses to 1-3 sentences. " +
               $"Never mention being an AI. Express emotion through word choice, not by naming it.";
    }

    public void AddDialogue(string speaker, string text)
    {
        _dialogue.Add($"<color={(speaker == "Player" ? "#4FC3F7" : "#81C784")}>[{speaker}]:</color> {text}");
        _heightsDirty = true;
        _scrollPos.y = 99999;
    }

    public void SetStatus(string text)
    {
        _statusText = text;
        _statusTimer = 3f;
    }

    public void Clear()
    {
        _dialogue.Clear();
        _lineHeights.Clear();
        _inputText = "";
        _scrollPos = Vector2.zero;
    }

    private void RecalcHeights(float width)
    {
        _lineHeights.Clear();
        foreach (var line in _dialogue)
        {
            _lineHeights.Add(_chatStyle!.CalcHeight(new GUIContent(line), width));
        }
        _heightsDirty = false;
    }

    public string Draw(Rect rect, bool isLoading)
    {
        EnsureStyles();

        GUI.DrawTexture(rect, _bgTex!);
        float y = rect.y + 6;

        var header = $"{_activeNpcName} — {_npcRole}";
        GUI.Label(new Rect(rect.x, y, rect.width, 22), header, _titleStyle);
        y += 26;

        if (isLoading)
        {
            var loadStyle = _statusStyle!;
            loadStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(rect.x, y, rect.width, 20), "Thinking...", loadStyle);
            y += 22;
        }

        var chatRect = new Rect(rect.x + 4, y, rect.width - 8, rect.height - y - 110);
        GUI.BeginGroup(chatRect);

        float cw = chatRect.width, ch = chatRect.height;

        if (_heightsDirty)
            RecalcHeights(cw - 30);

        float totalH = 4;
        foreach (var h in _lineHeights)
            totalH += h + 4;

        totalH = Mathf.Max(ch, totalH);
        _scrollPos = GUI.BeginScrollView(new Rect(0, 0, cw, ch), _scrollPos, new Rect(0, 0, cw - 20, totalH));

        float iy = 4;
        for (int i = 0; i < _dialogue.Count; i++)
        {
            var h = _lineHeights[i];
            GUI.Label(new Rect(6, iy, cw - 32, h), _dialogue[i], _chatStyle);
            iy += h + 4;
        }

        GUI.EndScrollView();
        GUI.EndGroup();

        float iy2 = rect.y + rect.height - 80;
        var btnRect = new Rect(rect.x + 4, iy2, rect.width - 8, 22);
        if (!isLoading && GUI.Button(btnRect, "Send (ENTER)", _btnStyle!))
        {
            var result = _inputText;
            _inputText = "";
            return result;
        }

        var inputRect = new Rect(rect.x + 4, iy2 + 24, rect.width - 8, 22);
        GUI.SetNextControlName("npcInput");
        _inputText = GUI.TextField(inputRect, _inputText, 200, _inputStyle);

        if (!_inputActive && !isLoading)
        {
            GUI.FocusControl("npcInput");
            _inputActive = true;
        }

        float qy = iy2 + 50;
        float qw = (rect.width - 16) / 3;
        for (int i = 0; i < _quickActions.Length; i++)
        {
            if (i % 3 == 0) qy = iy2 + 50 + (i / 3) * 22;
            var qr = new Rect(rect.x + 4 + (i % 3) * (qw + 2), qy, qw, 20);
            if (!isLoading && GUI.Button(qr, _quickActions[i], _btnStyle!))
            {
                _inputText = _quickActions[i];
                var result = _quickActions[i];
                _inputText = "";
                return result;
            }
        }

        if (_statusTimer > 0)
        {
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer > 0 && !string.IsNullOrEmpty(_statusText))
            {
                var s = _statusStyle!;
                s.normal.textColor = Color.gray;
                GUI.Label(new Rect(rect.x, rect.y + rect.height - 18, rect.width, 18), _statusText, s);
            }
        }

        return "";
    }

    public void OnGuiLostFocus()
    {
        _inputActive = false;
    }
}
