using System.Reflection;
using UnityEngine;

internal static class KeyBindStore
{
    internal static KeyCode Load(string fileName, string id, KeyCode fallback)
    {
        try
        {
            string path = GetPath(fileName);
            if (!File.Exists(path)) return fallback;

            foreach (string line in File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                if (!line[..split].Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                if (Enum.TryParse(line[(split + 1)..].Trim(), true, out KeyCode value))
                    return value;
            }
        }
        catch { }

        return fallback;
    }

    internal static void Save(string fileName, string id, KeyCode value)
    {
        try
        {
            string path = GetPath(fileName);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    values[line[..split].Trim()] = line[(split + 1)..].Trim();
                }
            }

            values[id] = value.ToString();
            File.WriteAllLines(path, values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch { }
    }

    private static string GetPath(string fileName)
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        return Path.Combine(dir, fileName);
    }
}

internal static class KeyBindWidget
{
    private static string? _activeId;

    internal static bool IsCapturing => _activeId != null;

    internal static KeyCode Draw(Rect rect, string fileName, string id, string label, KeyCode current)
    {
        string buttonText = _activeId == id ? $"{label}: press key (Esc cancels)" : $"{label}: {current}";
        if (GUI.Button(rect, buttonText))
            _activeId = id;

        if (_activeId != id)
            return current;

        var ev = Event.current;
        if (ev.type == EventType.KeyDown)
        {
            ev.Use();
            if (ev.keyCode == KeyCode.Escape)
            {
                _activeId = null;
                return current;
            }

            if (ev.keyCode != KeyCode.None)
                return Capture(fileName, id, ev.keyCode);
        }

        if (ev.type == EventType.MouseDown)
        {
            ev.Use();
            return Capture(fileName, id, MouseButtonToKeyCode(ev.button, current));
        }

        return current;
    }

    internal static void Cancel()
    {
        _activeId = null;
    }

    private static KeyCode Capture(string fileName, string id, KeyCode value)
    {
        if (value != KeyCode.None)
            KeyBindStore.Save(fileName, id, value);
        _activeId = null;
        return value;
    }

    private static KeyCode MouseButtonToKeyCode(int button, KeyCode fallback) => button switch
    {
        0 => KeyCode.Mouse0,
        1 => KeyCode.Mouse1,
        2 => KeyCode.Mouse2,
        3 => KeyCode.Mouse3,
        4 => KeyCode.Mouse4,
        5 => KeyCode.Mouse5,
        6 => KeyCode.Mouse6,
        _ => fallback
    };
}
