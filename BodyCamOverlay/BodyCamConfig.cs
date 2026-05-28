using FlashingLights.ModKit.Core;
using UnityEngine;

namespace BodyCamOverlay;

public sealed class BodyCamConfig
{
    public bool Enabled { get; set; } = true;
    public bool TriggerOnWeaponDraw { get; set; } = true;
    public bool DebugLogging { get; set; } = false;

    [ModKitConfigDisplay("Emergency Trigger Press Count")]
    [ModKitConfigRange(1, 10, 1)]
    public int EmergencyTriggerPressCount { get; set; } = 3;

    [ModKitConfigDisplay("Emergency Trigger Window Seconds")]
    [ModKitConfigRange(0.5, 5.0, 0.25)]
    public float EmergencyTriggerWindowSeconds { get; set; } = 2f;

    [ModKitConfigDisplay("Signal Repeat Interval Seconds")]
    [ModKitConfigRange(10, 600, 5)]
    public float SignalIntervalSeconds { get; set; } = 90f;

    [ModKitConfigDisplay("Weapon Poll Interval Seconds")]
    [ModKitConfigRange(0.25, 5.0, 0.25)]
    public float WeaponPollIntervalSeconds { get; set; } = 1.25f;

    [ModKitConfigDisplay("Weapon Cache Refresh Seconds")]
    [ModKitConfigRange(5, 60, 1)]
    public float WeaponCacheRefreshSeconds { get; set; } = 20f;

    public float Volume { get; set; } = 1f;
    public string Agency { get; set; } = "FLASHING LIGHTS POLICE";
    public string OfficerName { get; set; } = "OFFICER";
    public string UnitId { get; set; } = "UNIT 001";
    public string CameraId { get; set; } = "AXON BODY 4";
}
