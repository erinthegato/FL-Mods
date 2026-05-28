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
    private const int ScreenReadTimeoutMs = 900;
    private static readonly string ScreenReadScript =
        "Add-Type -AssemblyName UIAutomationClient;" +
        "Add-Type -AssemblyName UIAutomationTypes;" +
        "Add-Type @' using System; using System.Runtime.InteropServices; public static class W{[DllImport(\"user32.dll\")] public static extern IntPtr GetForegroundWindow();}'@;" +
        "$h=[W]::GetForegroundWindow();" +
        "$root=[System.Windows.Automation.AutomationElement]::FromHandle($h);" +
        "if($root -eq $null){$root=[System.Windows.Automation.AutomationElement]::FocusedElement};" +
        "$cond=[System.Windows.Automation.Condition]::TrueCondition;" +
        "$sb=New-Object System.Text.StringBuilder;" +
        "$els=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$cond);" +
        "$limit=[Math]::Min($els.Count,250);" +
        "for($i=0;$i -lt $limit;$i++){" +
        "$e=$els.Item($i);" +
        "try{" +
        "$n=$e.Current.Name; if($n){[void]$sb.AppendLine($n)};" +
        "$vp=$e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern); if($vp -and $vp.Current.Value){[void]$sb.AppendLine($vp.Current.Value)}" +
        "}catch{}}" +
        "$sb.ToString()";

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
            var psi = new ProcessStartInfo("powershell")
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(ScreenReadScript),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return "";
            if (!process.WaitForExit(ScreenReadTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "";
            }
            string output = process.StandardOutput.ReadToEnd();
            return output;
        }
        catch
        {
            return "";
        }
    }

    private DriverLicenseScan Parse(string raw)
    {
        var lines = raw.Split(new[] { '\r', '\n', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        string first = FindField(lines, "DAC", "first", "firstname", "given");
        string last = FindField(lines, "DCS", "last", "lastname", "surname");
        string plate = FindField(lines, "PLATE", "plate", "licenseplate");
        string status = FindField(lines, "STATUS", "licensestatus");
        string weapon = FindField(lines, "WEAPON", "weaponlicense", "firearm", "firearms");

        var tokens = raw.Split(new[] { ' ', '\r', '\n', '\t', ',', ';', '|', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);

        string firstToken = "";
        string lastToken = "";
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = CleanToken(tokens[i]);
                if (t.Length <= 1) continue;
                if (string.IsNullOrWhiteSpace(first) && _firstNames.Contains(t))
                    firstToken = t;
                if (string.IsNullOrWhiteSpace(last) && _lastNames.Contains(t))
                    lastToken = t;
                if (!string.IsNullOrWhiteSpace(firstToken) && !string.IsNullOrWhiteSpace(lastToken))
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(first))
            first = firstToken;

        if (string.IsNullOrWhiteSpace(last))
            last = lastToken;

        if (string.IsNullOrWhiteSpace(status))
            status = GuessLicenseStatus(tokens);

        bool hasWeapon = weapon.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         weapon.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                         weapon.Equals("valid", StringComparison.OrdinalIgnoreCase) ||
                         (raw.Contains("weapon", StringComparison.OrdinalIgnoreCase) &&
                          raw.Contains("valid", StringComparison.OrdinalIgnoreCase));

        return new DriverLicenseScan(DateTime.Now, raw, first, last, plate, status, hasWeapon);
    }

    private static string GuessLicenseStatus(string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = CleanToken(tokens[i]);
            if (t.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) return "Suspended";
            if (t.Equals("Revoked", StringComparison.OrdinalIgnoreCase)) return "Revoked";
            if (t.Equals("Expired", StringComparison.OrdinalIgnoreCase)) return "Expired";
        }
        return "Valid";
    }

    private static string FindField(string[] lines, params string[] keys)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            string trimmed = lines[li].Trim();
            for (int ki = 0; ki < keys.Length; ki++)
            {
                string key = keys[ki];
                int keyLen = key.Length;
                if (trimmed.Length <= keyLen) continue;

                if (char.ToUpperInvariant(trimmed[0]) == char.ToUpperInvariant(key[0]) &&
                    trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    char after = trimmed[keyLen];
                    if (after is ':' or '=')
                        return CleanValue(trimmed[(keyLen + 1)..]);
                    return CleanValue(trimmed[keyLen..]);
                }
            }
        }
        return "";
    }

    private static string CleanValue(string value)
    {
        ReadOnlySpan<char> span = value.Trim();
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (char.IsLetterOrDigit(c) || c is ' ' or '-')
                count++;
        }
        char[] arr = new char[count];
        int pos = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (char.IsLetterOrDigit(c) || c is ' ' or '-')
                arr[pos++] = c;
        }
        return new string(arr).Trim();
    }

    private static string CleanToken(string value)
    {
        ReadOnlySpan<char> span = value.Trim();
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsLetter(span[i]))
                count++;
        }
        if (count == 0) return "";
        char[] arr = new char[count];
        int pos = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (char.IsLetter(c))
                arr[pos++] = pos == 1 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
        }
        return new string(arr);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";
}
