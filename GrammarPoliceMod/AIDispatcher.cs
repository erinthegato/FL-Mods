using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;

namespace GrammarPoliceMod;

public sealed class DispatchAudio
{
    public static volatile bool AudioDuckActive;

    private readonly Dictionary<string, string> _audioFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeAudioPlayer _audioPlayer;
    private string _audioDir = string.Empty;
    private string _panicDir = string.Empty;
    private string[] _panicFiles = Array.Empty<string>();

    public IReadOnlyList<string> PanicAudioFiles => _panicFiles;

    public DispatchAudio()
    {
        _audioPlayer = new RuntimeAudioPlayer(
            debugLog: msg => { if (GrammarPoliceMod.Instance.VerboseLogging) MelonLogger.Msg($"[DispatchAudio] {msg}"); },
            warningLog: msg => MelonLogger.Warning($"[DispatchAudio] {msg}"));

        try
        {
            _audioDir = Path.Combine(
                Path.GetDirectoryName(typeof(GrammarPoliceMod).Assembly.Location) ?? ".",
                "..", "DispatchAudio");

            _audioDir = Path.GetFullPath(_audioDir);

            if (Directory.Exists(_audioDir))
            {
                var wavs = Directory.GetFiles(_audioDir, "*.wav");
                foreach (var f in wavs)
                {
                    string code = Path.GetFileNameWithoutExtension(f).Trim();
                    if (!string.IsNullOrWhiteSpace(code))
                        _audioFiles[code] = f;
                }
                MelonLogger.Msg($"[DispatchAudio] Loaded {_audioFiles.Count} audio files from {_audioDir}");
            }
            else
            {
                Directory.CreateDirectory(_audioDir);
                MelonLogger.Msg($"[DispatchAudio] Created directory: {_audioDir}");
            }

            _panicDir = Path.Combine(_audioDir, "Panic Button");
            if (Directory.Exists(_panicDir))
            {
                _panicFiles = Directory.GetFiles(_panicDir, "*.wav")
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(n => n)
                    .ToArray();
                if (_panicFiles.Length > 0)
                    MelonLogger.Msg($"[DispatchAudio] Panic Button audio files: {string.Join(", ", _panicFiles)}");
                else
                    MelonLogger.Msg("[DispatchAudio] Panic Button folder is empty (no .wav files).");
            }
            else
            {
                Directory.CreateDirectory(_panicDir);
                MelonLogger.Msg($"[DispatchAudio] Created Panic Button folder: {_panicDir}");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DispatchAudio] Init error: {ex.Message}");
        }
    }

    public void Play(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var path = FindAudio(code);
        if (path == null)
        {
            MelonLogger.Msg($"[DispatchAudio] No audio for '{code}'");
            return;
        }

        if (GrammarPoliceMod.Instance.VerboseLogging)
            MelonLogger.Msg($"[DispatchAudio] Playing: {code} ({Path.GetFileName(path)})");
        PlayCore(path);
    }

    public void PlayAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var path = FindAudio(code);
        if (path == null) return;

        if (GrammarPoliceMod.Instance.VerboseLogging)
            MelonLogger.Msg($"[DispatchAudio] Playing async: {code}");
        PlayCore(path);
    }

    public void PlayPanicTone(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return;

        try
        {
            string path = Path.Combine(_panicDir, filename);
            if (!File.Exists(path))
            {
                if (_panicFiles.Length > 0)
                {
                    path = Path.Combine(_panicDir, _panicFiles[0]);
                    if (GrammarPoliceMod.Instance.VerboseLogging)
                        MelonLogger.Msg($"[DispatchAudio] Panic file '{filename}' not found, using '{_panicFiles[0]}'");
                }
                else
                {
                    MelonLogger.Msg("[DispatchAudio] No panic audio files available.");
                    return;
                }
            }

            if (GrammarPoliceMod.Instance.VerboseLogging)
                MelonLogger.Msg($"[DispatchAudio] Playing panic tone: {Path.GetFileName(path)}");
            PlayCore(path);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[DispatchAudio] Panic tone error: {ex.Message}");
        }
    }

    private void PlayCore(string path)
    {
        AudioDuckActive = true;
        _audioPlayer.Play(path);
        AudioDuckActive = false;
    }

    private string? FindAudio(string code)
    {
        if (_audioFiles.TryGetValue(code, out var path))
            return path;

        foreach (var kv in _audioFiles)
        {
            if (code.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        if (_audioFiles.TryGetValue("10-4", out var fallback))
            return fallback;

        return _audioFiles.Values.FirstOrDefault();
    }
}
