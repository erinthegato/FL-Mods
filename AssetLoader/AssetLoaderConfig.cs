using FlashingLights.ModKit.Core;

namespace AssetLoader;

public sealed class AssetLoaderConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoLoadOnStartup { get; set; } = true;
    public bool InstantiatePrefabAssets { get; set; } = true;
}
