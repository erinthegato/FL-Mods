using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MDTMod;

public sealed class MDTUI
{
    private enum Tab { Filing, Court, Records }
    private enum MDTRole { Police, Fire, EMS }

    private Tab _activeTab = Tab.Filing;
    private Vector2 _scrollPos;
    private string _subjectName = "";
    private string _firstName = "";
    private string _lastName = "";
    private string _officerName = "Officer";
    private string _statusMessage = "";
    private float _statusTimer;
    private int _selectedChargeIndex = -1;
    private int _selectedCitationIndex = -1;
    private int _filingModeIndex;
    private string _chargeSearch = "";
    private bool _showChargeDropdown = true;
    private string[] _cachedSubjects = Array.Empty<string>();
    private int _lastDataVersion = -1;
    private bool _chargeSubjectSelected;
    private PhotoBrowser _photoBrowser = new();
    private bool _photoBrowserVisible;
    private MDTRole _currentRole = MDTRole.Police;
    private GameObject? _cachedPlayer;
    private readonly Dictionary<string, Texture2D> _photoCache = new();

    private List<USCharge> _pendingCharges = new();
    private bool _showArrestNarrative;
    private string _locationZip = "";
    private string _locationStreet = "";
    private string _arrestNature = "";
    private string _arrestDOB = "";
    private string _arrestLicense = "";
    private string _witnessName = "";
    private string _witnessStatement = "";
    private List<WitnessStatement> _arrestWitnesses = new();

    private static readonly string[] RoleNames = { "LAW ENFORCEMENT", "FIRE RESCUE", "EMS" };
    private static readonly string[] TabNames = { "FILING", "COURT", "RECORDS" };
    private static readonly string[] FilingModes = { "Arrest / Charges", "Citation" };
    private static readonly string[] RegistrationStatuses = { "Valid", "Expired", "Suspended", "Revoked" };
    private static readonly string[] WantedStatuses = { "Clear", "Wanted", "Missing" };
    private static readonly string[] LicenseStatuses = { "Valid", "Suspended", "Revoked", "Expired" };
    private static readonly string[] StreetOptions =
    {
        "suburbs west", "suburbs east", "route 600", "interstate 69", "cod town",
        "route 20", "beach town", "port", "city", "city marina"
    };
    private const float TabW = 120;

    private bool _showModifyView;
    private string _modifySubjectName = "";
    private string _modifyNewFirstName = "";
    private string _modifyNewLastName = "";
    private bool _showNewRecordForm;
    private string _recordFirstName = "";
    private string _recordLastName = "";
    private string _recordRegistrationStatus = "Valid";
    private string _recordWantedStatus = "Clear";
    private string _recordLicenseStatus = "Valid";
    private string _recordLicensePlate = "";
    private bool _recordWeaponLicense;

    private Color _bgColor, _borderColor, _accentColor, _textColor;
    private float _roleTimer;

    private static Texture2D _texBg = null!;
    private static Texture2D _texBorder = null!;
    private static Texture2D _texLine = null!;
    private static Texture2D _texHighlight = null!;
    private static Color _cachedBg;
    private static Color _cachedAccent;

    private static GUIStyle _labelStyle = null!;
    private static GUIStyle _boldLabel = null!;
    private static GUIStyle _catLabel = null!;
    private static GUIStyle _inputField = null!;
    private static GUIStyle _chargeBtn = null!;
    private static GUIStyle _selectedBtn = null!;
    private static GUIStyle _headerStyle = null!;
    private static GUIStyle _clockStyle = null!;
    private static GUIStyle _tabBtn = null!;
    private static GUIStyle _statusStyle = null!;
    private static GUIStyle _recordsBtn = null!;
    private static GUIStyle _recordsModifyBtn = null!;
    private static GUIStyle _recordsChargeLabel = null!;
    private static GUIStyle _recordsCitLabel = null!;
    private static GUIStyle _recordsArrestLabel = null!;
    private static GUIStyle _sugStyle = null!;
    private static GUIStyle _subjectBtnStyle = null!;
    private static GUIStyle _removeStyle = null!;
    private static GUIStyle _npcStyle = null!;
    private static bool _stylesInit;

    private static Texture2D InitTex(Texture2D? tex)
    {
        if (tex != null) return tex;
        tex = new Texture2D(1, 1);
        tex.hideFlags = HideFlags.DontSave;
        return tex;
    }

    private static void CacheTextures(Color bg, Color accent)
    {
        if (_cachedBg == bg && _cachedAccent == accent && _texBg != null) return;
        _texBg = InitTex(_texBg);
        _texBorder = InitTex(_texBorder);
        _texLine = InitTex(_texLine);
        _texBg.SetPixel(0, 0, bg); _texBg.Apply();
        _texBorder.SetPixel(0, 0, accent); _texBorder.Apply();
        _texLine.SetPixel(0, 0, accent); _texLine.Apply();
        _cachedBg = bg;
        _cachedAccent = accent;
    }

    private static Color _cachedHighlightColor;
    private static bool _highlightDirty = true;

    private static Texture2D HighlightTex(Color accent)
    {
        _texHighlight = InitTex(_texHighlight);
        Color target = accent * 0.4f;
        if (!_highlightDirty && _cachedHighlightColor == target)
            return _texHighlight;
        _texHighlight.SetPixel(0, 0, target);
        _texHighlight.Apply();
        _cachedHighlightColor = target;
        _highlightDirty = false;
        return _texHighlight;
    }

    private static void EnsureStyles(Color textColor, Color accent)
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = textColor } };
        _boldLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = accent } };
        _catLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, fontSize = 11, normal = { textColor = textColor } };
        _inputField = new GUIStyle(GUI.skin.textField) { fontSize = 13, normal = { textColor = Color.white } };
        _chargeBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        _selectedBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _clockStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
        _tabBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleCenter, hover = { textColor = Color.white } };
        _statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter };
        _recordsBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, hover = { textColor = Color.white } };
        _recordsModifyBtn = new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold, hover = { textColor = Color.white } };
        _recordsChargeLabel = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        _recordsCitLabel = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        _recordsArrestLabel = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic };
        _sugStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleLeft, hover = { textColor = Color.white } };
        _subjectBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, hover = { textColor = Color.white } };
        _removeStyle = new GUIStyle(GUI.skin.button) { fontSize = 10, normal = { textColor = Color.red }, hover = { textColor = Color.white } };
        _npcStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleLeft, hover = { textColor = Color.white } };

        _chargeBtn.normal.textColor = textColor;
        _chargeBtn.hover.textColor = Color.white;
        _selectedBtn.normal.textColor = Color.white;
        _selectedBtn.hover.textColor = Color.white;
    }

    public MDTUI()
    {
        DetectRole();
        ApplyColors();
    }

    public void Cleanup()
    {
        foreach (var tex in _photoCache.Values)
            if (tex != null) UnityEngine.Object.Destroy(tex);
        _photoCache.Clear();
    }

    private Texture2D? GetOrLoadPhoto(string subjectName)
    {
        string? path = NPCDataStore.GetPhoto(subjectName);
        if (path == null || !File.Exists(path))
        {
            _photoCache.Remove(subjectName);
            return null;
        }

        if (_photoCache.TryGetValue(subjectName, out var cached) && cached != null)
        {
            if (File.GetLastWriteTimeUtc(path) != _lastPhotoWriteTimes.GetValueOrDefault(subjectName))
                _photoCache.Remove(subjectName);
            else
                return cached;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            tex.hideFlags = HideFlags.DontSave;
            if (tex.LoadImage(bytes))
            {
                _photoCache[subjectName] = tex;
                _lastPhotoWriteTimes[subjectName] = File.GetLastWriteTimeUtc(path);
                return tex;
            }
            UnityEngine.Object.Destroy(tex);
        }
        catch { }
        return null;
    }

    private readonly Dictionary<string, DateTime> _lastPhotoWriteTimes = new();

    public void Render()
    {
        if (_showModifyView)
        {
            RenderModifyView();
            return;
        }

        if (_photoBrowserVisible)
        {
            float bw = 480, bh = 540;
            var browserRect = new Rect((Screen.width - bw) / 2f, (Screen.height - bh) / 2f, bw, bh);
            string? result = _photoBrowser.Render(browserRect, _bgColor, _accentColor, _textColor);
            if (result != null)
            {
                _photoBrowserVisible = false;
                if (result.Length > 0)
                {
                    string fullName = _showModifyView ? $"{_modifyNewFirstName} {_modifyNewLastName}".Trim() : $"{_firstName} {_lastName}".Trim();
                    NPCDataStore.SetPhoto(fullName, result);
                    SetStatus($"Photo assigned to {fullName}");
                }
            }
            return;
        }

        if (Event.current.isKey && _activeTab == Tab.Filing)
        {
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode is KeyCode.Return or KeyCode.Tab)
                GUI.FocusControl(null);
        }

        _roleTimer += Time.unscaledDeltaTime;
        if (_roleTimer > 2f || _roleTimer == 0)
        {
            _roleTimer = 0;
            DetectRole();
            ApplyColors();
        }

        int margin = 40;
        var screenRect = new Rect(margin, margin, Screen.width - margin * 2, Screen.height - margin * 2);

        CacheTextures(_bgColor, _accentColor);
        EnsureStyles(_textColor, _accentColor);
        DrawBackground(screenRect);
        DrawHeader(screenRect);
        DrawTabs(screenRect);

        var contentRect = new Rect(
            screenRect.x + 10, screenRect.y + 80,
            screenRect.width - 20, screenRect.height - 100);

        GUI.BeginGroup(contentRect);
        _scrollPos = GUI.BeginScrollView(new Rect(0, 0, contentRect.width, contentRect.height),
            _scrollPos, new Rect(0, 0, contentRect.width - 20, 4000));

        switch (_activeTab)
        {
            case Tab.Filing: RenderFilingTab(contentRect.width - 20); break;
            case Tab.Court: RenderCourtTab(contentRect.width - 20); break;
            case Tab.Records: RenderRecordsTab(contentRect.width - 20); break;
        }

        GUI.EndScrollView();
        GUI.EndGroup();

        DrawStatus(screenRect);
    }

    private void DetectRole()
    {
        try
        {
            GameObject player;
            if (_cachedPlayer == null)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");
            player = _cachedPlayer;
            if (player != null)
            {
                var parent = player.transform.parent;
                while (parent != null)
                {
                    string n = parent.name ?? "";
                    if (n.Contains("Fire", StringComparison.OrdinalIgnoreCase) &&
                        n.Contains("Truck", StringComparison.OrdinalIgnoreCase))
                    { _currentRole = MDTRole.Fire; return; }
                    if (n.Contains("EMS", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("Ambulance", StringComparison.OrdinalIgnoreCase))
                    { _currentRole = MDTRole.EMS; return; }
                    if (n.Contains("Police", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("PoliceCar", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("Cruiser", StringComparison.OrdinalIgnoreCase))
                    { _currentRole = MDTRole.Police; return; }
                    parent = parent.parent;
                }
            }
        }
        catch { }
        _currentRole = MDTRole.Police;
    }

    private void ApplyColors()
    {
        _highlightDirty = true;
        switch (_currentRole)
        {
            case MDTRole.Police:
                _bgColor = new Color(0.06f, 0.06f, 0.18f);
                _borderColor = new Color(0.10f, 0.10f, 0.28f);
                _accentColor = new Color(0.20f, 0.35f, 0.80f);
                _textColor = new Color(0.70f, 0.80f, 1.0f);
                break;
            case MDTRole.Fire:
                _bgColor = new Color(0.18f, 0.06f, 0.06f);
                _borderColor = new Color(0.28f, 0.10f, 0.10f);
                _accentColor = new Color(0.80f, 0.25f, 0.15f);
                _textColor = new Color(1.0f, 0.75f, 0.70f);
                break;
            case MDTRole.EMS:
                _bgColor = new Color(0.06f, 0.18f, 0.06f);
                _borderColor = new Color(0.10f, 0.28f, 0.10f);
                _accentColor = new Color(0.20f, 0.75f, 0.35f);
                _textColor = new Color(0.70f, 1.0f, 0.75f);
                break;
        }
    }

    private void DrawBackground(Rect rect)
    {
        GUI.DrawTexture(rect, _texBg, ScaleMode.StretchToFill);
        float bw = 2;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bw), _texBorder);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - bw, rect.width, bw), _texBorder);
        GUI.DrawTexture(new Rect(rect.x, rect.y, bw, rect.height), _texBorder);
        GUI.DrawTexture(new Rect(rect.x + rect.width - bw, rect.y, bw, rect.height), _texBorder);
    }

    private void DrawHeader(Rect rect)
    {
        _headerStyle.normal.textColor = _accentColor;
        GUI.Label(new Rect(rect.x, rect.y + 8, rect.width, 30),
            $"=== {RoleNames[(int)_currentRole]} MOBILE DATA TERMINAL ===", _headerStyle);

        _clockStyle.normal.textColor = _textColor;
        GUI.Label(new Rect(rect.x + rect.width - 160, rect.y + 8, 150, 20),
            DateTime.Now.ToString("HH:mm:ss"), _clockStyle);

        MDTMod.Instance.DrawToggleKeyBind(new Rect(rect.x + 20, rect.y + 10, 150, 22));

        GUI.DrawTexture(new Rect(rect.x + 20, rect.y + 48, rect.width - 40, 1), _texLine);
    }

    private void DrawTabs(Rect rect)
    {
        float startX = rect.x + (rect.width - TabNames.Length * TabW) / 2f;
        Color highlight = _accentColor * 0.6f;
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool isActive = (int)_activeTab == i;
            _tabBtn.fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal;
            _tabBtn.normal.textColor = isActive ? Color.white : _textColor;
            _tabBtn.normal.background = isActive ? HighlightTex(highlight) : null;
            if (GUI.Button(new Rect(startX + i * TabW, rect.y + 52, TabW - 4, 24), TabNames[i], _tabBtn))
            {
                if (_activeTab != (Tab)i)
                {
                    _scrollPos = Vector2.zero;
                    _chargeSubjectSelected = false;
                    _firstName = "";
                    _lastName = "";
                    _selectedChargeIndex = -1;
                    _selectedCitationIndex = -1;
                    _chargeSearch = "";
                    _showNewRecordForm = false;
                    ClearArrestState();
                }
                _activeTab = (Tab)i;
            }
        }
    }

    private void RenderFilingTab(float width)
    {
        _labelStyle.normal.textColor = _textColor;
        GUI.Label(new Rect(0, 5, 70, 22), "Type:", _labelStyle);
        _filingModeIndex = GUI.SelectionGrid(new Rect(75, 5, 300, 24), _filingModeIndex, FilingModes, 2);

        GUI.DrawTexture(new Rect(0, 36, width, 1), _texLine);
        GUI.BeginGroup(new Rect(0, 44, width, 3900));
        if (_filingModeIndex == 0)
            RenderChargesTab(width);
        else
            RenderCitationsTab(width);
        GUI.EndGroup();
    }

    private void RenderChargesTab(float width)
    {
        if (_showArrestNarrative)
        {
            RenderArrestNarrative(width);
            return;
        }

        if (!_chargeSubjectSelected)
        {
            _labelStyle.normal.textColor = _textColor;
            GUI.Label(new Rect(0, 5, 80, 22), "First Name:", _labelStyle);
            _firstName = GUI.TextField(new Rect(85, 5, 160, 22), _firstName, _inputField);

            GUI.Label(new Rect(255, 5, 80, 22), "Last Name:", _labelStyle);
            _lastName = GUI.TextField(new Rect(340, 5, 160, 22), _lastName, _inputField);

            var fnSug = NameListLoader.MatchFirstNames(_firstName);
            var lnSug = NameListLoader.MatchLastNames(_lastName);
            float sugStart = 30;
            float sugCount = Math.Max(fnSug.Count, lnSug.Count);

            _sugStyle.normal.textColor = _textColor;
            _sugStyle.normal.background = HighlightTex(_accentColor * 0.3f);
            for (int i = 0; i < fnSug.Count; i++)
            {
                if (GUI.Button(new Rect(85, sugStart + i * 19, 160, 18), fnSug[i], _sugStyle))
                {
                    _firstName = fnSug[i];
                    GUI.FocusControl(null);
                }
            }
            for (int i = 0; i < lnSug.Count; i++)
            {
                if (GUI.Button(new Rect(340, sugStart + i * 19, 160, 18), lnSug[i], _sugStyle))
                {
                    _lastName = lnSug[i];
                    GUI.FocusControl(null);
                }
            }

            float btnY = sugStart + sugCount * 19 + 6;
            if (GUI.Button(new Rect(10, btnY, 100, 24), "SEARCH") && (!string.IsNullOrWhiteSpace(_firstName) || !string.IsNullOrWhiteSpace(_lastName)))
            {
                _subjectName = $"{_firstName} {_lastName}".Trim();
                _chargeSubjectSelected = true;
                GUI.FocusControl(null);
                return;
            }

            _boldLabel.normal.textColor = _accentColor;
            GUI.Label(new Rect(0, btnY + 30, width, 22), "EXISTING SUBJECTS:", _boldLabel);

            float y = btnY + 56;
            var subjects = NPCDataStore.GetSubjectNames();
            if (subjects.Length == 0)
            {
                _labelStyle.normal.textColor = _textColor;
                GUI.Label(new Rect(10, y, width, 20), "No subjects on file.", _labelStyle);
            }
            else
            {
            _subjectBtnStyle.normal.textColor = _textColor;
                foreach (var name in subjects)
                {
                    if (GUI.Button(new Rect(10, y, width - 20, 22), name, _subjectBtnStyle))
                    {
                        var parts = name.Split(' ');
                        _firstName = parts.Length > 0 ? parts[0] : "";
                        _lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                        _subjectName = name;
                        _chargeSubjectSelected = true;
                        GUI.FocusControl(null);
                        return;
                    }
                    y += 24;
                }
            }
            return;
        }

        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, 5, width - 240, 22), $"FILING AGAINST: {_firstName} {_lastName}", _boldLabel);

        if (GUI.Button(new Rect(width - 240, 5, 110, 22), "PHOTO"))
        {
            _photoBrowser.Load();
            _photoBrowserVisible = true;
        }

        if (GUI.Button(new Rect(width - 120, 5, 110, 22), "CHANGE"))
        {
            ClearChargeState();
            return;
        }

        _labelStyle.normal.textColor = _textColor;
        GUI.Label(new Rect(0, 30, 90, 22), "Officer:", _labelStyle);
        _officerName = GUI.TextField(new Rect(95, 30, 200, 22), _officerName, _inputField);

        if (_pendingCharges.Count > 0)
        {
            GUI.Label(new Rect(310, 30, width - 310, 22),
                $"Pending charges: {_pendingCharges.Count}", _labelStyle);
        }

        float yPos = 60;
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, yPos, width, 22), "SELECT CHARGE:", _boldLabel);
        yPos += 28;

        GUI.Label(new Rect(10, yPos, 70, 22), "Search:", _labelStyle);
        _chargeSearch = GUI.TextField(new Rect(85, yPos, 260, 22), _chargeSearch, _inputField);
        if (GUI.Button(new Rect(355, yPos, 90, 22), _showChargeDropdown ? "HIDE" : "SHOW"))
            _showChargeDropdown = !_showChargeDropdown;
        yPos += 28;

        if (_showChargeDropdown)
        {
            var matches = FilterCharges(_chargeSearch, _filingModeIndex == 1).Take(18).ToList();
            foreach (var ch in matches)
            {
                string line = ChargeLine(ch);
                int idx = USCharges.All.IndexOf(ch);
                bool selected = idx == _selectedChargeIndex;
                var style = selected ? _selectedBtn : _chargeBtn;
                style.normal.textColor = selected ? Color.white : _textColor;
                style.normal.background = selected ? HighlightTex(_accentColor) : null;

                if (GUI.Button(new Rect(10, yPos, width - 20, 22), line, style))
                    _selectedChargeIndex = idx;
                yPos += 24;
            }
        }

        yPos += 10;

        if (GUI.Button(new Rect(10, yPos, 160, 28), "ADD CHARGE",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white, background = HighlightTex(Color.cyan * 0.5f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            if (_selectedChargeIndex < 0 || _selectedChargeIndex >= USCharges.All.Count)
                SetStatus("Select a charge first.");
            else
            {
                var charge = USCharges.All[_selectedChargeIndex];
                if (!_pendingCharges.Contains(charge))
                {
                    _pendingCharges.Add(charge);
                    SetStatus($"Added: {charge.Name}");
                }
                else
                    SetStatus("Charge already added.");
            }
        }

        if (_pendingCharges.Count > 0)
        {
            if (GUI.Button(new Rect(190, yPos, 180, 28), "CONFIRM ARREST",
                    new GUIStyle(GUI.skin.button)
                    {
                        fontSize = 13, fontStyle = FontStyle.Bold,
                        normal = { textColor = Color.white, background = HighlightTex(Color.green * 0.5f) },
                        hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                    }))
            {
                _scrollPos = Vector2.zero;
                _locationZip = "";
                _locationStreet = "";
                _arrestNature = "";
                _arrestDOB = "";
                _arrestLicense = "";
                _arrestWitnesses.Clear();
                _witnessName = "";
                _witnessStatement = "";
                _showArrestNarrative = true;
            }
        }

        yPos += 35;

        if (_pendingCharges.Count > 0)
        {
            _boldLabel.normal.textColor = _accentColor;
            GUI.Label(new Rect(0, yPos, width, 22), "PENDING CHARGES:", _boldLabel);
            yPos += 28;

            for (int i = 0; i < _pendingCharges.Count; i++)
            {
                var pc = _pendingCharges[i];
                string cls = pc.Class switch
                {
                    ChargeClass.Infraction => "INF",
                    ChargeClass.Misdemeanor => "MIS",
                    ChargeClass.Felony => "FEL",
                    _ => ""
                };
                string line = $"  {i + 1}. [{cls}] {pc.Name}";
                GUI.Label(new Rect(10, yPos, width - 100, 18), line, _labelStyle);
                if (GUI.Button(new Rect(width - 90, yPos, 80, 18), "REMOVE", _removeStyle))
                {
                    _pendingCharges.RemoveAt(i);
                    SetStatus($"Removed: {pc.Name}");
                }
                yPos += 22;
            }
        }
    }

    private void RenderArrestNarrative(float width)
    {
        float y = 5;
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, y, width, 22), $"ARREST NARRATIVE — {_firstName} {_lastName}", _boldLabel);
        y += 30;

        _labelStyle.normal.textColor = _textColor;

        GUI.Label(new Rect(0, y, 100, 22), "Officer:", _labelStyle);
        _officerName = GUI.TextField(new Rect(105, y, 250, 22), _officerName, _inputField);
        y += 28;

        GUI.Label(new Rect(0, y, 100, 22), "ZIP:", _labelStyle);
        _locationZip = FormatZip(GUI.TextField(new Rect(105, y, 90, 22), _locationZip, 5, _inputField));
        GUI.Label(new Rect(215, y, 90, 22), "Street:", _labelStyle);
        _locationStreet = GUI.TextField(new Rect(300, y, 180, 22), _locationStreet, _inputField);
        DrawStreetSuggestions(300, y + 24, 180);
        y += 92;

        GUI.Label(new Rect(0, y, 100, 22), "DOB:", _labelStyle);
        _arrestDOB = GUI.TextField(new Rect(105, y, 200, 22), _arrestDOB, _inputField);
        y += 28;

        GUI.Label(new Rect(0, y, 100, 22), "License Plate:", _labelStyle);
        _arrestLicense = FormatPlate(GUI.TextField(new Rect(105, y, 110, 22), _arrestLicense, 7, _inputField));
        y += 28;

        GUI.Label(new Rect(0, y, width, 22), "Nature of Arrest:", _labelStyle);
        y += 22;
        _arrestNature = GUI.TextArea(new Rect(0, y, width - 10, 60), _arrestNature, _inputField);
        y += 66;

        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, y, width, 22), "WITNESSES:", _boldLabel);
        y += 28;

        GUI.Label(new Rect(0, y, 50, 22), "Name:", _labelStyle);
        _witnessName = GUI.TextField(new Rect(55, y, 160, 22), _witnessName, _inputField);

        GUI.Label(new Rect(225, y, 70, 22), "Statement:", _labelStyle);
        _witnessStatement = GUI.TextField(new Rect(300, y, width - 410, 22), _witnessStatement, _inputField);

        if (GUI.Button(new Rect(width - 90, y, 80, 22), "ADD",
                new GUIStyle(GUI.skin.button) { fontSize = 11, normal = { textColor = Color.green }, hover = { textColor = Color.white } }))
        {
            if (!string.IsNullOrWhiteSpace(_witnessName))
            {
                _arrestWitnesses.Add(new WitnessStatement(_witnessName.Trim(), _witnessStatement.Trim()));
                SetStatus($"Witness added: {_witnessName}");
                _witnessName = "";
                _witnessStatement = "";
            }
            else
                SetStatus("Enter a witness name.");
        }
        y += 28;

            for (int i = 0; i < _arrestWitnesses.Count; i++)
            {
                var w = _arrestWitnesses[i];
                string line = $"  {w.Name} — {w.Statement}";
                GUI.Label(new Rect(10, y, width - 100, 18), line, _labelStyle);
                if (GUI.Button(new Rect(width - 90, y, 80, 18), "REMOVE", _removeStyle))
            {
                _arrestWitnesses.RemoveAt(i);
                SetStatus("Witness removed.");
            }
            y += 22;
        }

        y += 5;
        _labelStyle.normal.textColor = _textColor;
        GUI.Label(new Rect(0, y, width, 22), $"Total charges to file: {_pendingCharges.Count}", _labelStyle);
        y += 30;

        if (GUI.Button(new Rect(10, y, 200, 30), "CONFIRM & SAVE",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white, background = HighlightTex(Color.green * 0.6f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            if (_pendingCharges.Count == 0)
            {
                SetStatus("No charges to file.");
                return;
            }

            string fullName = $"{_firstName} {_lastName}".Trim();
            string? subjectRegistration = ResolveSubjectRegistration(fullName, _arrestLicense);
            var filedChargeIds = new List<string>();

            foreach (var charge in _pendingCharges)
            {
                var nc = NPCDataStore.FileCharge(fullName, charge, _officerName, subjectRegistration);
                filedChargeIds.Add(nc.Id);
            }

            var arrest = new ArrestRecord(
                "",
                fullName,
                DateTime.Now,
                BuildLocation(),
                _arrestNature,
                _arrestDOB,
                _arrestLicense,
                new List<WitnessStatement>(_arrestWitnesses),
                _officerName,
                filedChargeIds,
                subjectRegistration
            );
            NPCDataStore.FileArrest(arrest);

            SetStatus($"Arrest filed for {fullName} with {_pendingCharges.Count} charge(s).");
            ClearChargeState();
        }

        if (GUI.Button(new Rect(230, y, 120, 30), "BACK",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    normal = { textColor = _textColor, background = HighlightTex(_accentColor * 0.4f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            _showArrestNarrative = false;
        }
    }

    private void ClearArrestState()
    {
        _pendingCharges.Clear();
        _showArrestNarrative = false;
        _locationZip = "";
        _locationStreet = "";
        _arrestNature = "";
        _arrestDOB = "";
        _arrestLicense = "";
        _arrestWitnesses.Clear();
        _witnessName = "";
        _witnessStatement = "";
    }

    private void ClearChargeState()
    {
        _chargeSubjectSelected = false;
        _pendingCharges.Clear();
        _selectedChargeIndex = -1;
        _firstName = "";
        _lastName = "";
        _showArrestNarrative = false;
        _locationZip = "";
        _locationStreet = "";
        _arrestNature = "";
        _arrestDOB = "";
        _arrestLicense = "";
        _arrestWitnesses.Clear();
        _witnessName = "";
        _witnessStatement = "";
    }

    private static string? ResolveSubjectRegistration(string fullName, string? fallbackRegistration = null)
    {
        if (!string.IsNullOrWhiteSpace(fallbackRegistration))
            return fallbackRegistration.Trim();

        var latestNpc = NPCDataStore.FindLatestNpcRecord(fullName);
        if (latestNpc == null || string.IsNullOrWhiteSpace(latestNpc.Registration))
            return null;

        return latestNpc.Registration.Trim();
    }

    private void RenderCitationsTab(float width)
    {
        _labelStyle.normal.textColor = _textColor;
        DrawNameInputs(0, 5, ref _firstName, ref _lastName);

        GUI.Label(new Rect(0, 98, 120, 22), "Your Name:", _labelStyle);
        _officerName = GUI.TextField(new Rect(130, 98, 250, 22), _officerName, _inputField);

        float y = 132;
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, y, width, 22), "SEARCH CITATION:", _boldLabel);
        y += 28;

        GUI.Label(new Rect(10, y, 70, 22), "Search:", _labelStyle);
        _chargeSearch = GUI.TextField(new Rect(85, y, 260, 22), _chargeSearch, _inputField);
        if (GUI.Button(new Rect(355, y, 90, 22), _showChargeDropdown ? "HIDE" : "SHOW"))
            _showChargeDropdown = !_showChargeDropdown;
        y += 28;

        if (_showChargeDropdown)
        {
            foreach (var cit in FilterCharges(_chargeSearch, citationOnly: true).Take(18))
            {
                string line = ChargeLine(cit);

                int idx = USCharges.All.IndexOf(cit);
                bool selected = idx == _selectedCitationIndex;
                var style = selected ? _selectedBtn : _chargeBtn;
                style.normal.textColor = selected ? Color.white : _textColor;
                style.normal.background = selected ? HighlightTex(_accentColor) : null;

                if (GUI.Button(new Rect(10, y, width - 20, 22), line, style))
                    _selectedCitationIndex = idx;
                y += 24;
            }
        }

        y += 10;
        if (GUI.Button(new Rect(10, y, 200, 28), "ISSUE CITATION",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white, background = HighlightTex(Color.yellow * 0.5f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            if (string.IsNullOrWhiteSpace(_firstName) && string.IsNullOrWhiteSpace(_lastName))
                SetStatus("Enter a subject name.");
            else if (_selectedCitationIndex < 0 || _selectedCitationIndex >= USCharges.All.Count)
                SetStatus("Select a citation.");
            else
            {
                string fullName = $"{_firstName} {_lastName}".Trim();
                var charge = USCharges.All[_selectedCitationIndex];
                string? subjectRegistration = ResolveSubjectRegistration(fullName);
                NPCDataStore.IssueCitation(fullName, charge, _officerName, subjectRegistration);
                SetStatus($"Citation issued: {charge.Name} to {fullName}");
            }
        }
    }

    private void RenderCourtTab(float width)
    {
        var pending = NPCDataStore.GetPendingCharges();
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, 5, width, 22), $"PENDING CHARGES: {pending.Count}", _boldLabel);

        _labelStyle.normal.textColor = _textColor;
        if (pending.Count == 0)
        {
            GUI.Label(new Rect(10, 35, width, 20), "No pending charges awaiting court.", _labelStyle);
        }
        else
        {
            float y = 35;
            foreach (var p in pending)
            {
                string line = $"{p.SubjectName,-20} {p.Charge.Name,-40} [{p.Charge.Class}]  Filed: {p.FiledAt:MM/dd HH:mm}";
                GUI.Label(new Rect(10, y, width - 20, 18), line, _labelStyle);
                y += 20;
            }
        }

        float y2 = pending.Count == 0 ? 65 : 35 + pending.Count * 20 + 20;
        var ruled = NPCDataStore.GetCharges().Where(c => c.Verdict != null).ToList();
        GUI.Label(new Rect(0, y2, width, 22), "VERDICTS:", _boldLabel);
        y2 += 28;

        if (ruled.Count == 0)
        {
            GUI.Label(new Rect(10, y2, width, 20), "No verdicts yet.", _labelStyle);
        }
        else
        {
            int start2 = Math.Max(0, ruled.Count - 20);
            for (int i = ruled.Count - 1; i >= start2; i--)
            {
                var r = ruled[i];
                var v = r.Verdict!;
                Color c = v.Outcome switch
                {
                    "Guilty" => Color.red,
                    "Not Guilty" => Color.green,
                    "Plea Bargain" => Color.yellow,
                    _ => _textColor
                };
                string line = $"{r.SubjectName,-18} {r.Charge.Name,-38} -> {v.Outcome,-12}" +
                              (v.Fine > 0 ? $" ${v.Fine}" : "") +
                              (v.JailDays > 0 ? $" {v.JailDays}d" : "");
                _recordsChargeLabel.normal.textColor = c;
                GUI.Label(new Rect(10, y2, width - 20, 18), line, _recordsChargeLabel);
                y2 += 20;
            }

            _recordsArrestLabel.normal.textColor = _textColor;
            GUI.Label(new Rect(10, y2 + 5, width, 18),
                $"Auto-court runs every {MDTMod.Instance.CourtIntervalMinutesConfig} min (config.txt overrides).",
                _recordsArrestLabel);
        }
    }

    private void RenderRecordsTab(float width)
    {
        int version = NPCDataStore.GetDataVersion();
        if (version != _lastDataVersion || _cachedSubjects.Length == 0)
        {
            _lastDataVersion = version;
            _cachedSubjects = NPCDataStore.GetSubjectNames();
        }

        var npcRecords = NPCDataStore.GetNpcRecords();
        bool hasNpcRecords = npcRecords.Count > 0;

        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(0, 5, width, 22), "SEARCH RECORDS:", _boldLabel);

        float y = 35;
        _labelStyle.normal.textColor = _textColor;

        if (GUI.Button(new Rect(10, y, 160, 26), _showNewRecordForm ? "CANCEL NEW RECORD" : "MAKE NEW RECORD"))
        {
            _showNewRecordForm = !_showNewRecordForm;
            if (_showNewRecordForm)
                SeedNewRecordForm();
        }
        y += 34;

        if (_showNewRecordForm)
        {
            y = RenderNewRecordForm(width, y);
            GUI.DrawTexture(new Rect(0, y, width, 1), _texLine);
            y += 10;
        }

        // NPC Info section at top
        if (hasNpcRecords)
        {
            _catLabel.normal.textColor = _accentColor;
            GUI.Label(new Rect(0, y, width, 18), $"-- CITIZEN DATABASE ({npcRecords.Count} records) --", _catLabel);
            y += 22;

            float npcY = y + 26;
            int shown = 0;
            int start = Math.Max(0, npcRecords.Count - 15);

            for (int i = npcRecords.Count - 1; i >= start; i--)
            {
                var npc = npcRecords[i];
                string status = "";
                if (npc.IsWanted) status += " [WANTED]";
                if (npc.IsMissing) status += " [MISSING]";

                string line = $"{npc.Name} | Plate: {DisplayPlate(npc)} | Reg: {npc.RegistrationStatus} | Lic: {npc.LicenseStatus} | Weapon: {(npc.HasWeaponLicense || npc.HasFirearmsLicense ? "Y" : "N")}{status}";

                Color itemColor = npc.IsWanted ? Color.red : npc.IsMissing ? Color.yellow : _textColor;
                _npcStyle.normal.textColor = itemColor;
                if (GUI.Button(new Rect(10, npcY, width - 20, 20), line, _npcStyle))
                {
                    var parts = npc.Name.Split(' ');
                    _firstName = parts.Length > 0 ? parts[0] : "";
                    _lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                    SetStatus($"Selected {npc.Name}");
                }
                npcY += 22;
                shown++;
                if (shown >= 15) break;
            }

            if (npcRecords.Count > 15)
            {
                _labelStyle.normal.textColor = _textColor;
                GUI.Label(new Rect(10, npcY, width, 16), $"... and {npcRecords.Count - 15} more", _labelStyle);
                npcY += 20;
            }

            y = npcY + 10;
            GUI.DrawTexture(new Rect(0, y - 5, width, 1), _texLine);
        }

        if (_cachedSubjects.Length == 0)
        {
            GUI.Label(new Rect(10, y, width, 20), "No records on file.", _labelStyle);
            return;
        }

        _recordsBtn.normal.textColor = _textColor;
        _recordsChargeLabel.normal.textColor = _textColor;
        _recordsCitLabel.normal.textColor = _textColor;
        _recordsModifyBtn.normal.textColor = _accentColor;

        foreach (var name in _cachedSubjects)
        {
            var charges = NPCDataStore.GetChargesForSubject(name);
            var citations = NPCDataStore.GetCitationsForSubject(name);
            var arrests = NPCDataStore.GetArrestsForSubject(name);
            var npcInfo = NPCDataStore.FindNpcRecord(name);

            string summary;
            Color nameColor = _textColor;
            if (charges.Count > 0 || citations.Count > 0 || arrests.Count > 0)
            {
                summary = $"{name,-22} Charges: {charges.Count}  Citations: {citations.Count}  Arrests: {arrests.Count}";
            }
            else if (npcInfo != null)
            {
                string flags = "";
                if (npcInfo.IsWanted) flags += " [WANTED]";
                if (npcInfo.IsMissing) flags += " [MISSING]";
                summary = $"{name} | Plate: {DisplayPlate(npcInfo)} | Reg: {npcInfo.RegistrationStatus} | Lic: {npcInfo.LicenseStatus} | Weapon: {(npcInfo.HasWeaponLicense || npcInfo.HasFirearmsLicense ? "Y" : "N")}{flags}";
                if (npcInfo.IsWanted) nameColor = Color.red;
                else if (npcInfo.IsMissing) nameColor = Color.yellow;
            }
            else
            {
                summary = $"{name,-22} No records on file.";
            }

            _recordsBtn.normal.textColor = nameColor;

            if (GUI.Button(new Rect(10, y, width - 100, 22), summary, _recordsBtn))
            {
                _scrollPos = Vector2.zero;
                var parts = name.Split(' ');
                _firstName = parts.Length > 0 ? parts[0] : "";
                _lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                _subjectName = name;
                _chargeSubjectSelected = true;
                _activeTab = Tab.Filing;
                _filingModeIndex = 0;
                SetStatus($"Viewing records for {name}");
            }

            if (GUI.Button(new Rect(width - 80, y, 70, 22), "MODIFY", _recordsModifyBtn))
            {
                var parts = name.Split(' ');
                _modifySubjectName = name;
                _modifyNewFirstName = parts.Length > 0 ? parts[0] : "";
                _modifyNewLastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                _showModifyView = true;
                SetStatus($"Editing record for {name}");
            }
            y += 26;

            Texture2D? photoTex = GetOrLoadPhoto(name);
            float photoRightX = 0;
            if (photoTex != null)
            {
                float pw = 55, ph = 69;
                GUI.DrawTexture(new Rect(20, y, pw, ph), photoTex, ScaleMode.ScaleToFit);
                float labelX = 20 + pw + 6;
                photoRightX = labelX;
                y += ph + 4;
            }
            else
                photoRightX = 30;

            foreach (var ch in charges)
            {
                string v = ch.Verdict?.Outcome ?? "PENDING";
                Color c = ch.Verdict?.Outcome switch
                {
                    "Guilty" => Color.red,
                    "Not Guilty" => Color.green,
                    "Plea Bargain" => Color.yellow,
                    _ => Color.gray
                };
                _recordsChargeLabel.normal.textColor = c;
                GUI.Label(new Rect(photoRightX, y, width - photoRightX - 10, 16),
                    $"  * {ch.Charge.Name,-38} [{v}]", _recordsChargeLabel);
                y += 18;
            }

            foreach (var cit in citations)
            {
                GUI.Label(new Rect(photoRightX, y, width - photoRightX - 10, 16),
                    $"  # {cit.Charge.Name,-38} ${cit.Charge.FineMin}-${cit.Charge.FineMax}", _recordsCitLabel);
                y += 18;
            }

            foreach (var a in arrests)
            {
                _recordsArrestLabel.normal.textColor = _accentColor;
                GUI.Label(new Rect(photoRightX, y, width - photoRightX - 10, 16),
                    $"  ARREST: {a.ArrestedAt:MM/dd/yyyy HH:mm} at {(string.IsNullOrEmpty(a.Location) ? "N/A" : a.Location)}", _recordsArrestLabel);
                y += 16;
                if (!string.IsNullOrEmpty(a.NatureOfArrest))
                {
                    GUI.Label(new Rect(photoRightX + 10, y, width - photoRightX - 20, 16),
                        $"  Nature: {a.NatureOfArrest}", _recordsCitLabel);
                    y += 16;
                }
                if (!string.IsNullOrEmpty(a.DOB))
                {
                    GUI.Label(new Rect(photoRightX + 10, y, width - photoRightX - 20, 16),
                        $"  DOB: {a.DOB}", _recordsCitLabel);
                    y += 16;
                }
                if (!string.IsNullOrEmpty(a.LicensePlate))
                {
                    GUI.Label(new Rect(photoRightX + 10, y, width - photoRightX - 20, 16),
                        $"  Plate: {a.LicensePlate}", _recordsCitLabel);
                    y += 16;
                }
                if (a.Witnesses.Count > 0)
                {
                    GUI.Label(new Rect(photoRightX + 10, y, width - photoRightX - 20, 16),
                        $"  Witnesses: {string.Join(", ", a.Witnesses.Select(w => w.Name))}", _recordsCitLabel);
                    y += 16;
                }
                y += 4;
            }

            y += 4;
        }
    }

    private float RenderNewRecordForm(float width, float y)
    {
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(10, y, width - 20, 22), "NEW CITIZEN RECORD", _boldLabel);
        y += 28;

        DrawNameInputs(10, y, ref _recordFirstName, ref _recordLastName);
        y += 92;

        GUI.Label(new Rect(10, y, 130, 22), "Registration:", _labelStyle);
        _recordRegistrationStatus = DrawOptionRow(new Rect(145, y, 360, 22), _recordRegistrationStatus, RegistrationStatuses);
        y += 28;

        GUI.Label(new Rect(10, y, 130, 22), "Wanted:", _labelStyle);
        _recordWantedStatus = DrawOptionRow(new Rect(145, y, 300, 22), _recordWantedStatus, WantedStatuses);
        y += 28;

        GUI.Label(new Rect(10, y, 130, 22), "Driver License:", _labelStyle);
        _recordLicenseStatus = DrawOptionRow(new Rect(145, y, 360, 22), _recordLicenseStatus, LicenseStatuses);
        y += 28;

        GUI.Label(new Rect(10, y, 130, 22), "License Plate:", _labelStyle);
        _recordLicensePlate = FormatPlate(GUI.TextField(new Rect(145, y, 110, 22), _recordLicensePlate, 7, _inputField));
        _recordWeaponLicense = GUI.Toggle(new Rect(275, y, 180, 22), _recordWeaponLicense, "Weapon License");
        y += 34;

        if (GUI.Button(new Rect(10, y, 150, 28), "SAVE RECORD",
                new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = HighlightTex(Color.green * 0.6f) }, hover = { textColor = Color.white } }))
        {
            string fullName = BuildName(_recordFirstName, _recordLastName);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                SetStatus("Enter first and last name.");
            }
            else
            {
                bool wanted = _recordWantedStatus.Equals("Wanted", StringComparison.OrdinalIgnoreCase);
                bool missing = _recordWantedStatus.Equals("Missing", StringComparison.OrdinalIgnoreCase);
                NPCDataStore.AddNpcRecord(new NPCInfo(
                    fullName,
                    _recordLicensePlate,
                    !_recordRegistrationStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase),
                    _recordWeaponLicense,
                    wanted,
                    missing,
                    DateTime.Now,
                    _recordRegistrationStatus,
                    _recordLicenseStatus,
                    _recordLicensePlate,
                    _recordWeaponLicense));
                _lastDataVersion = -1;
                _showNewRecordForm = false;
                SetStatus($"Record saved for {fullName}");
            }
        }

        return y + 42;
    }

    private void SeedNewRecordForm()
    {
        _recordFirstName = _firstName;
        _recordLastName = _lastName;
        _recordRegistrationStatus = "Valid";
        _recordWantedStatus = "Clear";
        _recordLicenseStatus = "Valid";
        _recordLicensePlate = "";
        _recordWeaponLicense = false;
    }

    private string DrawOptionRow(Rect rect, string current, string[] options)
    {
        int selected = Math.Max(0, Array.IndexOf(options, current));
        selected = GUI.SelectionGrid(rect, selected, options, options.Length);
        return options[Math.Clamp(selected, 0, options.Length - 1)];
    }

    private static string DisplayPlate(NPCInfo npc) =>
        !string.IsNullOrWhiteSpace(npc.LicensePlate) ? npc.LicensePlate : npc.Registration;

    private void DrawNameInputs(float x, float y, ref string first, ref string last)
    {
        GUI.Label(new Rect(x, y, 80, 22), "First:", _labelStyle);
        first = GUI.TextField(new Rect(x + 85, y, 160, 22), first, _inputField);
        GUI.Label(new Rect(x + 255, y, 70, 22), "Last:", _labelStyle);
        last = GUI.TextField(new Rect(x + 325, y, 160, 22), last, _inputField);

        var fnSug = NameListLoader.MatchFirstNames(first);
        var lnSug = NameListLoader.MatchLastNames(last);
        _sugStyle.normal.textColor = _textColor;
        _sugStyle.normal.background = HighlightTex(_accentColor * 0.3f);
        for (int i = 0; i < fnSug.Count && i < 3; i++)
            if (GUI.Button(new Rect(x + 85, y + 25 + i * 19, 160, 18), fnSug[i], _sugStyle))
                first = fnSug[i];
        for (int i = 0; i < lnSug.Count && i < 3; i++)
            if (GUI.Button(new Rect(x + 325, y + 25 + i * 19, 160, 18), lnSug[i], _sugStyle))
                last = lnSug[i];
    }

    private static string BuildName(string first, string last) =>
        $"{first.Trim()} {last.Trim()}".Trim();

    private static IEnumerable<USCharge> FilterCharges(string query, bool citationOnly)
    {
        IEnumerable<USCharge> source = USCharges.All;
        if (citationOnly)
            source = source.Where(c => c.Category is ChargeCategory.Traffic or ChargeCategory.Registration);

        if (string.IsNullOrWhiteSpace(query))
            return source;

        query = query.Trim();
        return source.Where(c =>
            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Statute.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Category.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string ChargeLine(USCharge charge)
    {
        string cls = charge.Class switch
        {
            ChargeClass.Infraction => "INF",
            ChargeClass.Misdemeanor => "MIS",
            ChargeClass.Felony => "FEL",
            _ => ""
        };
        string line = $"{charge.Name} [{cls}] ${charge.FineMin}-${charge.FineMax} ({charge.Statute})";
        if (charge.JailDaysMax > 0)
            line += $" {charge.JailDaysMin}-{charge.JailDaysMax}d";
        return line;
    }

    private static string FormatPlate(string value)
    {
        string digits = new string((value ?? "").Where(char.IsDigit).Take(6).ToArray());
        return digits.Length > 3 ? $"{digits[..3]}-{digits[3..]}" : digits;
    }

    private static string FormatZip(string value)
    {
        string digits = new string((value ?? "").Where(char.IsDigit).Take(4).ToArray());
        return digits.Length > 2 ? $"{digits[..2]}-{digits[2..]}" : digits;
    }

    private string BuildLocation()
    {
        string zip = _locationZip.Trim();
        string street = _locationStreet.Trim();
        if (string.IsNullOrWhiteSpace(zip)) return street;
        if (string.IsNullOrWhiteSpace(street)) return zip;
        return $"{zip} {street}";
    }

    private void DrawStreetSuggestions(float x, float y, float w)
    {
        if (string.IsNullOrWhiteSpace(_locationStreet)) return;
        var matches = StreetOptions
            .Where(s => s.Contains(_locationStreet, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        for (int i = 0; i < matches.Count; i++)
        {
            if (GUI.Button(new Rect(x, y + i * 19, w, 18), matches[i], _sugStyle))
            {
                _locationStreet = matches[i];
                GUI.FocusControl(null);
            }
        }
    }

    private void RenderModifyView()
    {
        int margin = 40;
        var screenRect = new Rect(margin, margin, Screen.width - margin * 2, Screen.height - margin * 2);

        CacheTextures(_bgColor, _accentColor);
        EnsureStyles(_textColor, _accentColor);
        DrawBackground(screenRect);
        DrawHeader(screenRect);

        float x = screenRect.x + 20;
        float y = screenRect.y + 60;
        float w = screenRect.width - 40;

        _headerStyle.normal.textColor = _accentColor;
        GUI.Label(new Rect(x, y, w, 26), $"MODIFY RECORD — {_modifySubjectName}", _headerStyle);
        y += 32;

        _labelStyle.normal.textColor = _textColor;
        DrawNameInputs(x, y, ref _modifyNewFirstName, ref _modifyNewLastName);

        if (GUI.Button(new Rect(x + 520, y, 100, 22), "PHOTO"))
        {
            _photoBrowser.Load();
            _photoBrowserVisible = true;
        }
        y += 96;

        Texture2D? photoTex = GetOrLoadPhoto(_modifySubjectName);
        if (photoTex != null)
        {
            GUI.DrawTexture(new Rect(x, y, 55, 69), photoTex, ScaleMode.ScaleToFit);
        }
        float dataX = photoTex != null ? x + 65 : x;
        y = photoTex != null ? y + 75 : y;

        var charges = NPCDataStore.GetChargesForSubject(_modifySubjectName);
        var citations = NPCDataStore.GetCitationsForSubject(_modifySubjectName);
        var arrests = NPCDataStore.GetArrestsForSubject(_modifySubjectName);

        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(dataX, y, w - (dataX - x), 20), $"CHARGES ({charges.Count}):", _boldLabel);
        y += 24;

        if (charges.Count == 0)
        {
            GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 5, 16), "  No charges.", _labelStyle);
            y += 18;
        }
        else
        {
            for (int i = 0; i < charges.Count; i++)
            {
                var c = charges[i];
                string v = c.Verdict?.Outcome ?? "PENDING";
                string line = $"  {c.Charge.Name,-38} [{v}]  Filed: {c.FiledAt:MM/dd HH:mm}";
                GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 95, 16), line, _labelStyle);
                if (GUI.Button(new Rect(dataX + w - (dataX - x) - 85, y, 80, 16), "REMOVE", _removeStyle))
                {
                    NPCDataStore.RemoveCharge(c.Id);
                    SetStatus($"Removed charge: {c.Charge.Name}");
                }
                y += 18;
            }
        }

        y += 6;
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(dataX, y, w - (dataX - x), 20), $"CITATIONS ({citations.Count}):", _boldLabel);
        y += 24;

        if (citations.Count == 0)
        {
            GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 5, 16), "  No citations.", _labelStyle);
            y += 18;
        }
        else
        {
            for (int i = 0; i < citations.Count; i++)
            {
                var ct = citations[i];
                string line = $"  {ct.Charge.Name,-38} ${ct.Charge.FineMin}-${ct.Charge.FineMax}  Issued: {ct.IssuedAt:MM/dd HH:mm}";
                GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 95, 16), line, _labelStyle);
                if (GUI.Button(new Rect(dataX + w - (dataX - x) - 85, y, 80, 16), "REMOVE", _removeStyle))
                {
                    NPCDataStore.RemoveCitation(ct.Id);
                    SetStatus($"Removed citation: {ct.Charge.Name}");
                }
                y += 18;
            }
        }

        y += 6;
        _boldLabel.normal.textColor = _accentColor;
        GUI.Label(new Rect(dataX, y, w - (dataX - x), 20), $"ARREST RECORDS ({arrests.Count}):", _boldLabel);
        y += 24;

        if (arrests.Count == 0)
        {
            GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 5, 16), "  No arrests.", _labelStyle);
            y += 18;
        }
        else
        {
            for (int i = 0; i < arrests.Count; i++)
            {
                var a = arrests[i];
                string line = $"  {a.ArrestedAt:MM/dd/yyyy HH:mm} at {(string.IsNullOrEmpty(a.Location) ? "N/A" : a.Location)}";
                GUI.Label(new Rect(dataX + 5, y, w - (dataX - x) - 95, 16), line, _labelStyle);
                if (GUI.Button(new Rect(dataX + w - (dataX - x) - 85, y, 80, 16), "REMOVE", _removeStyle))
                {
                    NPCDataStore.RemoveArrest(a.Id);
                    SetStatus("Arrest record removed.");
                }
                y += 16;
                if (!string.IsNullOrEmpty(a.NatureOfArrest))
                {
                    GUI.Label(new Rect(dataX + 15, y, w - (dataX - x) - 15, 14), $"  Nature: {a.NatureOfArrest}", _labelStyle);
                    y += 14;
                }
                y += 4;
            }
        }

        y += 12;

        string newFullName = $"{_modifyNewFirstName} {_modifyNewLastName}".Trim();
        bool nameChanged = !string.IsNullOrWhiteSpace(newFullName) &&
                           !newFullName.Equals(_modifySubjectName, StringComparison.OrdinalIgnoreCase);

        if (nameChanged)
        {
            GUI.Label(new Rect(x, y, w, 18),
                $"Note: Name will be changed to \"{newFullName}\" for all records.", new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Italic, normal = { textColor = Color.yellow } });
            y += 22;
        }

        if (GUI.Button(new Rect(x, y, 160, 30), "SAVE CHANGES",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white, background = HighlightTex(Color.green * 0.6f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            if (nameChanged)
            {
                var subjectCharges = NPCDataStore.GetChargesForSubject(_modifySubjectName);
                foreach (var c in subjectCharges)
                    NPCDataStore.UpdateCharge(c.Id, c with { SubjectName = newFullName });

                var subjectCitations = NPCDataStore.GetCitationsForSubject(_modifySubjectName);
                foreach (var ct in subjectCitations)
                    NPCDataStore.UpdateCitation(ct.Id, ct with { SubjectName = newFullName });

                var subjectArrests = NPCDataStore.GetArrestsForSubject(_modifySubjectName);
                foreach (var a in subjectArrests)
                    NPCDataStore.UpdateArrest(a.Id, a with { SubjectName = newFullName });

                if (!string.IsNullOrWhiteSpace(NPCDataStore.GetPhoto(_modifySubjectName)))
                {
                    NPCDataStore.SetPhoto(newFullName, NPCDataStore.GetPhoto(_modifySubjectName)!);
                }

                _modifySubjectName = newFullName;
            }

            NPCDataStore.SaveData();
            SetStatus($"Changes saved for {_modifySubjectName}");
            _showModifyView = false;
        }

        if (GUI.Button(new Rect(x + 180, y, 100, 30), "BACK",
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    normal = { textColor = _textColor, background = HighlightTex(_accentColor * 0.4f) },
                    hover = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter
                }))
        {
            _showModifyView = false;
        }

        DrawStatus(screenRect);
    }

    private void DrawStatus(Rect rect)
    {
        if (_statusTimer <= 0 || string.IsNullOrEmpty(_statusMessage)) return;

        GUI.Label(new Rect(
            rect.x + (rect.width - 500) / 2f,
            rect.y + rect.height - 50,
            500, 30),
            _statusMessage, _statusStyle);
    }

    private void SetStatus(string msg)
    {
        _statusMessage = msg;
        _statusTimer = 4f;
    }
}
