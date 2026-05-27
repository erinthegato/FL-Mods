using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackgroundRadio;

public sealed class OfflineScannerPlayer
{
    private readonly AudioPlayer _player;
    private List<string> _files = new();
    private int _currentIndex;
    private bool _stopped;
    private bool _waitingForPause;
    private float _pauseSeconds = 30f;

    public int FileCount => _files.Count;
    public int CurrentIndex => _currentIndex;
    public string? CurrentFileName => _currentIndex >= 0 && _currentIndex < _files.Count ? Path.GetFileName(_files[_currentIndex]) : null;
    public string? NextFileName => _currentIndex + 1 < _files.Count ? Path.GetFileName(_files[_currentIndex + 1]) : null;
    public bool IsPlaying => _player.IsPlaying || _waitingForPause;
    public IReadOnlyList<string> Playlist => _files;

    public event Action<string>? FileChanged;
    public event Action? PlaylistComplete;

    public OfflineScannerPlayer(AudioPlayer player)
    {
        _player = player;
        _player.PlaybackStopped += OnPlaybackStopped;
    }

    public void ScanDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            _files = Directory.GetFiles(path)
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();
        }
        else
        {
            _files = new List<string>();
        }
    }

    public void SetPauseSeconds(float seconds)
    {
        _pauseSeconds = Math.Max(0, seconds);
    }

    public async Task PlayAsync()
    {
        if (_files.Count == 0) return;
        _stopped = false;
        _currentIndex = 0;
        await PlayCurrentAsync();
    }

    public async Task PlayFromIndexAsync(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        _stopped = false;
        _currentIndex = index;
        await PlayCurrentAsync();
    }

    public void Stop()
    {
        _stopped = true;
        _waitingForPause = false;
        _player.Stop();
    }

    private async Task PlayCurrentAsync()
    {
        if (_stopped || _currentIndex >= _files.Count) return;
        string file = _files[_currentIndex];
        FileChanged?.Invoke(Path.GetFileName(file));
        await _player.PlayAsync(file);
    }

    private async void OnPlaybackStopped()
    {
        if (_stopped) return;

        _currentIndex++;
        if (_currentIndex >= _files.Count)
        {
            _currentIndex = 0;
            PlaylistComplete?.Invoke();
        }

        _waitingForPause = true;
        await Task.Delay((int)(_pauseSeconds * 1000));
        if (_stopped) { _waitingForPause = false; return; }
        _waitingForPause = false;
        await PlayCurrentAsync();
    }
}
