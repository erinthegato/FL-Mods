using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace GameEventLogger;

internal static class PoolPatches
{
    public static void AfterActivatePool(string PMFCHPHHOHB, string GAJFBIGDHBL)
    {
        try
        {
            var mod = GameEventLoggerMod.Instance;
            var group = PMFCHPHHOHB ?? "?";
            var pool = GAJFBIGDHBL ?? "?";
            var tag = ClassifyPool(group, pool);

            GameEventLoggerMod.WriteLog($"[Pool] ACTIVATE group=\"{group}\" pool=\"{pool}\" tag={tag}");

            if (mod.LogCalls && tag == "CALL")
            {
                var key = $"{group}/{pool}";
                mod.ActiveCalls.Add(key);
                mod.CallSequence++;
                GameEventLoggerMod.WriteLog($"[Call] #{mod.CallSequence} DISPATCHED: {pool} (group={group}) — {mod.ActiveCalls.Count} active call(s)");
            }

            if (mod.LogWeapons && tag == "WEAPON")
            {
                GameEventLoggerMod.WriteLog($"[Weapon] Deployed: {pool} (group={group})");
            }

            if (tag == "NPC_NAME" && mod.LogNpcNames)
            {
                GameEventLoggerMod.WriteLog($"[NPC Name] Pool: \"{pool}\" (group={group})");
            }

            if (tag == "DATABASE" && mod.LogDatabase)
            {
                GameEventLoggerMod.WriteLog($"[Database] Pool: \"{pool}\" (group={group})");
            }

            if (tag == "GENERATION" && mod.LogGeneration)
            {
                GameEventLoggerMod.WriteLog($"[Generation] Pool: \"{pool}\" (group={group})");
            }

            if (tag == "EVENT_ID")
            {
                GameEventLoggerMod.WriteLog($"[Call ID / Event #] group=\"{group}\" pool=\"{pool}\"");
            }

            if (tag == "UNKNOWN" && (mod.LogCalls || mod.LogWeapons))
            {
                GameEventLoggerMod.WriteLog($"[Pool] (uncategorized) group=\"{group}\" pool=\"{pool}\"");
            }
        }
        catch
        {
        }
    }

    public static void AfterActivateGroup(string PMFCHPHHOHB)
    {
        try
        {
            GameEventLoggerMod.WriteLog($"[Pool] GROUP ACTIVATED: \"{PMFCHPHHOHB ?? "?"}\"");
        }
        catch
        {
        }
    }

    public static void AfterDeactivatePool(string PMFCHPHHOHB, string GAJFBIGDHBL)
    {
        try
        {
            var mod = GameEventLoggerMod.Instance;
            var group = PMFCHPHHOHB ?? "?";
            var pool = GAJFBIGDHBL ?? "?";
            var tag = ClassifyPool(group, pool);

            GameEventLoggerMod.WriteLog($"[Pool] DEACTIVATE group=\"{group}\" pool=\"{pool}\" tag={tag}");

            if (mod.LogCalls && tag == "CALL")
            {
                var key = $"{group}/{pool}";
                mod.ActiveCalls.Remove(key);

                if (mod.ActiveCalls.Count == 0)
                {
                    GameEventLoggerMod.WriteLog($"[Call] RESOLVED: {pool} (group={group}) — 0 call(s) remaining");
                    GameEventLoggerMod.WriteLog("[Call] ALL CALLS CLEARED");
                }
                else
                {
                    GameEventLoggerMod.WriteLog($"[Call] RESOLVED: {pool} (group={group}) — {mod.ActiveCalls.Count} call(s) remaining");
                    GameEventLoggerMod.WriteLog($"[Call] Still active: {string.Join(", ", mod.ActiveCalls)}");
                }
            }

            if (mod.LogWeapons && tag == "WEAPON")
            {
                GameEventLoggerMod.WriteLog($"[Weapon] Holstered: {pool} (group={group})");
            }

            if (tag == "NPC_NAME" && mod.LogNpcNames)
            {
                GameEventLoggerMod.WriteLog($"[NPC Name] Pool deactivated: \"{pool}\" (group={group})");
            }

            if (tag == "DATABASE" && mod.LogDatabase)
            {
                GameEventLoggerMod.WriteLog($"[Database] Pool deactivated: \"{pool}\" (group={group})");
            }

            if (tag == "GENERATION" && mod.LogGeneration)
            {
                GameEventLoggerMod.WriteLog($"[Generation] Pool deactivated: \"{pool}\" (group={group})");
            }

            if (tag == "EVENT_ID")
            {
                GameEventLoggerMod.WriteLog($"[Call ID / Event #] Deactivated: group=\"{group}\" pool=\"{pool}\"");
            }
        }
        catch
        {
        }
    }

    private static string ClassifyPool(string group, string pool)
    {
        // Call/dispatch patterns
        if (group.Contains("call", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("dispatch", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("incident", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("mission", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("signal", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("call", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("incident", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("signal", StringComparison.OrdinalIgnoreCase))
        {
            return "CALL";
        }

        // Weapon patterns
        if (group.Contains("weapon", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("firearm", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("gun", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("holster", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("arsenal", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("equipment", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("weapon", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("gun", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("rifle", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("pistol", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("shotgun", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("taser", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("bat", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("knife", StringComparison.OrdinalIgnoreCase))
        {
            return "WEAPON";
        }

        // NPC name generation patterns
        if (group.Contains("name", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("npcname", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("generatedname", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("name", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("npcname", StringComparison.OrdinalIgnoreCase))
        {
            return "NPC_NAME";
        }

        // Database/record patterns
        if (group.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("record", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("data", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("record", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("datafile", StringComparison.OrdinalIgnoreCase))
        {
            return "DATABASE";
        }

        // Generation patterns
        if (group.Contains("generation", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("spawner", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("populate", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("procgen", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("worldgen", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("generation", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("spawner", StringComparison.OrdinalIgnoreCase))
        {
            return "GENERATION";
        }

        // Call ID / Event # patterns
        if (group.Contains("call_id", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("callid", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("event_#", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("event#", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("event_id", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("call_id", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("callid", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("event_#", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("event#", StringComparison.OrdinalIgnoreCase) ||
            pool.Contains("event_id", StringComparison.OrdinalIgnoreCase))
        {
            return "EVENT_ID";
        }

        return "UNKNOWN";
    }
}
