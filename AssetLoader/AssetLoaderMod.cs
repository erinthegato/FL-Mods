using System.IO;
using FlashingLights.ModKit.Core;
using MelonLoader;
using UnityEngine;

namespace AssetLoader;

[ModKitManifest(
    Id = "fl-asset-loader",
    DisplayName = "FL Asset Loader",
    Version = "1.0.0",
    Author = "OPUser",
    License = "MIT",
    MinSdkVersion = "0.1.0",
    Category = "Utility")]
public sealed class AssetLoaderMod : ModKitMelonMod<AssetLoaderConfig>
{
    protected override string ModId => "fl-asset-loader";
    protected override bool EnableConfigHotReload => true;
    protected override TimeSpan ConfigReloadInterval => TimeSpan.FromSeconds(1);
    private readonly List<AssetBundle> _bundles = new();
    private readonly List<GameObject> _instances = new();
    private string _assetRoot = "";

    protected override void OnModKitInitialized()
    {
        _assetRoot = Path.Combine(FindGameRoot(), "UserData", "FLMods", "Assets");
        Directory.CreateDirectory(_assetRoot);

        if (Config.Enabled && Config.AutoLoadOnStartup && PerformanceSettings.Current.AssetAutoloadAllowed)
            ReloadBundles();
        else if (Config.Enabled && Config.AutoLoadOnStartup)
            MelonLogger.Msg("[AssetLoader] Startup autoload skipped by PerformanceMode.json.");
    }

    protected override void OnModKitUpdate()
    {
        if (!Config.Enabled) return;
        if (Input.GetKeyDown(Config.ReloadKey))
            ReloadBundles();
    }

    protected override void OnModKitDisabled()
    {
        UnloadBundles();
    }

    private void ReloadBundles()
    {
        UnloadBundles();

        var files = Directory.GetFiles(_assetRoot)
            .Where(IsBundleCandidate)
            .OrderBy(f => f)
            .ToArray();

        int loaded = 0;
        int instantiated = 0;
        foreach (string file in files)
        {
            try
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                {
                    MelonLogger.Warning($"[AssetLoader] Could not load bundle: {Path.GetFileName(file)}");
                    continue;
                }

                _bundles.Add(bundle);
                loaded++;

                if (Config.InstantiatePrefabAssets)
                    instantiated += InstantiatePrefabs(bundle);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AssetLoader] Bundle error for {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        MelonLogger.Msg($"[AssetLoader] Loaded {loaded} bundle(s), instantiated {instantiated} prefab(s) from {_assetRoot}");
    }

    private int InstantiatePrefabs(AssetBundle bundle)
    {
        int count = 0;
        foreach (string assetName in bundle.GetAllAssetNames())
        {
            GameObject? prefab = null;
            try
            {
                prefab = bundle.LoadAsset<GameObject>(assetName);
            }
            catch { }

            if (prefab == null) continue;

            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab);
                instance.name = $"FLAsset_{Path.GetFileNameWithoutExtension(assetName)}";
                UnityEngine.Object.DontDestroyOnLoad(instance);
                _instances.Add(instance);
                count++;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AssetLoader] Instantiate failed for {assetName}: {ex.Message}");
            }
        }

        return count;
    }

    private void UnloadBundles()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            try
            {
                if (_instances[i] != null)
                    UnityEngine.Object.Destroy(_instances[i]);
            }
            catch { }
        }
        _instances.Clear();

        foreach (var bundle in _bundles)
        {
            try { bundle.Unload(false); }
            catch { }
        }
        _bundles.Clear();
    }

    private static bool IsBundleCandidate(string path)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string FindGameRoot()
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(AssetLoaderMod).Assembly.Location) ?? ".");
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "flashinglights.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }

        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(AssetLoaderMod).Assembly.Location) ?? ".",
            ".."));
    }
}
