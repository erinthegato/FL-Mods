using System;
using System.Linq;

namespace MDTMod;

public static class MDTCourt
{
    public static event Action<string>? CourtLog;

    public static void RunSession()
    {
        var pending = NPCDataStore.GetPendingCharges();
        if (pending.Count == 0)
        {
            CourtLog?.Invoke("[Court] No pending charges to adjudicate.");
            return;
        }

        var rng = new Random();
        int convicted = 0, dismissed = 0, plea = 0;

        foreach (var charge in pending)
        {
            double roll = rng.NextDouble();
            CourtVerdict verdict;

            if (roll < 0.55)
            {
                int fine = rng.Next(charge.Charge.FineMin, charge.Charge.FineMax + 1);
                int jail = rng.Next(charge.Charge.JailDaysMin, charge.Charge.JailDaysMax + 1);
                verdict = new CourtVerdict("Guilty", fine, jail, "", DateTime.Now);
                convicted++;
            }
            else if (roll < 0.75)
            {
                var reduced = USCharges.All
                    .Where(c => c.Class < charge.Charge.Class && c.Category == charge.Charge.Category)
                    .OrderBy(_ => rng.Next())
                    .FirstOrDefault();
                int fine = reduced != null
                    ? rng.Next(reduced.FineMin, reduced.FineMax + 1)
                    : rng.Next(50, 200);
                verdict = new CourtVerdict("Plea Bargain", fine, 0,
                    reduced?.Name ?? "Reduced Charge", DateTime.Now);
                plea++;
            }
            else
            {
                verdict = new CourtVerdict("Not Guilty", 0, 0, "", DateTime.Now);
                dismissed++;
            }

            NPCDataStore.ApplyVerdict(charge, verdict);
            string line = $"[Court] {charge.SubjectName} — {charge.Charge.Name}: " +
                          $"{verdict.Outcome}" +
                          (verdict.Fine > 0 ? $" ${verdict.Fine}" : "") +
                          (verdict.JailDays > 0 ? $" {verdict.JailDays}d" : "") +
                          (!string.IsNullOrEmpty(verdict.ReducedTo) ? $" (reduced to: {verdict.ReducedTo})" : "");
            CourtLog?.Invoke(line);
        }

        CourtLog?.Invoke($"[Court] Session complete: {convicted} convicted, {plea} plea bargains, {dismissed} dismissed.");
    }
}
