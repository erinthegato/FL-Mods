using System.Diagnostics;
using MelonLoader;

namespace GrammarPoliceMod;

public sealed class OfflineSpeechBridge : IDisposable
{
    private readonly string _directory;
    private readonly string _inboxPath;
    private DateTime _lastWriteUtc;
    private Process? _process;
    private float _pollTimer;

    public bool IsEnabled { get; private set; }
    public string Status { get; private set; } = "Offline speech inactive.";

    public event Action<string>? TextReceived;

    public OfflineSpeechBridge()
    {
        _directory = Path.Combine(Path.GetDirectoryName(typeof(GrammarPoliceMod).Assembly.Location) ?? ".", "GrammarPolice", "OfflineSpeech");
        _inboxPath = Path.Combine(_directory, "transcript.txt");
    }

    public void Start(string command)
    {
        Directory.CreateDirectory(_directory);
        if (!File.Exists(_inboxPath))
            File.WriteAllText(_inboxPath, "");

        IsEnabled = true;
        Status = $"Offline speech bridge watching {_inboxPath}";

        if (!string.IsNullOrWhiteSpace(command))
            StartExternalCommand(command);
    }

    public void Update(float dt)
    {
        if (!IsEnabled) return;

        _pollTimer -= dt;
        if (_pollTimer > 0f) return;
        _pollTimer = 0.25f;

        try
        {
            if (!File.Exists(_inboxPath)) return;
            var writeUtc = File.GetLastWriteTimeUtc(_inboxPath);
            if (writeUtc <= _lastWriteUtc) return;

            _lastWriteUtc = writeUtc;
            string[] lines = File.ReadAllLines(_inboxPath);
            string text = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(text))
                TextReceived?.Invoke(text);
        }
        catch { }
    }

    private void StartExternalCommand(string command)
    {
        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    WorkingDirectory = _directory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            _process.Start();
            Status = "Offline speech bridge started external recognizer.";
        }
        catch (Exception ex)
        {
            Status = $"Offline speech bridge ready, external recognizer failed: {ex.Message}";
            MelonLogger.Warning($"[GrammarPolice] Offline speech recognizer launch failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        IsEnabled = false;
        try
        {
            if (_process != null && !_process.HasExited)
                _process.Kill();
        }
        catch { }
        _process?.Dispose();
        _process = null;
    }
}
