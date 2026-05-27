using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

const string CrashLogName = "CrashLog.txt";
const string GameExe = "flashinglights.exe";
const int PollIntervalMs = 2000;
const int HangThresholdSec = 10;

static string FindGameRoot()
{
    var loc = Environment.ProcessPath;
    if (string.IsNullOrEmpty(loc)) return ".";
    var dir = new DirectoryInfo(Path.GetDirectoryName(loc)!);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, GameExe)))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Path.GetDirectoryName(loc) ?? ".";
}

static string FindCrashLogPath(string gameRoot)
{
    var modsDir = Path.Combine(gameRoot, "Mods");
    return Directory.Exists(modsDir)
        ? Path.Combine(modsDir, CrashLogName)
        : Path.Combine(gameRoot, CrashLogName);
}

static void WriteCrashLog(string path, string message)
{
    try
    {
        File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
    }
    catch { }
}

static bool PromptYesNo(string question)
{
    Console.Write($"{question} (Y/N): ");
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Y) { Console.WriteLine("Y"); return true; }
        if (key.Key == ConsoleKey.N) { Console.WriteLine("N"); return false; }
    }
}

// Parse args
int? attachPid = null;
bool prompt = true;
bool silent = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--attach":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
                attachPid = pid;
            break;
        case "--noprompt":
            prompt = false;
            break;
        case "--silent":
            silent = true;
            prompt = false;
            break;
        case "--help":
        case "-h":
        case "/?":
            Console.WriteLine("FL Watchdog - monitors Flashing Lights for GPU/crash termination.");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  FLWatchdog [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --attach <PID>    Attach to an already-running Flashing Lights process");
            Console.WriteLine("  --noprompt        Don't ask for confirmation before launching");
            Console.WriteLine("  --silent          Run without console output (log only)");
            Console.WriteLine("  --help            Show this help");
            Environment.Exit(0);
            break;
    }
}

var gameRoot = FindGameRoot();
var crashLogPath = FindCrashLogPath(gameRoot);
var gamePath = Path.Combine(gameRoot, GameExe);

if (!silent)
{
    Console.WriteLine($"FL Watchdog — Crash Monitor");
    Console.WriteLine($"Game root:  {gameRoot}");
    Console.WriteLine($"Crash log:  {crashLogPath}");
    Console.WriteLine();
}

// Resolve game process
Process? gameProc = null;

if (attachPid.HasValue)
{
    try
    {
        gameProc = Process.GetProcessById(attachPid.Value);
        if (!silent)
            Console.WriteLine($"Attached to PID {attachPid.Value} ({gameProc.ProcessName})");
        WriteCrashLog(crashLogPath, $"[Watchdog] Attached to PID {attachPid.Value} ({gameProc.ProcessName})");
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine($"Error: No process with PID {attachPid.Value} found.");
        return 1;
    }
}
else
{
    if (!File.Exists(gamePath))
    {
        Console.Error.WriteLine($"Error: {GameExe} not found at {gamePath}");
        return 1;
    }

    if (prompt && !PromptYesNo($"Launch {GameExe} and monitor for crashes?"))
    {
        Console.WriteLine("Aborted.");
        return 0;
    }

    try
    {
        var psi = new ProcessStartInfo(gamePath)
        {
            UseShellExecute = true,
            WorkingDirectory = gameRoot
        };

        if (!silent)
            Console.WriteLine($"Launching {GameExe}...");

        gameProc = Process.Start(psi);
        if (gameProc == null)
        {
            Console.Error.WriteLine("Error: Failed to start process.");
            return 1;
        }

        if (!silent)
            Console.WriteLine($"Game PID: {gameProc.Id}");
        WriteCrashLog(crashLogPath, $"[Watchdog] Monitoring started — PID {gameProc.Id}");
    }
    catch (Win32Exception ex)
    {
        Console.Error.WriteLine($"Error launching game: {ex.Message}");
        return 1;
    }
}

// ── Monitoring loop ──
var hangStart = DateTime.MinValue;
var lastResponding = true;
var lastHangLog = DateTime.MinValue;
var lastRecoveryLog = DateTime.MinValue;
var started = DateTime.Now;

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    if (gameProc != null && !gameProc.HasExited)
    {
        Console.WriteLine("\nDetaching from game process (game continues running).");
        WriteCrashLog(crashLogPath, "[Watchdog] Detached by user (Ctrl+C)");
    }
    Environment.Exit(0);
};

if (!silent)
    Console.WriteLine("Monitoring (Ctrl+C to detach)...");

while (gameProc != null && !gameProc.HasExited)
{
    gameProc.Refresh();

    var responding = gameProc.Responding;
    var now = DateTime.Now;

    if (!responding)
    {
        if (lastResponding)
        {
            hangStart = now;
            if (!silent)
                Console.WriteLine($"[{now:HH:mm:ss}] WARNING: Game not responding");
        }

        var hangDuration = (now - hangStart).TotalSeconds;
        if (hangDuration >= HangThresholdSec && (now - lastHangLog).TotalSeconds >= HangThresholdSec)
        {
            lastHangLog = now;
            if (!silent)
                Console.WriteLine($"[{now:HH:mm:ss}] CRITICAL: Game unresponsive for {hangDuration:F0}s");
            WriteCrashLog(crashLogPath, $"[Watchdog] Game unresponsive for {hangDuration:F0}s — possible GPU hang/TDR");
        }
    }
    else if (!lastResponding && (now - lastRecoveryLog).TotalSeconds >= 30)
    {
        lastRecoveryLog = now;
        hangStart = DateTime.MinValue;
        if (!silent)
            Console.WriteLine($"[{now:HH:mm:ss}] Game recovered from hang");
        WriteCrashLog(crashLogPath, $"[Watchdog] Game recovered from hang");
    }

    lastResponding = responding;

    try
    {
        Thread.Sleep(PollIntervalMs);
    }
    catch { break; }
}

// ── Process exited — evaluate ──
var elapsed = DateTime.Now - started;
var exitCode = gameProc?.HasExited == true ? gameProc.ExitCode : -1;
var exitedNormally = exitCode == 0;

if (!silent)
{
    Console.WriteLine();
    Console.WriteLine($"Process exited after {elapsed.TotalMinutes:F1} min (code {exitCode})");
}

if (exitedNormally)
{
    WriteCrashLog(crashLogPath, $"[Watchdog] Game exited normally (code 0) after {elapsed.TotalMinutes:F1} min");
    if (!silent)
        Console.WriteLine("No crash detected.");
}
else
{
    var verdict = exitCode < 0 ? "process terminated abnormally" : $"exit code {exitCode}";
    WriteCrashLog(crashLogPath, $"[Watchdog] *** CRASH DETECTED *** {verdict} after {elapsed.TotalMinutes:F1} min");
    WriteCrashLog(crashLogPath, $"[Watchdog] Elapsed: {elapsed.TotalMinutes:F1} min | Exit code: {exitCode}");

    if (!lastResponding)
        WriteCrashLog(crashLogPath, $"[Watchdog] Process was unresponsive before exit — likely GPU driver TDR");

    // Try to append EventLog.txt context
    var eventLogPath = Path.Combine(Path.GetDirectoryName(crashLogPath) ?? gameRoot, "EventLog.txt");
    if (File.Exists(eventLogPath))
    {
        try
        {
            var tail = File.ReadLines(eventLogPath).Reverse().Take(20).Reverse().ToList();
            WriteCrashLog(crashLogPath, "=== Last 20 EventLog entries ===");
            foreach (var line in tail)
                WriteCrashLog(crashLogPath, line);
        }
        catch { }
    }

    WriteCrashLog(crashLogPath, "=== Watchdog Crash Log Ended ===");

    if (!silent)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"*** CRASH DETECTED — see {CrashLogName} for details ***");
        Console.ResetColor();
    }
}

return exitedNormally ? 0 : 1;
