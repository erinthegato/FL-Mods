using FlashingLights.ModKit.Core;
using UnityEngine;

namespace GameEventLogger;

public sealed class LoggerConfig
{
    public bool Enabled { get; set; } = true;

    [ModKitConfigDisplay("Log Scene Load/Unload")]
    public bool LogScenes { get; set; } = true;

    [ModKitConfigDisplay("Log Dispatch Calls")]
    public bool LogCalls { get; set; } = true;

    [ModKitConfigDisplay("Log Weapon State Changes")]
    public bool LogWeapons { get; set; } = true;

    [ModKitConfigDisplay("Log NPC Interactions")]
    public bool LogNpcInteractions { get; set; } = true;

    [ModKitConfigDisplay("Log NPC Name Generation")]
    public bool LogNpcNames { get; set; } = true;

    [ModKitConfigDisplay("Log Database Updates")]
    public bool LogDatabase { get; set; } = true;

    [ModKitConfigDisplay("Log Generation Events")]
    public bool LogGeneration { get; set; } = true;

    [ModKitConfigDisplay("Log Viewer Key (Delete to toggle)")]
    public KeyCode LogViewerKey { get; set; } = KeyCode.Delete;
}
