using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.SpeechRecognition;

namespace GrammarPoliceMod;

public sealed class CommandEngine : IDisposable
{
    private SpeechRecognizer? _recognizer;
    private Thread? _engineThread;
    private volatile bool _stopping;
    private bool _disposed;
    private bool _startFailed;
    private float _retryTimer;
    private const float RetryCooldown = 30f;
    private float _commandCooldownTimer;
    private const float CommandCooldown = 1.5f;

    public bool IsRunning { get; private set; }
    public bool IsVoiceAvailable { get; private set; } = true;
    public string LastTransmission { get; private set; } = "Ready. Use radio UI or press PTT for voice.";

    public event Action<CommandEventArgs>? CommandRecognized;

    public static bool IsSpeechRuntimeAvailable(out string reason)
    {
        try
        {
            Assembly.Load("Microsoft.Windows.SDK.NET");
            reason = "";
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            reason = ex.Message;
            return false;
        }
    }

    public void DisableVoice(string reason)
    {
        IsVoiceAvailable = false;
        _startFailed = false;
        LastTransmission = string.IsNullOrWhiteSpace(reason)
            ? "Voice: button-only mode"
            : $"Voice: button-only mode ({reason})";
    }

    public void Start()
    {
        if (IsRunning || _disposed || !IsVoiceAvailable) return;

        if (!IsSpeechRuntimeAvailable(out string reason))
        {
            DisableVoice(reason);
            MelonLoader.MelonLogger.Warning($"Speech recognition unavailable: {reason}");
            return;
        }

        _stopping = false;
        _engineThread = new Thread(RunEngine);
        _engineThread.SetApartmentState(ApartmentState.STA);
        _engineThread.Start();
    }

    private void RunEngine()
    {
        try
        {
            var task = RunEngineAsync();
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            IsVoiceAvailable = false;
            _startFailed = false;
            MelonLoader.MelonLogger.Warning($"Speech recognition unavailable: {ex.Message}");
            LastTransmission = "Voice: button-only mode";
            IsRunning = false;
        }
        finally
        {
            _engineThread = null;
        }
    }

    private async Task RunEngineAsync()
    {
        _recognizer = new SpeechRecognizer();
        MelonLoader.MelonLogger.Msg($"Using recognizer: {_recognizer.CurrentLanguage.DisplayName}");

        // Build phrases from currently loaded codes (will be updated via static property)
        var phraseSet = new HashSet<string>();
        foreach (var code in RadioCodeLoader.CurrentCodes)
            foreach (var t in code.VoiceTriggers)
                phraseSet.Add(t.ToLowerInvariant());
        var phrases = phraseSet.ToList();

        var constraint = new SpeechRecognitionListConstraint(phrases);
        _recognizer.Constraints.Add(constraint);

        var compileResult = await _recognizer.CompileConstraintsAsync();
        if (compileResult.Status != SpeechRecognitionResultStatus.Success)
            throw new InvalidOperationException($"Grammar compilation failed: {compileResult.Status}");

        _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;

        await _recognizer.ContinuousRecognitionSession.StartAsync();
        IsRunning = true;
        _startFailed = false;
        LastTransmission = "Voice recognition active.";

        while (!_stopping)
            Thread.Sleep(100);
    }

    private void OnResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var text = args.Result.Text.ToLowerInvariant();
        var confidenceLevel = args.Result.Confidence;

        if (confidenceLevel == SpeechRecognitionConfidence.Rejected)
            return;

        var threshold = GrammarPoliceMod.Instance.ConfidenceThreshold;
        if (confidenceLevel == SpeechRecognitionConfidence.Low && threshold > 0.3)
            return;
        if (confidenceLevel == SpeechRecognitionConfidence.Medium && threshold > 0.65)
            return;

        var (code, message) = MapCommand(text);
        if (code != null && _commandCooldownTimer <= 0)
        {
            _commandCooldownTimer = CommandCooldown;
            var confFraction = confidenceLevel switch
            {
                SpeechRecognitionConfidence.High => 0.9,
                SpeechRecognitionConfidence.Medium => 0.6,
                _ => 0.3
            };
            ExecuteCommand(code, message, confFraction);
        }
    }

    private static (string? code, string message) MapCommand(string voiceCommand)
    {
        var lower = voiceCommand.ToLowerInvariant();
        foreach (var code in RadioCodeLoader.CurrentCodes)
            foreach (var trigger in code.VoiceTriggers)
                if (lower.Contains(trigger.ToLowerInvariant()))
                    return (code.Code, code.Description);
        return (null, "");
    }

    public void ExecuteCommand(string code, string message, double? confidence = null)
    {
        var conf = confidence.HasValue ? $" [{confidence:P0}]" : "";
        var args = new CommandEventArgs(code, message, confidence);
        CommandRecognized?.Invoke(args);
        LastTransmission = $"{code}: {message}{conf}";

        if (GrammarPoliceMod.Instance.VerboseLogging)
            MelonLoader.MelonLogger.Msg($"Command: {code} - {message}");

        if (GrammarPoliceMod.Instance.KeyEmulationEnabled)
            SimulateKeySequence(code);
    }

    private static void SimulateKeySequence(string code)
    {
        if (string.IsNullOrEmpty(code) || !code.StartsWith("10-")) return;
        if (!int.TryParse(code.AsSpan(3), out int num) || num < 1 || num > 10) return;
        string sequence = GrammarPoliceMod.Instance.GetKeySequence(num);
        KeySimulator.PressSequence(sequence);
    }

    public void Stop()
    {
        if (!IsRunning || _disposed) return;
        _stopping = true;
        if (_recognizer != null)
        {
            try
            {
                var stopTask = _recognizer.ContinuousRecognitionSession.StopAsync();
                stopTask.AsTask().GetAwaiter().GetResult();
            }
            catch { }
            _recognizer.Dispose();
            _recognizer = null;
        }
        IsRunning = false;
        _engineThread = null;
        LastTransmission = "Voice recognition stopped.";
    }

    public void Update()
    {
        if (_commandCooldownTimer > 0)
            _commandCooldownTimer -= UnityEngine.Time.unscaledDeltaTime;

        if (_startFailed && !_disposed && !IsVoiceAvailable)
        {
            _retryTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (_retryTimer <= 0)
            {
                _startFailed = false;
                Start();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

public sealed record CommandEventArgs(string Code, string Message, double? Confidence);
