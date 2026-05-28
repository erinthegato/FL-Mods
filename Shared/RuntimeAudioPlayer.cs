using System.Diagnostics;

internal sealed class RuntimeAudioPlayer : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly object _gate = new();
    private bool _disposed;
    private readonly Action<string>? _debugLog;
    private readonly Action<string>? _warningLog;

    internal RuntimeAudioPlayer(Action<string>? debugLog = null, Action<string>? warningLog = null)
    {
        _debugLog = debugLog;
        _warningLog = warningLog;
    }

    internal void Play(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        lock (_gate)
        {
            try
            {
                EnsureProcess();
                if (_stdin == null) return;
                _stdin.WriteLine(path);
                _stdin.Flush();
            }
            catch (Exception ex)
            {
                _warningLog?.Invoke($"Audio playback failed: {ex.Message}");
                StopProcess();
            }
        }
    }

    internal void PlaySequence(params string?[] paths)
    {
        foreach (string? path in paths)
            if (!string.IsNullOrWhiteSpace(path))
                Play(path);
    }

    private void EnsureProcess()
    {
        if (_process is { HasExited: false } && _stdin != null) return;
        StopProcess();

        string command =
            "$ErrorActionPreference='SilentlyContinue'; " +
            "while (($p = [Console]::In.ReadLine()) -ne $null) { " +
            "if ([IO.File]::Exists($p)) { " +
            "$s = New-Object Media.SoundPlayer $p; $s.PlaySync(); " +
            "} }";

        var psi = new ProcessStartInfo("powershell")
        {
            Arguments = $"-NoProfile -Command \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        };

        _process = Process.Start(psi);
        _stdin = _process?.StandardInput;
        _debugLog?.Invoke("Started persistent WAV audio helper.");
    }

    private void StopProcess()
    {
        try { _stdin?.Dispose(); } catch { }
        _stdin = null;

        var proc = _process;
        _process = null;
        if (proc == null) return;

        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }

        try { proc.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
            StopProcess();
    }
}
