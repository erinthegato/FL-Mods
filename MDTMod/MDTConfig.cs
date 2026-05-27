using UnityEngine;

namespace MDTMod;

public sealed class MDTConfig
{
    public KeyCode MDTToggleKey { get; set; } = KeyCode.F11;
    public int CourtIntervalMinutes { get; set; } = 5;
}
