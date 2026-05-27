using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MDTMod;

public sealed class PhotoBrowser
{
    private string[] _files = Array.Empty<string>();
    private Texture2D[] _textures = Array.Empty<Texture2D>();
    private Vector2 _scrollPos;
    private bool _loaded;

    private const string PhotosDir = @"C:\Users\joshy\OneDrive\Documents\CivIDs-Flashing Lights";
    private const int Columns = 4;
    private const float ThumbSize = 130f;
    private const float Padding = 8f;

    private static Texture2D? _texBg;
    private static Texture2D? _texBorder;
    private static Texture2D? _texHighlight;
    private static Color _cachedBg;
    private static Color _cachedAccent;

    public void Load()
    {
        Cleanup();
        _files = Directory.Exists(PhotosDir)
            ? Directory.GetFiles(PhotosDir, "*.png").OrderBy(f => f).ToArray()
            : Array.Empty<string>();
        _loaded = false;
    }

    public void Cleanup()
    {
        foreach (var t in _textures)
            if (t != null) UnityEngine.Object.Destroy(t);
        _textures = Array.Empty<Texture2D>();
        _files = Array.Empty<string>();
        _loaded = false;
    }

    public string? Render(Rect rect, Color bgColor, Color accentColor, Color textColor)
    {
        if (!_loaded)
        {
            LoadTextures();
            _loaded = true;
        }

        CacheTextures(bgColor, accentColor);

        GUI.DrawTexture(rect, _texBg!, ScaleMode.StretchToFill);
        float bw = 2;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bw), _texBorder!);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - bw, rect.width, bw), _texBorder!);
        GUI.DrawTexture(new Rect(rect.x, rect.y, bw, rect.height), _texBorder!);
        GUI.DrawTexture(new Rect(rect.x + rect.width - bw, rect.y, bw, rect.height), _texBorder!);

        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = accentColor } };
        GUI.Label(new Rect(rect.x, rect.y + 8, rect.width, 26), "SELECT MUGSHOT / ID PHOTO", titleStyle);
        GUI.DrawTexture(new Rect(rect.x + 20, rect.y + 40, rect.width - 40, 1), _texBorder!);

        if (_files.Length == 0)
        {
            var emptyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = textColor } };
            GUI.Label(new Rect(rect.x, rect.y + rect.height / 2f - 20, rect.width, 40), "No .png photos found in directory.", emptyStyle);
        }
        else
        {
            float contentX = rect.x + Padding;
            float contentY = rect.y + 48;
            float contentW = rect.width - Padding * 2;
            float contentH = rect.height - 90;

            float cellW = (contentW - Padding * (Columns - 1)) / Columns;
            float cellH = ThumbSize + 24;
            int rows = (_files.Length + Columns - 1) / Columns;
            float totalH = rows * cellH + 10;

            GUI.BeginGroup(new Rect(contentX, contentY, contentW, contentH));
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, contentW, contentH), _scrollPos, new Rect(0, 0, contentW - 20, totalH));

            for (int i = 0; i < _files.Length; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                float x = col * (cellW + Padding);
                float y = row * cellH + 4;

                var thumbRect = new Rect(x + 2, y, cellW - 4, ThumbSize);

                if (_textures[i] != null)
                    GUI.DrawTexture(thumbRect, _textures[i], ScaleMode.ScaleToFit);

                if (GUI.Button(thumbRect, "", new GUIStyle(GUI.skin.button) { normal = { background = null, textColor = Color.clear } }))
                    return _files[i];

                var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter, normal = { textColor = textColor } };
                GUI.Label(new Rect(x, y + ThumbSize + 2, cellW, 20), Path.GetFileNameWithoutExtension(_files[i]), nameStyle);
            }

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        float btnY = rect.y + rect.height - 35;
        if (GUI.Button(new Rect(rect.x + rect.width - 120, btnY, 100, 26), "CLOSE", new GUIStyle(GUI.skin.button) { fontSize = 12, normal = { textColor = Color.white, background = MakeHighlightTex(accentColor) } }))
            return "";

        return null;
    }

    private void LoadTextures()
    {
        _textures = new Texture2D[_files.Length];
        for (int i = 0; i < _files.Length; i++)
        {
            try
            {
                var bytes = File.ReadAllBytes(_files[i]);
                var tex = new Texture2D(2, 2);
                tex.hideFlags = HideFlags.DontSave;
                if (tex.LoadImage(bytes))
                    _textures[i] = tex;
                else
                    UnityEngine.Object.Destroy(tex);
            }
            catch { }
        }
    }

    private static void CacheTextures(Color bg, Color accent)
    {
        if (_texBg != null && _cachedBg == bg && _cachedAccent == accent) return;

        _texBg ??= new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        _texBorder ??= new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };

        _texBg.SetPixel(0, 0, bg); _texBg.Apply();
        _texBorder.SetPixel(0, 0, accent); _texBorder.Apply();
        _cachedBg = bg;
        _cachedAccent = accent;
    }

    private static Texture2D MakeHighlightTex(Color accent)
    {
        _texHighlight ??= new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        _texHighlight.SetPixel(0, 0, accent * 0.4f);
        _texHighlight.Apply();
        return _texHighlight;
    }
}
