using UnityEngine.SceneManagement;

namespace GameEventLogger;

internal static class ScenePatches
{
    public static void AfterSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameEventLoggerMod.WriteLog($"[Scene] Loaded: \"{scene.name}\" (index={scene.buildIndex}, mode={mode})");
    }

    public static void AfterSceneUnloaded(Scene scene)
    {
        GameEventLoggerMod.WriteLog($"[Scene] Unloaded: \"{scene.name}\" (index={scene.buildIndex})");
    }
}
