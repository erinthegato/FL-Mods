using UnityEngine;

namespace BackgroundRadio;

public sealed class BackgroundRadioConfig
{
    public KeyCode ToggleKey { get; set; } = KeyCode.F10;
    public KeyCode NavigateUpKey { get; set; } = KeyCode.UpArrow;
    public KeyCode NavigateDownKey { get; set; } = KeyCode.DownArrow;
    public KeyCode SelectKey { get; set; } = KeyCode.Return;
    public KeyCode StopKey { get; set; } = KeyCode.Space;
    public float PanicResumeDelaySeconds { get; set; } = 180f;
    public float Volume { get; set; } = 0.5f;
    public bool OfflineMode { get; set; } = false;
    public float OfflinePauseSeconds { get; set; } = 30f;
    public bool DebugLogging { get; set; } = false;
}
