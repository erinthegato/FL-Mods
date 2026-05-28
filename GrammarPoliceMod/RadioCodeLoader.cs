using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace GrammarPoliceMod;

public record RadioCode(string Code, string Description, string Section, List<string> VoiceTriggers);

public static class RadioCodeLoader
{
    private static readonly string ConfigPath = Path.Combine(
        Path.GetDirectoryName(typeof(GrammarPoliceMod).Assembly.Location) ?? ".",
        "GrammarPolice", "RadioCodes.json");

    public static List<RadioCode> CurrentCodes { get; set; } = new();

    public static List<RadioCode> Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var defaults = GetDefaultCodes();
                var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                CurrentCodes = defaults;
                return defaults;
            }
            var text = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<List<RadioCode>>(text) ?? new List<RadioCode>();
            CurrentCodes = loaded;
            return loaded;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to load radio codes: {ex.Message}");
            var fallback = GetDefaultCodes();
            CurrentCodes = fallback;
            return fallback;
        }
    }

    private static List<RadioCode> GetDefaultCodes()
    {
        return new List<RadioCode>
        {
            new("10-1", "Unable to Copy — Signal Weak", "STATUS & AVAILABILITY", new List<string>{"ten one", "signal weak"}),
            new("10-2", "Receiving Well", "STATUS & AVAILABILITY", new List<string>{"ten two"}),
            new("10-3", "Stop Transmitting", "STATUS & AVAILABILITY", new List<string>{"ten three"}),
            new("10-4", "Acknowledged", "STATUS & AVAILABILITY", new List<string>{"ten four", "acknowledged"}),
            new("10-5", "Relay", "STATUS & AVAILABILITY", new List<string>{"ten five"}),
            new("10-6", "Busy — Stand By", "STATUS & AVAILABILITY", new List<string>{"ten six", "busy", "stand by"}),
            new("10-7", "Out of Service", "STATUS & AVAILABILITY", new List<string>{"ten seven", "out of service"}),
            new("10-8", "In Service", "STATUS & AVAILABILITY", new List<string>{"ten eight", "in service"}),
            new("10-9", "Say Again", "STATUS & AVAILABILITY", new List<string>{"ten nine", "repeat"}),
            new("10-10", "Fight in Progress", "ENFORCEMENT", new List<string>{"ten ten"}),
            new("10-11", "Animal Problem", "ENFORCEMENT", new List<string>{"ten eleven"}),
            new("10-15", "Prisoner in Custody", "ENFORCEMENT", new List<string>{"ten fifteen", "subject in custody"}),
            new("10-16", "Pick Up for Questioning", "ENFORCEMENT", new List<string>{"ten sixteen"}),
            new("10-22", "Disregard", "ENFORCEMENT", new List<string>{"ten twenty two", "disregard"}),
            new("10-26", "Detaining Subject", "ENFORCEMENT", new List<string>{"ten twenty six"}),
            new("10-31", "Crime in Progress", "ENFORCEMENT", new List<string>{"ten thirty one"}),
            new("10-32", "Man with Gun / Shots Fired", "ENFORCEMENT", new List<string>{"ten thirty two", "shots fired"}),
            new("10-33", "Emergency — All Units Stand By", "ENFORCEMENT", new List<string>{"ten thirty three"}),
            new("10-35", "Major Crime Alert", "ENFORCEMENT", new List<string>{"ten thirty five"}),
            new("10-50", "Traffic Stop / Accident", "ENFORCEMENT", new List<string>{"ten fifty", "traffic stop"}),
            new("10-54", "Hit and Run", "ENFORCEMENT", new List<string>{"ten fifty four"}),
            new("10-55", "Stolen Vehicle", "ENFORCEMENT", new List<string>{"ten fifty five", "stolen vehicle"}),
            new("10-56", "Intoxicated Driver", "ENFORCEMENT", new List<string>{"ten fifty six"}),
            new("10-57", "Reckless Driving", "ENFORCEMENT", new List<string>{"ten fifty seven"}),
            new("10-78", "Officer Needs Assistance", "ENFORCEMENT", new List<string>{"ten seventy eight", "officer needs assistance"}),
            new("10-80", "Pursuit in Progress", "ENFORCEMENT", new List<string>{"ten eighty", "pursuit"}),
            new("10-89", "Bomb Threat", "ENFORCEMENT", new List<string>{"ten eighty nine"}),
            new("10-90", "Bank Alarm", "ENFORCEMENT", new List<string>{"ten ninety"}),
            new("10-99", "Wanted / Stolen Record", "ENFORCEMENT", new List<string>{"ten ninety nine", "wanted person"}),
            new("10-20", "Requesting Location", "DISPATCHER", new List<string>{"ten twenty"}),
            new("10-21", "Call by Telephone", "DISPATCHER", new List<string>{"ten twenty one"}),
            new("10-23", "Arrived on Scene", "DISPATCHER", new List<string>{"ten twenty three", "arriving on scene", "arrived on scene"}),
            new("10-24", "Assignment Complete", "DISPATCHER", new List<string>{"ten twenty four", "assignment complete"}),
            new("10-25", "Meet With", "DISPATCHER", new List<string>{"ten twenty five"}),
            new("10-27", "Drivers License Check", "DISPATCHER", new List<string>{"ten twenty seven"}),
            new("10-28", "Vehicle Registration Check", "DISPATCHER", new List<string>{"ten twenty eight"}),
            new("10-29", "Check for Wants / Warrants", "DISPATCHER", new List<string>{"ten twenty nine"}),
            new("10-76", "En Route", "DISPATCHER", new List<string>{"ten seventy six", "en route"}),
            new("10-97", "Arrived on Scene", "DISPATCHER", new List<string>{"ten ninety seven"}),
            new("10-98", "Assignment Complete", "DISPATCHER", new List<string>{"ten ninety eight", "assignment complete"}),
            new("10-12", "Stand By", "EMERGENCY", new List<string>{"ten twelve"}),
            new("10-13", "Weather / Road Report", "EMERGENCY", new List<string>{"ten thirteen"}),
            new("10-14", "Escort", "EMERGENCY", new List<string>{"ten fourteen"}),
            new("10-17", "Meet Complainant", "EMERGENCY", new List<string>{"ten seventeen"}),
            new("10-18", "Complete Quickly", "EMERGENCY", new List<string>{"ten eighteen"}),
            new("10-19", "Return to Station", "EMERGENCY", new List<string>{"ten nineteen"}),
            new("10-30", "Unnecessary Use of Radio", "EMERGENCY", new List<string>{"ten thirty"}),
            new("10-34", "Riot", "EMERGENCY", new List<string>{"ten thirty four"}),
            new("10-51", "Wrecker Needed", "EMERGENCY", new List<string>{"ten fifty one"}),
            new("10-52", "Ambulance Needed", "EMERGENCY", new List<string>{"ten fifty two"}),
            new("10-53", "Road Blocked", "EMERGENCY", new List<string>{"ten fifty three"}),
            new("10-79", "Notify Coroner", "EMERGENCY", new List<string>{"ten seventy nine"}),
            new("Code 2", "Request Backup (Non-Emergency)", "BACKUP CODES", new List<string>{"code two"}),
            new("Code 3", "Request Backup (Emergency)", "BACKUP CODES", new List<string>{"code three", "request backup"}),
            new("Code 4", "All Clear — Situation Resolved", "BACKUP CODES", new List<string>{"code four", "all clear"}),
            new("Cancel Backup", "Cancel Backup Request", "BACKUP CODES", new List<string>{"cancel backup"}),
            new("Radio Check", "Communications Check — OK", "RADIO CHECK", new List<string>{"radio check"}),
            new("Signal 1", "Minor Incident Reported", "RADIO CHECK", new List<string>{"signal one"}),
            new("Signal 2", "Major Incident Reported", "RADIO CHECK", new List<string>{"signal two"})
        };
    }
}