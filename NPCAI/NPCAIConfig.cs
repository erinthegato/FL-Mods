using UnityEngine;

namespace NPCAI;

public sealed class NPCAIConfig
{
    public bool ModEnabled { get; set; } = true;
    public KeyCode ToggleKey { get; set; } = KeyCode.F9;
    public string ApiKey { get; set; } = "sk-or-v1-...";
    public string ApiEndpoint { get; set; } = "https://openrouter.ai/api";
    public string ApiModel { get; set; } = "deepseek/deepseek-v4-flash";
    public float AudioVolume { get; set; } = 0.7f;
}
