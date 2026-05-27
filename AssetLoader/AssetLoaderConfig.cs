using FlashingLights.ModKit.Core;
using UnityEngine;

namespace AssetLoader;

public sealed class AssetLoaderConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoLoadOnStartup { get; set; } = true;
    public bool InstantiatePrefabAssets { get; set; } = true;

    [ModKitConfigDisplay("Reload Asset Bundles Key")]
    public KeyCode ReloadKey { get; set; } = KeyCode.F7;
}
