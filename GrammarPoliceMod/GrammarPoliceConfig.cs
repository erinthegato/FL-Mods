using System.Collections.Generic;
using FlashingLights.ModKit.Core;

namespace GrammarPoliceMod;

public sealed class PanicProfile
{
    public bool Enabled { get; set; } = true;
    public string DispatchCode { get; set; } = "10-78";
    public string DispatchMessage { get; set; } = "Officer Needs Assistance — Panic Activated";
    public string AudioFile { get; set; } = "";
    public string KeySequence { get; set; } = "";
}

public sealed class GrammarPoliceConfig
{
    public bool Enabled { get; set; } = true;
    public bool EnableHotReload { get; set; } = true;
    public double HotReloadIntervalSeconds { get; set; } = 1;

    public bool VoiceRecognitionEnabled { get; set; } = true;

    [ModKitConfigDisplay("Confidence Threshold")]
    [ModKitConfigRange(0.1, 1.0, 0.05)]
    public double ConfidenceThreshold { get; set; } = 0.6;

    [ModKitConfigDisplay("Transmission Display Seconds")]
    [ModKitConfigRange(1, 15, 1)]
    public int OverlayDisplaySeconds { get; set; } = 4;

    public bool ShowTransmissionOverlay { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;

    [ModKitConfigDisplay("Auto-Dispatch Backup on Events")]
    public bool AutoDispatchBackup { get; set; } = true;

    public bool KeyEmulationEnabled { get; set; } = true;

    [ModKitConfigDisplay("10-1 Key Sequence (comma-separated, e.g. F,G)")]
    public string KeySequence_10_1 { get; set; } = "";
    [ModKitConfigDisplay("10-2 Key Sequence")] public string KeySequence_10_2 { get; set; } = "";
    [ModKitConfigDisplay("10-3 Key Sequence")] public string KeySequence_10_3 { get; set; } = "";
    [ModKitConfigDisplay("10-4 Key Sequence")] public string KeySequence_10_4 { get; set; } = "";
    [ModKitConfigDisplay("10-5 Key Sequence")] public string KeySequence_10_5 { get; set; } = "";
    [ModKitConfigDisplay("10-6 Key Sequence")] public string KeySequence_10_6 { get; set; } = "";
    [ModKitConfigDisplay("10-7 Key Sequence")] public string KeySequence_10_7 { get; set; } = "";
    [ModKitConfigDisplay("10-8 Key Sequence")] public string KeySequence_10_8 { get; set; } = "";
    [ModKitConfigDisplay("10-9 Key Sequence")] public string KeySequence_10_9 { get; set; } = "";
    [ModKitConfigDisplay("10-10 Key Sequence")] public string KeySequence_10_10 { get; set; } = "";

    public bool PanicEnabled { get; set; } = true;

    [ModKitConfigDisplay("Panic Press Count (minimum 3)")]
    [ModKitConfigRange(3, 10, 1)]
    public int PanicPressCount { get; set; } = 3;

    [ModKitConfigDisplay("Panic Time Window (seconds)")]
    [ModKitConfigRange(0.5, 3.0, 0.25)]
    public double PanicTimeWindow { get; set; } = 1.5;

    [ModKitConfigDisplay("Current Role (Law/Fire/EMS)")]
    public string CurrentRole { get; set; } = "Law";

    public Dictionary<string, PanicProfile> PanicProfiles { get; set; } = new()
    {
        ["Law"] = new PanicProfile { DispatchCode = "10-78", DispatchMessage = "Officer Needs Assistance" },
        ["Fire"] = new PanicProfile { DispatchCode = "10-78", DispatchMessage = "Firefighter in Distress" },
        ["EMS"] = new PanicProfile { DispatchCode = "10-52", DispatchMessage = "Ambulance Crew Needs Assistance" }
    };

    [ModKitConfigDisplay("Panic Dispatch Code (legacy, kept for compatibility)")]
    public string PanicDispatchCode { get; set; } = "10-78";

    [ModKitConfigDisplay("Panic Dispatch Message (legacy)")]
    public string PanicDispatchMessage { get; set; } = "Officer Needs Assistance — Panic Activated";

    [ModKitConfigDisplay("Panic Key Sequence (legacy)")]
    public string PanicKeySequence { get; set; } = "";

    public bool PanicAudioEnabled { get; set; } = true;

    [ModKitConfigDisplay("Panic Audio File (filename from DispatchAudio/Panic Button/)")]
    public string PanicAudioFile { get; set; } = "";
}
