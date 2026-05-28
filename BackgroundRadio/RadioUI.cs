using System.IO;
using System.Linq;
using UnityEngine;

namespace BackgroundRadio;

public sealed class RadioUI
{
    private Vector2 _scrollPos;
    private int _selectedIndex;
    private string _statusText = "";
    private float _statusTimer;
    public string? ActiveFeedName { get; private set; }

    private static GUIStyle? _titleStyle;
    private static GUIStyle? _itemStyle;
    private static GUIStyle? _selectedStyle;
    private static GUIStyle? _statusStyle;
    private static GUIStyle? _infoStyle;
    private static GUIStyle? _statusInfoStyle;
    private static Texture2D? _bgTex;
    private static Texture2D? _borderTex;
    private static bool _stylesReady;

    private static void EnsureStyles()
    {
        if (_stylesReady || GUI.skin == null) return;
        _stylesReady = true;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.2f, 0.6f, 1f) }
        };

        _itemStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12, alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white },
            hover = { textColor = Color.white }
        };

        _selectedStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12, alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white, background = MakeTex(new Color(0.15f, 0.35f, 0.7f)) },
            hover = { textColor = Color.white }
        };

        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.green }
        };

        _infoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.gray }
        };

        _statusInfoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.yellow }
        };

        _bgTex = MakeTex(new Color(0.05f, 0.05f, 0.12f));
        _borderTex = MakeTex(new Color(0.2f, 0.35f, 0.7f));
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    public void SetStatus(string text)
    {
        _statusText = text;
        _statusTimer = 4f;
    }

    public void Draw(Rect rect, BroadcastifyService service, AudioPlayer player, BackgroundRadioConfig config,
        bool isLoading, string? currentStation, OfflineScannerPlayer? offlinePlayer = null)
    {
        EnsureStyles();
        GUI.DrawTexture(rect, _bgTex!);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), _borderTex!);

        bool offline = config.OfflineMode && offlinePlayer != null;

        float y = rect.y + 8;
        GUI.Label(new Rect(rect.x, y, rect.width, 24), offline ? "OFFLINE SCANNER" : "BACKGROUND RADIO", _titleStyle);
        y += 30;

        bool playing = offline ? offlinePlayer!.IsPlaying : player.IsPlaying;
        string playState = playing ? "PLAYING" : "STOPPED";
        Color stateColor = playing ? Color.green : Color.gray;
        _statusStyle!.normal.textColor = stateColor;
        GUI.Label(new Rect(rect.x, y, rect.width, 18), playState, _statusStyle);
        y += 20;

        if (offline)
        {
            string now = offlinePlayer!.CurrentFileName ?? "(none)";
            string next = offlinePlayer.NextFileName ?? "(loop to start)";
            _infoStyle!.normal.textColor = Color.cyan;
            GUI.Label(new Rect(rect.x + 5, y, rect.width - 10, 16), $"Now: {now}", _infoStyle);
            y += 18;
            _infoStyle!.normal.textColor = new Color(0.4f, 0.8f, 0.4f);
            GUI.Label(new Rect(rect.x + 5, y, rect.width - 10, 16), $"Next: {next}", _infoStyle);
            y += 18;
        }
        else if (!string.IsNullOrEmpty(currentStation))
        {
            _infoStyle!.normal.textColor = Color.cyan;
            GUI.Label(new Rect(rect.x + 5, y, rect.width - 10, 16), currentStation, _infoStyle);
            y += 18;
        }

        y += 4;

        var contentRect = new Rect(rect.x + 4, y, rect.width - 8, rect.y + rect.height - y - 118);
        GUI.BeginGroup(contentRect);

        float cx = 0, cy = 0, cw = contentRect.width, ch = contentRect.height;
        float itemH = 24f;

        if (offline)
        {
            var files = offlinePlayer!.Playlist;
            float totalH = Mathf.Max(ch, files.Count * itemH + 10);
            _scrollPos = GUI.BeginScrollView(new Rect(cx, cy, cw, ch), _scrollPos, new Rect(0, 0, cw - 20, totalH));

            if (files.Count == 0)
            {
                GUI.Label(new Rect(10, 10, cw - 30, 22), "No audio files found in OfflineScanner/", _infoStyle!);
            }
            else
            {
                float iy = 4;
                for (int i = 0; i < files.Count; i++)
                {
                    bool sel = i == offlinePlayer.CurrentIndex;
                    string label = (sel ? "> " : "  ") + Path.GetFileName(files[i]);
                    var style = sel ? _selectedStyle! : _itemStyle!;

                    if (GUI.Button(new Rect(4, iy, cw - 28, itemH - 2), label, style))
                    {
                        offlinePlayer.Stop();
                        _ = offlinePlayer.PlayFromIndexAsync(i);
                        SetStatus($"Playing: {Path.GetFileName(files[i])}");
                    }
                    iy += itemH;
                }
            }

            GUI.EndScrollView();
        }
        else
        {
            var feeds = service.GetCachedFeeds();
            float totalH = Mathf.Max(ch, feeds.Count * itemH + 10);
            _scrollPos = GUI.BeginScrollView(new Rect(cx, cy, cw, ch), _scrollPos, new Rect(0, 0, cw - 20, totalH));

            if (isLoading)
            {
                GUI.Label(new Rect(10, 10, cw - 30, 22), "Loading feeds...", _infoStyle!);
            }
            else if (feeds.Count == 0)
            {
                GUI.Label(new Rect(10, 10, cw - 30, 22), "No feeds available.", _infoStyle!);
            }
            else
            {
                float iy = 4;
                for (int i = 0; i < feeds.Count; i++)
                {
                    bool sel = i == _selectedIndex;
                    string label = $"#{feeds[i].Listeners,5}  {feeds[i].Name}";
                    var style = sel ? _selectedStyle! : _itemStyle!;

                    if (GUI.Button(new Rect(4, iy, cw - 28, itemH - 2), label, style))
                    {
                        _selectedIndex = i;
                        SetStatus($"Selected: {feeds[i].Name}");
                    }
                    iy += itemH;
                }
            }

            GUI.EndScrollView();
        }

        GUI.EndGroup();

        float by = rect.y + rect.height - 30;
        _infoStyle!.normal.textColor = Color.gray;
        string hint = offline
            ? $"{BackgroundRadioMod.Instance.NavigateUpKey}/{BackgroundRadioMod.Instance.NavigateDownKey}: Navigate  {BackgroundRadioMod.Instance.SelectKey}: Play  {BackgroundRadioMod.Instance.StopKey}: Stop  {BackgroundRadioMod.Instance.ToggleKey}: Close"
            : $"{BackgroundRadioMod.Instance.NavigateUpKey}/{BackgroundRadioMod.Instance.NavigateDownKey}: Navigate  {BackgroundRadioMod.Instance.SelectKey}: Play  {BackgroundRadioMod.Instance.StopKey}: Stop  {BackgroundRadioMod.Instance.ToggleKey}: Close";
        GUI.Label(new Rect(rect.x + 5, by, rect.width - 10, 24), hint, _infoStyle);

        if (_statusTimer > 0)
        {
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer > 0 && !string.IsNullOrEmpty(_statusText))
            {
                var s = _statusInfoStyle;
                GUI.Label(new Rect(rect.x, by - 20, rect.width, 18), _statusText, s);
            }
        }
    }

    public void HandleKeyboard(BroadcastifyService service, AudioPlayer player, BackgroundRadioConfig config,
        OfflineScannerPlayer? offlinePlayer = null)
    {
        bool offline = config.OfflineMode && offlinePlayer != null;

        if (offline)
        {
            var files = offlinePlayer!.Playlist;
            if (files.Count == 0) return;

            if (Input.GetKeyDown(BackgroundRadioMod.Instance.NavigateUpKey))
            {
                _selectedIndex = (_selectedIndex - 1 + files.Count) % files.Count;
                ScrollToVisible();
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.NavigateDownKey))
            {
                _selectedIndex = (_selectedIndex + 1) % files.Count;
                ScrollToVisible();
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.SelectKey))
            {
                offlinePlayer.Stop();
                _ = offlinePlayer.PlayFromIndexAsync(_selectedIndex);
                SetStatus($"Playing: {Path.GetFileName(files[_selectedIndex])}");
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.StopKey))
            {
                offlinePlayer.Stop();
                SetStatus("Playback stopped.");
            }
        }
        else
        {
            var feeds = service.GetCachedFeeds();
            if (feeds.Count == 0) return;

            if (Input.GetKeyDown(BackgroundRadioMod.Instance.NavigateUpKey))
            {
                _selectedIndex = (_selectedIndex - 1 + feeds.Count) % feeds.Count;
                ScrollToVisible();
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.NavigateDownKey))
            {
                _selectedIndex = (_selectedIndex + 1) % feeds.Count;
                ScrollToVisible();
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.SelectKey))
            {
                var feed = feeds[_selectedIndex];
                SetStatus($"Connecting to {feed.Name}...");
                _ = PlayFeedAsync(service, player, feed.Id, feed.Name);
            }
            else if (Input.GetKeyDown(BackgroundRadioMod.Instance.StopKey))
            {
                player.Stop();
                SetStatus("Playback stopped.");
            }
        }
    }

    private void ScrollToVisible()
    {
        float itemH = 24f;
        float targetY = _selectedIndex * itemH;
        float visibleHeight = 340f;
        if (targetY < _scrollPos.y)
            _scrollPos.y = targetY;
        else if (targetY + itemH > _scrollPos.y + visibleHeight)
            _scrollPos.y = targetY - visibleHeight + itemH;
    }

    private async System.Threading.Tasks.Task PlayFeedAsync(BroadcastifyService service, AudioPlayer player, int feedId, string feedName)
    {
        string? url = await service.GetStreamUrlAsync(feedId);
        if (url != null)
        {
            bool ok = await player.PlayAsync(url);
            if (ok) ActiveFeedName = feedName;
            SetStatus(ok ? $"Now playing: {feedName}" : $"Failed: {feedName}");
        }
        else
        {
            SetStatus("Could not get stream URL.");
        }
    }

    public void ResetSelection()
    {
        _selectedIndex = 0;
        _scrollPos = Vector2.zero;
    }
}
