using FlashingLights.ModKit.Core;
using UnityEngine;

namespace FLModkitBindings;

public sealed class FLModkitBindingsConfig
{
    [ModKitConfigDisplay("Asset Loader - Reload Assets")]
    public KeyCode AssetLoaderReloadKey { get; set; } = KeyCode.F7;

    [ModKitConfigDisplay("Background Radio - Toggle")]
    public KeyCode BackgroundRadioToggleKey { get; set; } = KeyCode.F10;

    [ModKitConfigDisplay("Background Radio - Up")]
    public KeyCode BackgroundRadioNavigateUpKey { get; set; } = KeyCode.UpArrow;

    [ModKitConfigDisplay("Background Radio - Down")]
    public KeyCode BackgroundRadioNavigateDownKey { get; set; } = KeyCode.DownArrow;

    [ModKitConfigDisplay("Background Radio - Select")]
    public KeyCode BackgroundRadioSelectKey { get; set; } = KeyCode.Return;

    [ModKitConfigDisplay("Background Radio - Stop")]
    public KeyCode BackgroundRadioStopKey { get; set; } = KeyCode.Space;

    [ModKitConfigDisplay("Bodycam - Toggle Overlay")]
    public KeyCode BodyCamToggleKey { get; set; } = KeyCode.F8;

    [ModKitConfigDisplay("Bodycam - Emergency Trigger")]
    public KeyCode BodyCamEmergencyTriggerKey { get; set; } = KeyCode.Alpha2;

    [ModKitConfigDisplay("Grammar Police - Push To Talk")]
    public KeyCode GrammarPolicePushToTalkKey { get; set; } = KeyCode.LeftControl;

    [ModKitConfigDisplay("Grammar Police - Radio UI")]
    public KeyCode GrammarPoliceRadioUIToggleKey { get; set; } = KeyCode.F12;

    [ModKitConfigDisplay("Grammar Police - Radio Up")]
    public KeyCode GrammarPoliceRadioNavigateUpKey { get; set; } = KeyCode.UpArrow;

    [ModKitConfigDisplay("Grammar Police - Radio Down")]
    public KeyCode GrammarPoliceRadioNavigateDownKey { get; set; } = KeyCode.DownArrow;

    [ModKitConfigDisplay("Grammar Police - Radio Select")]
    public KeyCode GrammarPoliceRadioSelectKey { get; set; } = KeyCode.Return;

    [ModKitConfigDisplay("Grammar Police - Panic")]
    public KeyCode GrammarPolicePanicTriggerKey { get; set; } = KeyCode.F;

    [ModKitConfigDisplay("MDT - Toggle")]
    public KeyCode MDTToggleKey { get; set; } = KeyCode.F11;

    [ModKitConfigDisplay("NPC AI - Toggle")]
    public KeyCode NPCAIToggleKey { get; set; } = KeyCode.F9;

    [ModKitConfigDisplay("NPC AI - Send")]
    public KeyCode NPCAISendKey { get; set; } = KeyCode.Return;
}
