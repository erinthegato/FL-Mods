using System.Collections.Generic;

namespace MDTMod;

public enum ChargeCategory { Traffic, Registration, Criminal, Drug, Weapon, PublicOrder }
public enum ChargeClass { Infraction, Misdemeanor, Felony }

public sealed record USCharge(
    string Name,
    string Statute,
    ChargeCategory Category,
    ChargeClass Class,
    int FineMin,
    int FineMax,
    int JailDaysMin,
    int JailDaysMax
);

public static class USCharges
{
    public static readonly List<USCharge> All = new()
    {
        new("Speeding (1-15 over)",        "T-101-1", ChargeCategory.Traffic,     ChargeClass.Infraction,    50,   150,   0, 0),
        new("Speeding (16-25 over)",       "T-101-2", ChargeCategory.Traffic,     ChargeClass.Infraction,   100,   300,   0, 0),
        new("Speeding (26+ over)",         "T-101-3", ChargeCategory.Traffic,     ChargeClass.Misdemeanor,  200,   500,   0, 30),
        new("Reckless Driving",            "T-102",   ChargeCategory.Traffic,     ChargeClass.Misdemeanor,  250,  1000,   0, 90),
        new("Failure to Obey Traffic Control", "T-103", ChargeCategory.Traffic,  ChargeClass.Infraction,    50,   200,   0, 0),
        new("Failure to Yield",            "T-104",   ChargeCategory.Traffic,     ChargeClass.Infraction,    50,   150,   0, 0),
        new("Improper Lane Change",        "T-105",   ChargeCategory.Traffic,     ChargeClass.Infraction,    50,   150,   0, 0),
        new("Following Too Closely",       "T-106",   ChargeCategory.Traffic,     ChargeClass.Infraction,    50,   200,   0, 0),
        new("Texting While Driving",       "T-107",   ChargeCategory.Traffic,     ChargeClass.Misdemeanor,  100,   500,   0, 0),
        new("Running Red Light",           "T-108",   ChargeCategory.Traffic,     ChargeClass.Infraction,   100,   250,   0, 0),
        new("Running Stop Sign",           "T-109",   ChargeCategory.Traffic,     ChargeClass.Infraction,    75,   200,   0, 0),
        new("Illegal U-Turn",              "T-110",   ChargeCategory.Traffic,     ChargeClass.Infraction,    50,   150,   0, 0),
        new("Driving Wrong Way",           "T-111",   ChargeCategory.Traffic,     ChargeClass.Misdemeanor,  200,   750,   0, 60),
        new("Driving Without Headlights",  "T-112",   ChargeCategory.Traffic,     ChargeClass.Infraction,    30,   100,   0, 0),
        new("Expired Registration",        "R-201",   ChargeCategory.Registration, ChargeClass.Infraction,    50,   200,   0, 0),
        new("No Valid Registration",       "R-202",   ChargeCategory.Registration, ChargeClass.Infraction,   100,   500,   0, 0),
        new("Expired Inspection Sticker",  "R-203",   ChargeCategory.Registration, ChargeClass.Infraction,    25,   100,   0, 0),
        new("No Proof of Insurance",       "R-204",   ChargeCategory.Registration, ChargeClass.Infraction,   150,   500,   0, 0),
        new("Fraudulent Registration",     "R-205",   ChargeCategory.Registration, ChargeClass.Misdemeanor,  500,  2000,   0, 30),
        new("Assault",                     "C-301",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  500,  2500,   0, 365),
        new("Aggravated Assault",          "C-302",   ChargeCategory.Criminal,    ChargeClass.Felony,      2000, 10000, 365, 1825),
        new("Battery",                     "C-303",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  500,  2500,   0, 365),
        new("Aggravated Battery",          "C-304",   ChargeCategory.Criminal,    ChargeClass.Felony,      2000, 10000, 365, 1825),
        new("Robbery",                     "C-305",   ChargeCategory.Criminal,    ChargeClass.Felony,      3000, 15000, 365, 3650),
        new("Burglary",                    "C-306",   ChargeCategory.Criminal,    ChargeClass.Felony,      2000, 10000, 180, 1825),
        new("Grand Theft",                 "C-307",   ChargeCategory.Criminal,    ChargeClass.Felony,      1000, 10000, 180, 1825),
        new("Petit Theft",                 "C-308",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  200,  1000,   0, 180),
        new("Criminal Trespass",           "C-309",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  200,  1000,   0, 180),
        new("Criminal Mischief",           "C-310",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  250,  1500,   0, 365),
        new("Disorderly Conduct",          "C-311",   ChargeCategory.Criminal,    ChargeClass.Infraction,   100,   500,   0, 0),
        new("Resisting Arrest (No Violence)", "C-312", ChargeCategory.Criminal,  ChargeClass.Misdemeanor,  250,  1000,   0, 365),
        new("Resisting Arrest (Violence)", "C-313",   ChargeCategory.Criminal,    ChargeClass.Felony,      1000,  5000, 180, 1095),
        new("Domestic Battery",            "C-314",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  500,  2500,   0, 365),
        new("Loitering",                   "C-315",   ChargeCategory.Criminal,    ChargeClass.Infraction,    50,   250,   0, 0),
        new("Public Intoxication",         "C-316",   ChargeCategory.Criminal,    ChargeClass.Infraction,   100,   500,   0, 0),
        new("Vandalism",                   "C-317",   ChargeCategory.Criminal,    ChargeClass.Misdemeanor,  250,  2000,   0, 365),
        new("Possession of Controlled Substance", "D-401", ChargeCategory.Drug,  ChargeClass.Misdemeanor,  500,  2500,   0, 365),
        new("Possession with Intent to Distribute", "D-402", ChargeCategory.Drug, ChargeClass.Felony, 5000, 50000, 365, 3650),
        new("Drug Trafficking",            "D-403",   ChargeCategory.Drug,        ChargeClass.Felony,     10000,100000,1095, 7300),
        new("Possession of Paraphernalia", "D-404",   ChargeCategory.Drug,        ChargeClass.Infraction,   100,   500,   0, 0),
        new("DUI (First Offense)",         "D-405",   ChargeCategory.Drug,        ChargeClass.Misdemeanor,  500,  2000,   0, 180),
        new("DUI (Second Offense)",        "D-406",   ChargeCategory.Drug,        ChargeClass.Misdemeanor, 1000,  4000,  10, 365),
        new("Carrying Concealed Weapon",   "W-501",   ChargeCategory.Weapon,     ChargeClass.Felony,      2000, 10000, 365, 1825),
        new("Open Carry of Firearm",       "W-502",   ChargeCategory.Weapon,     ChargeClass.Misdemeanor,  500,  2000,   0, 365),
        new("Possession of Firearm by Felon", "W-503", ChargeCategory.Weapon,    ChargeClass.Felony,      5000, 25000, 365, 3650),
        new("Discharging Firearm in City", "W-504",   ChargeCategory.Weapon,     ChargeClass.Misdemeanor,  500,  2500,   0, 365),
    };

    public static List<USCharge> ByCategory(ChargeCategory cat) =>
        All.FindAll(c => c.Category == cat);
}
