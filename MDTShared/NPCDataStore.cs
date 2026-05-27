using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace MDTMod;

public sealed record Citation(
    string Id,
    string SubjectName,
    USCharge Charge,
    DateTime IssuedAt,
    string IssuingOfficer,
    string? SubjectRegistration = null
);

public sealed record NPCCharge(
    string Id,
    string SubjectName,
    USCharge Charge,
    DateTime FiledAt,
    string FiledBy,
    CourtVerdict? Verdict,
    string? SubjectRegistration = null
);

public sealed record CourtVerdict(
    string Outcome,         // Guilty / Not Guilty / Plea Bargain
    int Fine,
    int JailDays,
    string ReducedTo,
    DateTime RuledAt
);

public sealed record WitnessStatement(string Name, string Statement);

public sealed record NPCInfo(
    string Name,
    string Registration,
    bool HasInsurance,
    bool HasFirearmsLicense,
    bool IsWanted,
    bool IsMissing,
    DateTime DetectedAt
);

public sealed record ArrestRecord(
    string Id,
    string SubjectName,
    DateTime ArrestedAt,
    string Location,
    string NatureOfArrest,
    string DOB,
    string LicensePlate,
    List<WitnessStatement> Witnesses,
    string ArrestingOfficer,
    List<string> ChargeIds,
    string? SubjectRegistration = null
);

public static class NPCDataStore
{
    private static readonly List<Citation> Citations = new();
    private static readonly List<NPCCharge> Charges = new();
    private static readonly List<ArrestRecord> Arrests = new();
    private static readonly List<NPCInfo> NpcRecords = new();
    private static int _nextId;
    private static int _dataVersion;

    private static readonly string? _dataSavePath;
    private static readonly string? _photoSavePath;

    private static string NextId() => $"MDT-{++_nextId:D6}";

    public static int GetDataVersion() => _dataVersion;

    private sealed record DataSnapshot(
        List<Citation> Citations,
        List<NPCCharge> Charges,
        List<ArrestRecord> Arrests,
        List<NPCInfo> NpcRecords,
        int NextId
    );

    static NPCDataStore()
    {
        try
        {
            // When running in-game, this DLL is deployed to the game's `UserLibs` folder.
            // Using the executing assembly directory ensures we read/write the same
            // persisted `mdt_data.json` that the game/mod environment generates.
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            _dataSavePath = Path.Combine(dir, "mdt_data.json");
            _photoSavePath = Path.Combine(dir, "mdt_photos.json");
            LoadData();
            LoadPhotos();
        }
        catch { }
    }

    public static void SaveData()
    {
        _dataVersion++;
        if (_dataSavePath == null) return;
        try
        {
            var data = new DataSnapshot(Citations, Charges, Arrests, NpcRecords, _nextId);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataSavePath, json);
        }
        catch { }
    }

    public static void LoadData()
    {
        if (_dataSavePath == null || !File.Exists(_dataSavePath)) return;
        try
        {
            var json = File.ReadAllText(_dataSavePath);
            var data = JsonSerializer.Deserialize<DataSnapshot>(json);
            if (data == null) return;
            Citations.Clear();
            Citations.AddRange(data.Citations);
            Charges.Clear();
            Charges.AddRange(data.Charges);
            Arrests.Clear();
            Arrests.AddRange(data.Arrests);
            NpcRecords.Clear();
            NpcRecords.AddRange(data.NpcRecords);
            _nextId = data.NextId;
        }
        catch { }
    }

    public static Citation IssueCitation(string subject, USCharge charge, string officer, string? subjectRegistration = null)
    {
        var cit = new Citation(NextId(), subject, charge, DateTime.Now, officer, subjectRegistration);
        Citations.Add(cit);
        SaveData();
        return cit;
    }

    public static NPCCharge FileCharge(string subject, USCharge charge, string officer, string? subjectRegistration = null)
    {
        var nc = new NPCCharge(NextId(), subject, charge, DateTime.Now, officer, null, subjectRegistration);
        Charges.Add(nc);
        SaveData();
        return nc;
    }

    public static IReadOnlyList<Citation> GetCitations() => Citations.AsReadOnly();
    public static IReadOnlyList<NPCCharge> GetCharges() => Charges.AsReadOnly();

    public static List<NPCCharge> GetPendingCharges() =>
        Charges.FindAll(c => c.Verdict == null);

    public static List<NPCCharge> GetChargesForSubject(string name) =>
        Charges.FindAll(c =>
            IsSubjectMatch(c.SubjectName, c.SubjectRegistration, name));

    public static List<Citation> GetCitationsForSubject(string name) =>
        Citations.FindAll(c =>
            IsSubjectMatch(c.SubjectName, c.SubjectRegistration, name));

    public static void ApplyVerdict(NPCCharge charge, CourtVerdict verdict)
    {
        var idx = Charges.FindIndex(c => c.Id == charge.Id);
        if (idx >= 0)
        {
            Charges[idx] = charge with { Verdict = verdict };
            SaveData();
        }
    }

    public static string[] GetSubjectNames() =>
        Charges.Select(c => c.SubjectName)
               .Union(Citations.Select(c => c.SubjectName))
               .Union(Arrests.Select(a => a.SubjectName))
               .Union(NpcRecords.Select(n => n.Name))
               .Distinct()
               .OrderBy(n => n)
               .ToArray();

    public static ArrestRecord FileArrest(ArrestRecord arrest)
    {
        var newArrest = arrest with { Id = NextId() };
        Arrests.Add(newArrest);
        SaveData();
        return newArrest;
    }

    public static List<ArrestRecord> GetArrestsForSubject(string name) =>
        Arrests.FindAll(a =>
            IsSubjectMatch(a.SubjectName, a.SubjectRegistration, name));

    public static IReadOnlyList<ArrestRecord> GetAllArrests() => Arrests.AsReadOnly();

    public static bool UpdateCharge(string id, NPCCharge updated)
    {
        var idx = Charges.FindIndex(c => c.Id == id);
        if (idx < 0) return false;
        Charges[idx] = updated;
        SaveData();
        return true;
    }

    public static bool RemoveCharge(string id)
    {
        var removed = Charges.RemoveAll(c => c.Id == id);
        if (removed > 0) SaveData();
        return removed > 0;
    }

    public static NPCCharge? FindCharge(string id) =>
        Charges.Find(c => c.Id == id);

    public static bool UpdateCitation(string id, Citation updated)
    {
        var idx = Citations.FindIndex(c => c.Id == id);
        if (idx < 0) return false;
        Citations[idx] = updated;
        SaveData();
        return true;
    }

    public static bool RemoveCitation(string id)
    {
        var removed = Citations.RemoveAll(c => c.Id == id);
        if (removed > 0) SaveData();
        return removed > 0;
    }

    public static Citation? FindCitation(string id) =>
        Citations.Find(c => c.Id == id);

    public static bool UpdateArrest(string id, ArrestRecord updated)
    {
        var idx = Arrests.FindIndex(a => a.Id == id);
        if (idx < 0) return false;
        Arrests[idx] = updated;
        SaveData();
        return true;
    }

    public static bool RemoveArrest(string id)
    {
        var removed = Arrests.RemoveAll(a => a.Id == id);
        if (removed > 0) SaveData();
        return removed > 0;
    }

    public static ArrestRecord? FindArrest(string id) =>
        Arrests.Find(a => a.Id == id);

    // ── NPC Records ──

    public static void AddNpcRecord(NPCInfo info)
    {
        // Keep NPC records in sync across repeated scene scans by upserting on registration first,
        // then falling back to name when no registration is available.
        int idx = -1;
        if (!string.IsNullOrWhiteSpace(info.Registration))
        {
            idx = NpcRecords.FindIndex(r =>
                !string.IsNullOrWhiteSpace(r.Registration) &&
                r.Registration.Equals(info.Registration, StringComparison.OrdinalIgnoreCase));
        }

        if (idx < 0)
        {
            idx = NpcRecords.FindIndex(r =>
                r.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (idx >= 0)
            NpcRecords[idx] = info;
        else
            NpcRecords.Add(info);

        SaveData();
    }

    public static IReadOnlyList<NPCInfo> GetNpcRecords() => NpcRecords.AsReadOnly();

    public static List<NPCInfo> SearchNpcRecords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<NPCInfo>(NpcRecords);
        return NpcRecords.FindAll(r =>
            r.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static bool RemoveNpcRecord(string name)
    {
        int removed = NpcRecords.RemoveAll(r =>
            r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) SaveData();
        return removed > 0;
    }

    public static NPCInfo? FindNpcRecord(string name) =>
        NpcRecords.Find(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static NPCInfo? FindLatestNpcRecord(string name) =>
        NpcRecords
            .Where(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DetectedAt)
            .FirstOrDefault();

    private static readonly Dictionary<string, string> SubjectPhotos = new();

    public static void SetPhoto(string subject, string photoPath)
    {
        SubjectPhotos[subject] = photoPath;
        SavePhotos();
    }

    public static string? GetPhoto(string subject)
    {
        return SubjectPhotos.TryGetValue(subject, out var path) ? path : null;
    }

    public static void SavePhotos()
    {
        if (_photoSavePath == null) return;
        try
        {
            var json = JsonSerializer.Serialize(SubjectPhotos);
            File.WriteAllText(_photoSavePath, json);
        }
        catch { }
    }

    public static void LoadPhotos()
    {
        if (_photoSavePath == null || !File.Exists(_photoSavePath)) return;
        try
        {
            var json = File.ReadAllText(_photoSavePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded == null) return;
            SubjectPhotos.Clear();
            foreach (var kv in loaded)
                SubjectPhotos[kv.Key] = kv.Value;
        }
        catch { }
    }

    private static bool IsSubjectMatch(string subjectName, string? subjectRegistration, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        string trimmedQuery = query.Trim();
        return subjectName.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(subjectRegistration)
                   && subjectRegistration.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase));
    }
}
