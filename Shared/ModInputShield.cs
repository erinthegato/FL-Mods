using HarmonyLib;
using UnityEngine;

namespace FLMods.Shared;

internal static class ModInputShield
{
    private static readonly HashSet<KeyCode> AllowedKeys = new();
    private static bool _blocked;

    internal static void SetBlocked(bool blocked, params KeyCode[] allowedKeys)
    {
        _blocked = blocked;
        AllowedKeys.Clear();
        foreach (var key in allowedKeys)
            if (key != KeyCode.None)
                AllowedKeys.Add(key);
    }

    private static bool ShouldBlockKey(KeyCode key) =>
        _blocked && !AllowedKeys.Contains(key);

    private static bool ShouldBlockMouse() => _blocked;

    private static bool ShouldBlockAxis(string axisName)
    {
        if (!_blocked || string.IsNullOrWhiteSpace(axisName)) return false;
        return axisName.Contains("mouse", StringComparison.OrdinalIgnoreCase) ||
               axisName.Contains("horizontal", StringComparison.OrdinalIgnoreCase) ||
               axisName.Contains("vertical", StringComparison.OrdinalIgnoreCase) ||
               axisName.Contains("fire", StringComparison.OrdinalIgnoreCase) ||
               axisName.Contains("aim", StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
    private static class GetKeyPatch
    {
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!ShouldBlockKey(key)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
    private static class GetKeyDownPatch
    {
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!ShouldBlockKey(key)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) })]
    private static class GetKeyUpPatch
    {
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!ShouldBlockKey(key)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), new[] { typeof(int) })]
    private static class GetMouseButtonPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ShouldBlockMouse()) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) })]
    private static class GetMouseButtonDownPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ShouldBlockMouse()) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), new[] { typeof(int) })]
    private static class GetMouseButtonUpPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ShouldBlockMouse()) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) })]
    private static class GetAxisPatch
    {
        private static bool Prefix(string axisName, ref float __result)
        {
            if (!ShouldBlockAxis(axisName)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) })]
    private static class GetAxisRawPatch
    {
        private static bool Prefix(string axisName, ref float __result)
        {
            if (!ShouldBlockAxis(axisName)) return true;
            __result = 0f;
            return false;
        }
    }
}
