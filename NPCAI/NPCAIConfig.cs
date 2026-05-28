namespace NPCAI;

public sealed class NPCAIConfig
{
    public bool ModEnabled { get; set; } = true;
    public string ApiKey { get; set; } = "sk-or-v1-...";
    public string ApiEndpoint { get; set; } = "https://openrouter.ai/api";
    public string ApiModel { get; set; } = "deepseek/deepseek-v4-flash";
    public float AudioVolume { get; set; } = 0.7f;
    public float InteractionRange { get; set; } = 5f;
    public float NearbyScanIntervalSeconds { get; set; } = 0.75f;
    public float NpcCacheRefreshIntervalSeconds { get; set; } = 8f;
    public bool DebugLogging { get; set; } = false;

    // New settings
    public bool PerformanceMode { get; set; } = false;
    public bool EnableIdLookup { get; set; } = true;
    public bool EnableDispatchIntegration { get; set; } = true;
}
