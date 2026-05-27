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
    private bool _speechUnavailable;
    private float _retryTimer;
    private const float RetryCooldown = 30f;
    private float _commandCooldownTimer;
    private const float CommandCooldown = 1.5f;

    public bool IsRunning { get; private set; }
    public string LastTransmission { get; private set; } = "Ready. Use radio UI or press PTT for voice.";

    public event Action<CommandEventArgs>? CommandRecognized;

    public void Start()
    {
        if (IsRunning || _disposed || _startFailed || _speechUnavailable || _engineThread != null) return;

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
            _speechUnavailable = ex is FileNotFoundException;
            _startFailed = !_speechUnavailable;
            _retryTimer = _speechUnavailable ? 0f : RetryCooldown;
            MelonLoader.MelonLogger.Warning($"Speech recognition unavailable: {ex.Message}");
            LastTransmission = "Voice: speech recognition unavailable";
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

        var constraint = new SpeechRecognitionListConstraint(Phrases);
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

    private static readonly List<(string[] triggers, string code, string message)> CommandPatterns = new()
    {
        (new[] { "ten one" }, "10-1", "Unable to Copy — Signal Weak"),
        (new[] { "ten two" }, "10-2", "Receiving Well"),
        (new[] { "ten three" }, "10-3", "Stop Transmitting"),
        (new[] { "ten four", "acknowledged" }, "10-4", "Acknowledged"),
        (new[] { "ten five" }, "10-5", "Relay"),
        (new[] { "ten six", "busy", "stand by" }, "10-6", "Busy — Stand By"),
        (new[] { "ten seven", "out of service" }, "10-7", "Out of Service"),
        (new[] { "ten eight", "in service" }, "10-8", "In Service"),
        (new[] { "ten nine", "repeat" }, "10-9", "Say Again"),
        (new[] { "ten ten" }, "10-10", "Fight in Progress"),
        (new[] { "ten eleven" }, "10-11", "Animal Problem"),
        (new[] { "ten twelve" }, "10-12", "Stand By"),
        (new[] { "ten thirteen" }, "10-13", "Weather / Road Report"),
        (new[] { "ten fourteen" }, "10-14", "Escort"),
        (new[] { "ten fifteen" }, "10-15", "Prisoner in Custody"),
        (new[] { "ten sixteen" }, "10-16", "Pick Up for Questioning"),
        (new[] { "ten seventeen" }, "10-17", "Meet Complainant"),
        (new[] { "ten eighteen" }, "10-18", "Complete Quickly"),
        (new[] { "ten nineteen" }, "10-19", "Return to Station"),
        (new[] { "ten twenty" }, "10-20", "Requesting Location"),
        (new[] { "ten twenty one" }, "10-21", "Call by Telephone"),
        (new[] { "ten twenty two", "disregard" }, "10-22", "Disregard"),
        (new[] { "ten twenty three", "arriving on scene", "arrived on scene" }, "10-23", "Arrived on Scene"),
        (new[] { "ten twenty four", "assignment complete" }, "10-24", "Assignment Complete"),
        (new[] { "ten twenty five" }, "10-25", "Meet With"),
        (new[] { "ten twenty six" }, "10-26", "Detaining Subject"),
        (new[] { "ten twenty seven" }, "10-27", "Drivers License Check"),
        (new[] { "ten twenty eight" }, "10-28", "Vehicle Registration Check"),
        (new[] { "ten twenty nine" }, "10-29", "Check for Wants / Warrants"),
        (new[] { "ten thirty" }, "10-30", "Unnecessary Use of Radio"),
        (new[] { "ten thirty one" }, "10-31", "Crime in Progress"),
        (new[] { "ten thirty two", "shots fired" }, "10-32", "Man with Gun"),
        (new[] { "ten thirty three" }, "10-33", "Emergency — All Units Stand By"),
        (new[] { "ten thirty four" }, "10-34", "Riot"),
        (new[] { "ten thirty five" }, "10-35", "Major Crime Alert"),
        (new[] { "ten fifty", "traffic stop" }, "10-50", "Traffic Stop / Accident"),
        (new[] { "ten fifty one" }, "10-51", "Wrecker Needed"),
        (new[] { "ten fifty two" }, "10-52", "Ambulance Needed"),
        (new[] { "ten fifty three" }, "10-53", "Road Blocked"),
        (new[] { "ten fifty four" }, "10-54", "Hit and Run"),
        (new[] { "ten fifty five", "stolen vehicle" }, "10-55", "Stolen Vehicle"),
        (new[] { "ten fifty six" }, "10-56", "Intoxicated Driver"),
        (new[] { "ten fifty seven" }, "10-57", "Reckless Driving"),
        (new[] { "ten seventy six", "en route" }, "10-76", "En Route"),
        (new[] { "ten seventy eight", "officer needs assistance" }, "10-78", "Officer Needs Assistance"),
        (new[] { "ten seventy nine" }, "10-79", "Notify Coroner"),
        (new[] { "ten eighty", "pursuit" }, "10-80", "Pursuit in Progress"),
        (new[] { "ten eighty nine" }, "10-89", "Bomb Threat"),
        (new[] { "ten ninety" }, "10-90", "Bank Alarm"),
        (new[] { "ten ninety seven" }, "10-97", "Arrived on Scene"),
        (new[] { "ten ninety eight", "assignment complete" }, "10-98", "Assignment Complete"),
        (new[] { "ten ninety nine", "wanted person" }, "10-99", "Wanted / Stolen Record"),
        (new[] { "code three", "request backup" }, "Code 3", "Emergency Backup Requested"),
        (new[] { "code two" }, "Code 2", "Non-Emergency Backup Requested"),
        (new[] { "code four", "all clear" }, "Code 4", "All Clear — Situation Resolved"),
        (new[] { "cancel backup" }, "Cancel Backup", "Backup Cancelled"),
        (new[] { "signal one" }, "Signal 1", "Minor Incident Reported"),
        (new[] { "signal two" }, "Signal 2", "Major Incident Reported"),
        (new[] { "radio check" }, "Radio Check", "Communications Check — OK"),
        (new[] { "subject in custody" }, "10-15", "Prisoner in Custody"),
    };

    private static readonly IReadOnlyList<string> Phrases;

    static CommandEngine()
    {
        var phraseSet = new HashSet<string>();
        foreach (var (triggers, _, _) in CommandPatterns)
        foreach (var t in triggers)
            phraseSet.Add(t);
        Phrases = phraseSet.ToList().AsReadOnly();
    }

    private static (string? code, string message) MapCommand(string voiceCommand)
    {
        var lower = voiceCommand.ToLowerInvariant();
        foreach (var (triggers, code, message) in CommandPatterns)
        foreach (var trigger in triggers)
            if (lower.Contains(trigger))
                return (code, message);
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

        if (_startFailed && !_disposed && !_speechUnavailable)
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
