namespace N56Precision.Backend;

public sealed class ContactMetric(int width, int height)
{
    public double Distance(Contact c, (int X, int Y) origin) => Math.Sqrt(
        Math.Pow((c.X - origin.X) * 107.2 / width, 2) + Math.Pow((c.Y - origin.Y) * 64.3 / height, 2));
}

public sealed class TapAttempt
{
    public int Id { get; set; }
    public int Anchor { get; set; }
    public int Tapping { get; set; }
    public double Start { get; set; }
    public double HoldMs { get; set; }
    public double TapMs { get; set; }
    public double MotionMm { get; set; }
    public bool Finished { get; set; }
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "Tap in progress";
    public double MinimumHoldMs { get; set; }
    public double MaximumTapMs { get; set; }
    public double SlopMm { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, (int X, int Y)> Origins { get; set; } = new();
}

public sealed class ChordRightClick(int width, int height)
{
    private readonly ContactMetric metric = new(width, height);
    private Dictionary<int, double> downSince = new();
    private Dictionary<int, Contact> previous = new();
    private Candidate? candidate;
    private int sequence;
    private sealed record Candidate(int Anchor, int Tapping, double Time, Dictionary<int, (int X, int Y)> Origins) { public bool Valid = true; }
    public TapAttempt? Attempt { get; private set; }
    public object Snapshot(double now, Settings config)
    {
        double hold = previous.Count == 1 ? (now - downSince.Values.First()) * 1000 : 0;
        return new { liveHoldMs = Math.Max(0, hold), ready = hold >= config.RightClickHoldMs, enabled = config.ChordRightClickEnabled, attempt = Attempt };
    }
    private void Observe(Dictionary<int, Contact> active, double now, Settings config, bool buttonsDown)
    {
        var added = active.Keys.Except(previous.Keys).ToArray();
        if (active.Count == 2 && added.Length > 0 && (Attempt == null || Attempt.Finished))
        {
            int anchor = previous.Count == 1 ? previous.Keys.First() : active.Keys.First();
            if (!active.ContainsKey(anchor)) anchor = active.Keys.First();
            double hold = (now - downSince.GetValueOrDefault(anchor, now)) * 1000;
            Attempt = new TapAttempt { Id = ++sequence, Anchor = anchor, Tapping = active.Keys.First(slot => slot != anchor), Start = now,
                HoldMs = Math.Max(0, hold), MinimumHoldMs = config.RightClickHoldMs, MaximumTapMs = config.RightClickTapMaxMs,
                SlopMm = config.RightClickSlopMm, Origins = active.ToDictionary(p => p.Key, p => (p.Value.X, p.Value.Y)) };
            if (!config.ChordRightClickEnabled) Attempt.Reason = "Feature disabled";
            else if (previous.Count != 1 || hold < config.RightClickHoldMs) Attempt.Reason = "First finger was not held long enough";
        }
        var attempt = Attempt;
        if (attempt == null || attempt.Finished) return;
        attempt.TapMs = Math.Max(0, (now - attempt.Start) * 1000);
        foreach (var (slot, origin) in attempt.Origins)
            if (active.TryGetValue(slot, out var c)) attempt.MotionMm = Math.Max(attempt.MotionMm, metric.Distance(c, origin));
        if (buttonsDown) attempt.Reason = "Physical mouse button held";
        else if (!active.ContainsKey(attempt.Anchor)) attempt.Reason = "First finger lifted";
        else if (active.Count > 2) attempt.Reason = "Extra finger detected";
        else if (attempt.MotionMm > attempt.SlopMm) attempt.Reason = "Too much movement";
        else if (attempt.TapMs > attempt.MaximumTapMs) attempt.Reason = "Second finger held too long";
        if (!active.ContainsKey(attempt.Tapping) || !active.ContainsKey(attempt.Anchor) || active.Count > 2 || buttonsDown) attempt.Finished = true;
    }
    public bool Update(IEnumerable<Contact> contacts, double now, Settings config, bool buttonsDown = false)
    {
        var active = contacts.Where(c => c.Active).ToDictionary(c => c.Slot);
        Observe(active, now, config, buttonsDown);
        bool click = false;
        if (buttonsDown) candidate = null;
        if (candidate is Candidate c)
        {
            double elapsed = (now - c.Time) * 1000;
            if (!active.ContainsKey(c.Tapping))
            {
                click = c.Valid && active.Count == 1 && active.ContainsKey(c.Anchor) && elapsed > 0 && elapsed <= config.RightClickTapMaxMs;
                candidate = null;
            }
            else if (active.Count != 2 || !active.ContainsKey(c.Anchor)) candidate = null;
            else if (elapsed > config.RightClickTapMaxMs || active.Any(p => metric.Distance(p.Value, c.Origins[p.Key]) > config.RightClickSlopMm)) c.Valid = false;
        }
        var added = active.Keys.Except(previous.Keys).ToArray();
        if (!buttonsDown && config.ChordRightClickEnabled && candidate == null && previous.Count == 1 && active.Count == 2 && added.Length == 1)
        {
            int anchor = previous.Keys.First();
            if (active.ContainsKey(anchor) && (now - downSince[anchor]) * 1000 >= config.RightClickHoldMs)
                candidate = new Candidate(anchor, added[0], now, active.ToDictionary(p => p.Key, p => (p.Value.X, p.Value.Y)));
        }
        downSince = active.Keys.ToDictionary(slot => slot, slot => downSince.GetValueOrDefault(slot, now)); previous = active;
        if (!config.ChordRightClickEnabled) { click = false; candidate = null; }
        if (click && Attempt != null) { Attempt.Accepted = true; Attempt.Finished = true; Attempt.Reason = "Right-click accepted"; }
        else if (Attempt is { Finished: true, Reason: "Tap in progress" }) Attempt.Reason = "Tap rejected";
        return click;
    }
}
