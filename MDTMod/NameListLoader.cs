using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MDTMod;

public static class NameListLoader
{
    private static List<string>? _firstNames;
    private static List<string>? _lastNames;

    private static readonly string FirstNamesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "OneDrive", "Documents", "CivIDs-Flashing Lights", "AI_First_names.txt");

    private static readonly string LastNamesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "OneDrive", "Documents", "CivIDs-Flashing Lights", "Ai_Last_Names.txt");

    private static void EnsureLoaded()
    {
        if (_firstNames != null) return;
        _firstNames = File.Exists(FirstNamesPath)
            ? File.ReadAllLines(FirstNamesPath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().OrderBy(n => n).ToList()
            : new List<string>();
        _lastNames = File.Exists(LastNamesPath)
            ? File.ReadAllLines(LastNamesPath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().OrderBy(n => n).ToList()
            : new List<string>();
    }

    public static List<string> MatchFirstNames(string prefix)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(prefix)) return new List<string>();
        return _firstNames!.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
    }

    public static List<string> MatchLastNames(string prefix)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(prefix)) return new List<string>();
        return _lastNames!.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
    }
}
