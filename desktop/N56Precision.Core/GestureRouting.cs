namespace N56Precision.Backend;

public static class GestureOwnership
{
    public static Dictionary<int, uint> AsusValues(Settings config, bool buttonZoneActive = false) => Settings.Catalog.ToDictionary(
        g => g.Index, g => (uint)(g.Index != 5 && config.Asus[g.Key] && !config.Windows.GetValueOrDefault(g.Fingers.ToString()) &&
            !(buttonZoneActive && g.Fingers >= 2) ? 1 : 0));
}

public sealed class ButtonZone(int width, int height, bool invertY = false)
{
    public Dictionary<int, string> Reserved { get; private set; } = new();
    public HashSet<int> GestureSlots { get; } = new();
    public bool Occupied => Reserved.Count > 0;
    public void ProtectGesture(IEnumerable<Contact> contacts)
    {
        var active = contacts.Where(c => c.Active).ToArray();
        if (active.Length >= 2) GestureSlots.UnionWith(active.Where(c => !Reserved.ContainsKey(c.Slot)).Select(c => c.Slot));
    }
    public Contact[] Filter(IEnumerable<Contact> contacts, double percent, bool bottomAtLowY = false)
    {
        var raw = contacts.ToArray(); var active = raw.Where(c => c.Active).Select(c => c.Slot).ToHashSet();
        GestureSlots.IntersectWith(active);
        Reserved = Reserved.Where(p => active.Contains(p.Key)).ToDictionary();
        foreach (var c in raw)
        {
            if (!c.Active || percent == 0 || GestureSlots.Contains(c.Slot)) continue;
            double y = invertY != bottomAtLowY ? height - c.Y : c.Y;
            if (y >= height * (1 - percent / 100)) Reserved.TryAdd(c.Slot, c.X < width / 2.0 ? "left" : "right");
        }
        return raw.Where(c => !Reserved.ContainsKey(c.Slot)).ToArray();
    }
}

public sealed class Router
{
    public bool Blocked { get; private set; }
    public bool DragBlocked { get; private set; }
    public Contact[] Forward(IEnumerable<Contact> contacts, Settings config, int? physicalCount = null, bool buttonsDown = false, int buttonContactCount = 0)
    {
        var filtered = contacts.ToArray(); int count = filtered.Count(c => c.Active), physical = physicalCount ?? count;
        if (physical == 0) Blocked = false;
        if (physical < 2 && !buttonsDown) DragBlocked = false;
        if (buttonContactCount > 0 && count >= 2) DragBlocked = false;
        else if (buttonsDown) DragBlocked = true;
        if (count >= 2 && !config.Windows[count.ToString()]) Blocked = true;
        return count >= 2 && !Blocked && !DragBlocked ? filtered : Array.Empty<Contact>();
    }
}

public sealed class GestureMotionTracker(int width, int height)
{
    private readonly ContactMetric metric = new(width, height);
    private Dictionary<int, (int X, int Y)> origins = new();
    public bool Open { get; private set; }
    public bool Update(IEnumerable<Contact> contacts, double slopMm)
    {
        var active = contacts.Where(c => c.Active).ToDictionary(c => c.Slot);
        if (active.Count != 2) { origins.Clear(); Open = false; return active.Count > 2; }
        if (!active.Keys.ToHashSet().SetEquals(origins.Keys)) { origins = active.ToDictionary(p => p.Key, p => (p.Value.X, p.Value.Y)); Open = false; }
        if (active.Any(p => metric.Distance(p.Value, origins[p.Key]) > slopMm)) Open = true;
        return Open;
    }
}
