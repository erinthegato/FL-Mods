using FlashingLights.ModKit.Core;
using UnityEngine;

namespace BodyCamOverlay;

public sealed class BodyCamConfig
{
    public bool Enabled { get; set; } = true;
    public bool TriggerOnWeaponDraw { get; set; } = true;

    [ModKitConfigDisplay("Toggle Bodycam Overlay Key")]
    public KeyCode ToggleKey { get; set; } = KeyCode.F8;

    [ModKitConfigDisplay("Bodycam Emergency Trigger Key")]
    public KeyCode EmergencyTriggerKey { get; set; } = KeyCode.Alpha2;

    [ModKitConfigDisplay("Emergency Trigger Press Count")]
    [ModKitConfigRange(1, 10, 1)]
    public int EmergencyTriggerPressCount { get; set; } = 3;

    [ModKitConfigDisplay("Emergency Trigger Window Seconds")]
    [ModKitConfigRange(0.5, 5.0, 0.25)]
    public float EmergencyTriggerWindowSeconds { get; set; } = 2f;

    [ModKitConfigDisplay("Signal Repeat Interval Seconds")]
    [ModKitConfigRange(10, 600, 5)]
    public float SignalIntervalSeconds { get; set; } = 90f;

    public float Volume { get; set; } = 1f;
    public string Agency { get; set; } = "FLASHING LIGHTS POLICE";
    public string OfficerName { get; set; } = "OFFICER";
    public string UnitId { get; set; } = "UNIT 001";
    public string CameraId { get; set; } = "AXON BODY 4";
}
