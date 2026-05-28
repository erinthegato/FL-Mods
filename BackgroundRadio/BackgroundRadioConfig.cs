namespace BackgroundRadio;

public sealed class BackgroundRadioConfig
{
    public float PanicResumeDelaySeconds { get; set; } = 180f;
    public float Volume { get; set; } = 0.5f;
    public bool OfflineMode { get; set; } = false;
    public float OfflinePauseSeconds { get; set; } = 30f;
    public bool ShowNowPlayingOverlay { get; set; } = true;
    public bool DebugLogging { get; set; } = false;
}
