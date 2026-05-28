using FlashingLights.ModKit.Core;
using UnityEngine;

namespace GameEventLogger;

public sealed class LoggerConfig
{
    public bool Enabled { get; set; } = true;

    [ModKitConfigDisplay("Panic Alarm on Weapon Draw")]
    public bool PanicAlarmEnabled { get; set; } = true;

    [ModKitConfigDisplay("Panic Alarm Duration (seconds)")]
    public float PanicDuration { get; set; } = 5f;

    [ModKitConfigDisplay("Shots before panic triggers")]
    public int ShotsToPanic { get; set; } = 3;

    [ModKitConfigDisplay("Weapon Poll Interval Seconds")]
    [ModKitConfigRange(0.25, 5.0, 0.25)]
    public float WeaponPollIntervalSeconds { get; set; } = 1.0f;

    [ModKitConfigDisplay("Weapon Cache Refresh Seconds")]
    [ModKitConfigRange(5, 60, 1)]
    public float WeaponCacheRefreshSeconds { get; set; } = 20f;
}
