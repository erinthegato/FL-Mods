using UnityEngine;

namespace GrammarPoliceMod;

public sealed class RadioUI
{
    private readonly CommandEngine _engine;
    private readonly DispatchAudio _audio;
    private Vector2 _scrollPos;
    private string _statusText = "";
    private float _statusTimer;
    private const float StatusDuration = 4f;

    private int _selectedIndex;
    private float _itemsViewHeight;

    private enum OverlayState { Idle, Transmitting }
    private OverlayState _overlayState;
    private string? _currentCode;
    private string? _currentMessage;
    private float _overlayTimer;
    private float _overlayDuration = 4f;

    private bool _stylesReady;
    private GUIStyle _overlayBg = null!;
    private GUIStyle _overlayYou = null!;
    private GUIStyle _selectedBtn = null!;

    private sealed record RadioItem(string Code, string Description, string Section);
    private static readonly RadioItem[] Items = new RadioItem[]
    {
        new("10-1",  "Unable to Copy — Signal Weak",     "STATUS & AVAILABILITY"),
        new("10-2",  "Receiving Well",                     "STATUS & AVAILABILITY"),
        new("10-3",  "Stop Transmitting",                  "STATUS & AVAILABILITY"),
        new("10-4",  "Acknowledged",                       "STATUS & AVAILABILITY"),
        new("10-5",  "Relay",                              "STATUS & AVAILABILITY"),
        new("10-6",  "Busy — Stand By",                    "STATUS & AVAILABILITY"),
        new("10-7",  "Out of Service",                     "STATUS & AVAILABILITY"),
        new("10-8",  "In Service",                         "STATUS & AVAILABILITY"),
        new("10-9",  "Say Again",                          "STATUS & AVAILABILITY"),
        new("10-10", "Fight in Progress",                  "ENFORCEMENT"),
        new("10-11", "Animal Problem",                     "ENFORCEMENT"),
        new("10-15", "Prisoner in Custody",                "ENFORCEMENT"),
        new("10-16", "Pick Up for Questioning",            "ENFORCEMENT"),
        new("10-22", "Disregard",                          "ENFORCEMENT"),
        new("10-26", "Detaining Subject",                  "ENFORCEMENT"),
        new("10-31", "Crime in Progress",                  "ENFORCEMENT"),
        new("10-32", "Man with Gun / Shots Fired",        "ENFORCEMENT"),
        new("10-33", "Emergency — All Units Stand By",     "ENFORCEMENT"),
        new("10-35", "Major Crime Alert",                  "ENFORCEMENT"),
        new("10-50", "Traffic Stop / Accident",            "ENFORCEMENT"),
        new("10-54", "Hit and Run",                        "ENFORCEMENT"),
        new("10-55", "Stolen Vehicle",                     "ENFORCEMENT"),
        new("10-56", "Intoxicated Driver",                 "ENFORCEMENT"),
        new("10-57", "Reckless Driving",                   "ENFORCEMENT"),
        new("10-78", "Officer Needs Assistance",           "ENFORCEMENT"),
        new("10-80", "Pursuit in Progress",                "ENFORCEMENT"),
        new("10-89", "Bomb Threat",                        "ENFORCEMENT"),
        new("10-90", "Bank Alarm",                         "ENFORCEMENT"),
        new("10-99", "Wanted / Stolen Record",             "ENFORCEMENT"),
        new("10-20", "Requesting Location",                "DISPATCHER"),
        new("10-21", "Call by Telephone",                  "DISPATCHER"),
        new("10-23", "Arrived on Scene",                   "DISPATCHER"),
        new("10-24", "Assignment Complete",                "DISPATCHER"),
        new("10-25", "Meet With",                          "DISPATCHER"),
        new("10-27", "Drivers License Check",              "DISPATCHER"),
        new("10-28", "Vehicle Registration Check",         "DISPATCHER"),
        new("10-29", "Check for Wants / Warrants",         "DISPATCHER"),
        new("10-76", "En Route",                           "DISPATCHER"),
        new("10-97", "Arrived on Scene",                   "DISPATCHER"),
        new("10-98", "Assignment Complete",                "DISPATCHER"),
        new("10-12", "Stand By",                           "EMERGENCY"),
        new("10-13", "Weather / Road Report",              "EMERGENCY"),
        new("10-14", "Escort",                             "EMERGENCY"),
        new("10-17", "Meet Complainant",                   "EMERGENCY"),
        new("10-18", "Complete Quickly",                   "EMERGENCY"),
        new("10-19", "Return to Station",                  "EMERGENCY"),
        new("10-30", "Unnecessary Use of Radio",           "EMERGENCY"),
        new("10-34", "Riot",                               "EMERGENCY"),
        new("10-51", "Wrecker Needed",                     "EMERGENCY"),
        new("10-52", "Ambulance Needed",                   "EMERGENCY"),
        new("10-53", "Road Blocked",                       "EMERGENCY"),
        new("10-79", "Notify Coroner",                     "EMERGENCY"),
        new("Code 2", "Request Backup (Non-Emergency)",    "BACKUP CODES"),
        new("Code 3", "Request Backup (Emergency)",        "BACKUP CODES"),
        new("Code 4", "All Clear — Situation Resolved",   "BACKUP CODES"),
        new("Cancel Backup", "Cancel Backup Request",      "BACKUP CODES"),
        new("Radio Check", "Communications Check — OK",   "RADIO CHECK"),
        new("Signal 1", "Minor Incident Reported",         "RADIO CHECK"),
        new("Signal 2", "Major Incident Reported",         "RADIO CHECK"),
    };


    public RadioUI(CommandEngine engine, DispatchAudio audio)
    {
        _engine = engine;
        _audio = audio;
        _engine.CommandRecognized += OnCommandRecognized;
    }

    private void OnCommandRecognized(CommandEventArgs args)
    {
        Transmit(args.Code, args.Message);
    }

    public void SetStatus(string text)
    {
        _statusText = text;
        _statusTimer = Time.realtimeSinceStartup;
    }

    public void Transmit(string code, string message)
    {
        _currentCode = code;
        _currentMessage = message;
        _overlayState = OverlayState.Transmitting;
        _overlayTimer = _overlayDuration;
        _audio.PlayAsync(code);
    }

    public void Update(float dt)
    {
        if (_overlayState == OverlayState.Idle) return;

        _overlayTimer -= dt;
        if (_overlayTimer > 0f) return;

        _overlayState = OverlayState.Idle;
        _currentCode = null;
        _currentMessage = null;
    }

    public void SetOverlayDuration(float seconds)
    {
        _overlayDuration = seconds;
    }

    public void HandleKeyboard(GrammarPoliceConfig config)
    {
        var ev = Event.current;
        if (ev.type != EventType.KeyDown) return;

        if (ev.keyCode == GrammarPoliceMod.Instance.RadioNavigateUpKey)
        {
            _selectedIndex = (_selectedIndex - 1 + Items.Length) % Items.Length;
            ScrollToSelected();
            ev.Use();
        }
        else if (ev.keyCode == GrammarPoliceMod.Instance.RadioNavigateDownKey)
        {
            _selectedIndex = (_selectedIndex + 1) % Items.Length;
            ScrollToSelected();
            ev.Use();
        }
        else if (ev.keyCode == GrammarPoliceMod.Instance.RadioSelectKey)
        {
            var item = Items[_selectedIndex];
            Dispatch(item.Code, item.Description);
            ev.Use();
        }
    }

    private void ScrollToSelected()
    {
        float itemH = 26f;
        float viewH = _itemsViewHeight;
        float targetY = _selectedIndex * itemH;

        if (targetY < _scrollPos.y)
            _scrollPos.y = targetY;
        else if (targetY + itemH > _scrollPos.y + viewH)
            _scrollPos.y = targetY + itemH - viewH;
    }

    public void RenderPanel(Rect rect, CommandEngine engine, GrammarPoliceConfig config)
    {
        InitStyles();
        HandleKeyboard(config);

        GUI.Box(rect, "");

        var headerRect = new Rect(rect.x, rect.y, rect.width, 30);
        GUI.Box(headerRect, "<b>GRAMMAR POLICE RADIO  —  LAKE COUNTY SHERIFF</b>");

        var statusRect = new Rect(rect.x + 5, rect.y + 35, rect.width - 10, 22);
        var elapsed = Time.realtimeSinceStartup - _statusTimer;
        var status = elapsed < StatusDuration && !string.IsNullOrEmpty(_statusText)
            ? _statusText
            : engine.LastTransmission;
        GUI.Label(statusRect, status);

        string hint;
        if (_selectedIndex >= 0 && _selectedIndex < Items.Length)
            hint = $"<color=grey>Selected: [{Items[_selectedIndex].Code}]  {Items[_selectedIndex].Section}  (↑↓ enter, click to send)</color>";
        else
            hint = "<color=grey>↑↓ navigate, Enter/Space to send, click to send</color>";
        var hintRect = new Rect(rect.x + 5, rect.y + 57, rect.width - 10, 16);
        GUI.Label(hintRect, hint);

        float contentY = rect.y + 76;
        float contentH = rect.height - 92;
        _itemsViewHeight = contentH;

        GUILayout.BeginArea(new Rect(rect.x + 5, contentY, rect.width - 10, contentH));

        _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Width(rect.width - 10), GUILayout.Height(contentH));

        string? currentSection = null;
        int displayIndex = 0;

        foreach (var item in Items)
        {
            if (item.Section != currentSection)
            {
                currentSection = item.Section;
                GUILayout.Space(2);
                GUILayout.Label($"<b>== {item.Section} ==</b>");
            }

            bool isSelected = displayIndex == _selectedIndex;
            var btnStyle = isSelected ? _selectedBtn : GUI.skin.button;

            if (GUILayout.Button($"[{item.Code}]  {item.Description}", btnStyle, GUILayout.Height(24)))
            {
                _selectedIndex = displayIndex;
                Dispatch(item.Code, item.Description);
            }

            displayIndex++;
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

    }

    public void RenderOverlay()
    {
        InitStyles();
        if (_overlayState == OverlayState.Idle || _currentCode == null) return;

        float ow = Screen.width * 0.55f;
        float ox = (Screen.width - ow) * 0.5f;
        float oy = Screen.height - 150f;

        GUI.Box(new Rect(ox, oy, ow, 42f), GUIContent.none, _overlayBg);

        GUI.Label(new Rect(ox + 10f, oy + 8f, ow - 20f, 28f),
            $"[YOU]: {_currentCode} — {_currentMessage}", _overlayYou);
    }

    public void ResetSelection()
    {
        _selectedIndex = 0;
    }

    private void InitStyles()
    {
        if (_stylesReady || GUI.skin == null) return;
        _stylesReady = true;

        _overlayBg = new GUIStyle(GUI.skin.box);

        _overlayYou = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.3f, 0.95f, 0.3f) }
        };

        _selectedBtn = new GUIStyle(GUI.skin.button);
        _selectedBtn.normal.textColor = new Color(0.2f, 0.9f, 0.2f);
        _selectedBtn.hover.textColor = new Color(0.2f, 0.9f, 0.2f);

        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, new Color(0.15f, 0.35f, 0.15f));
        tex.Apply();
        _selectedBtn.normal.background = tex;
    }

    private void Dispatch(string code, string message)
    {
        _engine.ExecuteCommand(code, message);
        SetStatus($"<color=yellow>[TRANSMITTING]</color> {code}: {message}");
    }
}
