namespace MDTMod;

public sealed record CourtQuestion(string Prompt, string[] Options, int CorrectIndex, string Explanation);

public sealed record CourtGameResult(int Score, int Total, int ReputationDelta, string VerdictModifier);

public sealed class CourtMiniGame
{
    private readonly List<CourtQuestion> _questions = new();
    private int _currentIndex;
    private int _score;
    private bool _answered;
    private int _selectedAnswer;
    private bool _finished;

    public IReadOnlyList<CourtQuestion> Questions => _questions;
    public int CurrentIndex => _currentIndex;
    public int Score => _score;
    public int Total => _questions.Count;
    public bool IsAnswered => _answered;
    public int SelectedAnswer => _selectedAnswer;
    public bool IsFinished => _finished;
    public CourtQuestion? Current => _currentIndex < _questions.Count ? _questions[_currentIndex] : null;

    public bool HasQuestions => _questions.Count > 0;

    public void GenerateFromArrest(ArrestRecord arrest)
    {
        _questions.Clear();
        _currentIndex = 0;
        _score = 0;
        _answered = false;
        _finished = false;

        var rng = new Random();
        var charges = NPCDataStore.GetChargesForSubject(arrest.SubjectName)
            .Where(c => arrest.ChargeIds.Contains(c.Id))
            .ToList();

        if (charges.Count > 0)
        {
            var ch = charges[rng.Next(charges.Count)];
            var decoys = USCharges.All
                .Where(c => c.Statute != ch.Charge.Statute)
                .OrderBy(_ => rng.Next())
                .Take(3)
                .Select(c => c.Name)
                .ToList();
            decoys.Insert(rng.Next(4), ch.Charge.Name);
            _questions.Add(new CourtQuestion(
                $"What charge was filed against {arrest.SubjectName}?",
                decoys.ToArray(),
                decoys.IndexOf(ch.Charge.Name),
                $"Charge: {ch.Charge.Name} — Statute {ch.Charge.Statute} [{ch.Charge.Class}]"));
        }

        if (!string.IsNullOrWhiteSpace(arrest.Location))
        {
            var allStreets = new[] { "suburbs west", "suburbs east", "route 600", "interstate 69", "cod town",
                                     "route 20", "beach town", "port", "city", "city marina", "unknown" };
            var streetDecoys = allStreets.Where(s => s != arrest.Location).OrderBy(_ => rng.Next()).Take(3).ToList();
            streetDecoys.Insert(rng.Next(4), arrest.Location);
            _questions.Add(new CourtQuestion(
                $"Where did the arrest of {arrest.SubjectName} occur?",
                streetDecoys.ToArray(),
                streetDecoys.IndexOf(arrest.Location),
                $"Location: {arrest.Location}"));
        }

        if (!string.IsNullOrWhiteSpace(arrest.NatureOfArrest))
        {
            var natureOptions = new[] { arrest.NatureOfArrest, "Traffic violation", "Warrant service", "Civil matter" };
            natureOptions = natureOptions.OrderBy(_ => rng.Next()).ToArray();
            _questions.Add(new CourtQuestion(
                $"What was the nature of the arrest for {arrest.SubjectName}?",
                natureOptions,
                Array.IndexOf(natureOptions, arrest.NatureOfArrest),
                $"Nature: {arrest.NatureOfArrest}"));
        }

        if (arrest.Witnesses.Count > 0)
        {
            var w = arrest.Witnesses[rng.Next(arrest.Witnesses.Count)];
            var fakeStatements = new[] { "I didn't see anything.", "He was running away.", "She was arguing loudly.",
                                         "They were parked illegally.", "I heard a loud noise." }
                .Where(s => s != w.Statement)
                .OrderBy(_ => rng.Next())
                .Take(3)
                .ToList();
            var allStmts = new List<string>(fakeStatements) { w.Statement };
            allStmts = allStmts.OrderBy(_ => rng.Next()).ToList();
            _questions.Add(new CourtQuestion(
                $"What did witness {w.Name} state?",
                allStmts.ToArray(),
                allStmts.IndexOf(w.Statement),
                $"Witness {w.Name} stated: \"{w.Statement}\""));
        }

        if (!string.IsNullOrWhiteSpace(arrest.LicensePlate))
        {
            var plateDecoys = new[] { "ABC-123", "XYZ-789", "DEF-456", "GHI-012", "JKL-345", "MNO-678" }
                .Where(p => p != arrest.LicensePlate)
                .OrderBy(_ => rng.Next())
                .Take(3)
                .ToList();
            plateDecoys.Insert(rng.Next(4), arrest.LicensePlate);
            _questions.Add(new CourtQuestion(
                $"What is the license plate of {arrest.SubjectName}?",
                plateDecoys.ToArray(),
                plateDecoys.IndexOf(arrest.LicensePlate),
                $"Plate: {arrest.LicensePlate}"));
        }
    }

    public bool Answer(int selectedIndex)
    {
        if (_answered || _finished || Current == null) return false;
        _answered = true;
        _selectedAnswer = selectedIndex;
        if (selectedIndex == Current.CorrectIndex)
            _score++;
        return selectedIndex == Current.CorrectIndex;
    }

    public bool NextQuestion()
    {
        _answered = false;
        _selectedAnswer = -1;
        _currentIndex++;
        if (_currentIndex >= _questions.Count)
        {
            _finished = true;
            return false;
        }
        return true;
    }

    public CourtGameResult GetResult(string officer)
    {
        int pct = _questions.Count > 0 ? _score * 100 / _questions.Count : 0;
        int repDelta = pct >= 80 ? 25 : pct >= 60 ? 10 : pct >= 40 ? 0 : -15;
        string modifier = pct >= 80 ? "favorable" : pct >= 60 ? "neutral" : "unfavorable";
        return new CourtGameResult(_score, _questions.Count, repDelta, modifier);
    }

    public void ApplyVerdict(string officer, List<string> chargeIds)
    {
        var result = GetResult(officer);
        NPCDataStore.AddReputation(officer, result.ReputationDelta);

        foreach (var chargeId in chargeIds)
        {
            var charge = NPCDataStore.FindCharge(chargeId);
            if (charge == null || charge.Verdict != null) continue;

            var rng = new Random();
            double convictionBonus = result.VerdictModifier switch
            {
                "favorable" => 0.30,
                "unfavorable" => -0.30,
                _ => 0.0
            };
            double roll = rng.NextDouble() + convictionBonus;

            CourtVerdict verdict;
            if (roll < 0.55)
            {
                int fine = rng.Next(charge.Charge.FineMin, charge.Charge.FineMax + 1);
                int jail = rng.Next(charge.Charge.JailDaysMin, charge.Charge.JailDaysMax + 1);
                verdict = new CourtVerdict("Guilty", fine, jail, "", DateTime.Now);
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
            }
            else
            {
                verdict = new CourtVerdict("Not Guilty", 0, 0, "", DateTime.Now);
            }

            NPCDataStore.ApplyVerdict(charge, verdict);
        }
    }
}
