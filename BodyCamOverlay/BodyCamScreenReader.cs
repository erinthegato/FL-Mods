using System.Diagnostics;
using System.Reflection;
using System.Text;
using FLMods.Shared;
using UnityEngine;

namespace BodyCamOverlay;

internal sealed class BodyCamScreenReader
{
    private readonly HashSet<string> _firstNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lastNames = new(StringComparer.OrdinalIgnoreCase);

    internal BodyCamScreenReader()
    {
        LoadNames("AI_First_names.txt", _firstNames);
        LoadNames("Ai_Last_Names.txt", _lastNames);
    }

    internal DriverLicenseScan? Scan()
    {
        string raw = ReadScreenText();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Parse(raw);
    }

    private void LoadNames(string fileName, HashSet<string> target)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "Assets", fileName);
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string clean = CleanToken(line);
                if (clean.Length > 1)
                    target.Add(clean);
            }
        }
        catch { }
    }

    private string ReadScreenText()
    {
        try
        {
            string script =
                "Add-Type -AssemblyName UIAutomationClient;" +
                "Add-Type -AssemblyName UIAutomationTypes;" +
                "$root=[System.Windows.Automation.AutomationElement]::RootElement;" +
                "$cond=[System.Windows.Automation.Condition]::TrueCondition;" +
                "$sb=New-Object System.Text.StringBuilder;" +
                "$els=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$cond);" +
                "foreach($e in $els){" +
                "try{" +
                "$n=$e.Current.Name; if($n){[void]$sb.AppendLine($n)};" +
                "$vp=$e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern); if($vp -and $vp.Current.Value){[void]$sb.AppendLine($vp.Current.Value)}" +
                "}catch{}}" +
                "$sb.ToString()";

            var psi = new ProcessStartInfo("powershell")
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return "";
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(1500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            return output;
        }
        catch
        {
            return "";
        }
    }

    private DriverLicenseScan Parse(string raw)
    {
        string first = FindField(raw, "DAC", "first", "firstname", "given");
        string last = FindField(raw, "DCS", "last", "lastname", "surname");
        string plate = FindField(raw, "PLATE", "plate", "licenseplate");
        string status = FindField(raw, "STATUS", "licensestatus");
        string weapon = FindField(raw, "WEAPON", "weaponlicense", "firearm", "firearms");

        var tokens = raw.Split(new[] { ' ', '\r', '\n', '\t', ',', ';', '|', ':', '=' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanToken)
            .Where(t => t.Length > 1)
            .ToList();

        if (string.IsNullOrWhiteSpace(first))
            first = tokens.FirstOrDefault(t => _firstNames.Contains(t)) ?? "";
        if (string.IsNullOrWhiteSpace(last))
            last = tokens.FirstOrDefault(t => _lastNames.Contains(t)) ?? "";

        if (string.IsNullOrWhiteSpace(status))
            status = GuessLicenseStatus(tokens);

        bool hasWeapon = weapon.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         weapon.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                         weapon.Equals("valid", StringComparison.OrdinalIgnoreCase) ||
                         (raw.Contains("weapon", StringComparison.OrdinalIgnoreCase) &&
                          raw.Contains("valid", StringComparison.OrdinalIgnoreCase));

        return new DriverLicenseScan(DateTime.Now, raw, first, last, plate, status, hasWeapon);
    }

    private static string GuessLicenseStatus(List<string> tokens)
    {
        if (tokens.Any(t => t.Equals("Suspended", StringComparison.OrdinalIgnoreCase))) return "Suspended";
        if (tokens.Any(t => t.Equals("Revoked", StringComparison.OrdinalIgnoreCase))) return "Revoked";
        if (tokens.Any(t => t.Equals("Expired", StringComparison.OrdinalIgnoreCase))) return "Expired";
        return "Valid";
    }

    private static string FindField(string raw, params string[] keys)
    {
        var lines = raw.Split(new[] { '\r', '\n', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            foreach (string key in keys)
            {
                if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    return CleanValue(trimmed[(key.Length + 1)..]);
                if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return CleanValue(trimmed[(key.Length + 1)..]);
                if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase) && trimmed.Length > key.Length)
                    return CleanValue(trimmed[key.Length..]);
            }
        }
        return "";
    }

    private static string CleanValue(string value) =>
        new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();

    private static string CleanToken(string value)
    {
        string clean = new(value.Trim().Where(char.IsLetter).ToArray());
        return clean.Length == 0 ? "" : char.ToUpperInvariant(clean[0]) + clean[1..].ToLowerInvariant();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";
}
