using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BackgroundRadio;

public sealed class AudioPlayer : IDisposable
{
    private Process? _process;
    private string? _currentUrl;
    private bool _disposed;
    private float _volume = 0.5f;

    public bool IsPlaying => _process != null && !_process.HasExited;
    public bool IsPaused => false;
    public string? CurrentUrl => _currentUrl;

    public event Action? PlaybackStarted;
    public event Action? PlaybackStopped;
    public event Action<string>? Error;

    public Task<bool> PlayAsync(string streamUrl)
    {
        if (_disposed) return Task.FromResult(false);

        try
        {
            StopInternal();

            int vol = Math.Clamp((int)(_volume * 100), 0, 100);
            string safeUrl = streamUrl.Replace("'", "''");

            var psi = new ProcessStartInfo("powershell")
            {
                Arguments = $"-NoProfile -Command \"$w=New-Object -ComObject WMPlayer.OCX; $w.settings.volume={vol}; $w.URL='{safeUrl}'; $w.controls.play(); while(1){{Start-Sleep 1}}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();
            proc.Exited += OnProcessExited;
            _process = proc;

            _currentUrl = streamUrl;
            PlaybackStarted?.Invoke();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Playback failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public void Stop()
    {
        StopInternal();
        PlaybackStopped?.Invoke();
    }

    private void StopInternal()
    {
        var proc = _process;
        if (proc == null) return;

        proc.EnableRaisingEvents = false;
        proc.Exited -= OnProcessExited;

        if (!proc.HasExited)
            proc.Kill(entireProcessTree: true);

        proc.Dispose();
        _process = null;
        _currentUrl = null;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _currentUrl = null;
        var proc = _process;
        if (proc != null)
        {
            proc.Exited -= OnProcessExited;
            proc.Dispose();
            _process = null;
        }
        PlaybackStopped?.Invoke();
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
        GC.SuppressFinalize(this);
    }
}
