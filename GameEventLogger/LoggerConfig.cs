using FlashingLights.ModKit.Core;
using UnityEngine;

namespace GameEventLogger;

public sealed class LoggerConfig
{
    public bool Enabled { get; set; } = true;

    [ModKitConfigDisplay("Panic Alarm on Weapon Draw")]
    public bool PanicAlarmEnabled { get; set; } = true;

    [ModKitConfigDisplay("Auto-Create MDT NPC Records")]
    public bool NpcRecordEnabled { get; set; } = true;

    [ModKitConfigDisplay("Randomize NPC Data When Unavailable")]
    public bool RandomizeNpcData { get; set; } = true;

    [ModKitConfigDisplay("Panic Alarm Duration (seconds)")]
    public float PanicDuration { get; set; } = 5f;

    [ModKitConfigDisplay("Shots before panic triggers")]
    public int ShotsToPanic { get; set; } = 3;
}
