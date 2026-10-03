using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using N56Precision;
using N56Precision.Backend;

static class CoreTests
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static Contact F(int slot, int x = 1000, int y = 600) => new(slot, true, x, y);
    static string Record(int active = 1, int x = 1580) => "Data=5," + string.Join(',', new[] { active, x, 895, 3, 33 }.Concat(new int[20]));
    public static void Run()
    {
        Decoder(); Reports(); Zones(); Chords(); Momentum(); Asus(); Validation();
        Console.WriteLine($"PASS: {checks} C# backend checks (decoder, HID, zones, chords, momentum, settings).");
    }
    static void Decoder()
    {
        var bytes = Encoding.ASCII.GetBytes(Record() + Record(0) + Record(1, 1800));
        for (int split = 0; split <= bytes.Length; split++)
        {
            var decoder = new StreamDecoder(); var frames = decoder.Feed(bytes.AsSpan(0, split)).Concat(decoder.Feed(bytes.AsSpan(split))).ToArray();
            Check(frames.Select(f => f[0].Active).SequenceEqual(new[] { true, false, true }), "Split-byte active mismatch " + split);
            Check(frames.Select(f => f[0].X).SequenceEqual(new[] { 1580, 1580, 1800 }), "Split-byte coordinate mismatch " + split);
        }
        var single = new StreamDecoder(); var one = bytes.SelectMany(b => single.Feed(new[] { b })).ToArray(); Check(one.Length == 3, "Byte-by-byte decoder");
        string tail = Record()[..(Record().LastIndexOf(',') + 1)]; var final = new StreamDecoder();
        Check(final.Feed(Encoding.ASCII.GetBytes(tail)).Count == 1, "Up must not await final pressure digit");
        Check(final.Feed(Encoding.ASCII.GetBytes("123")).Count == 0, "Duplicate frame");
        var recover = new StreamDecoder(); Check(recover.Feed(Encoding.ASCII.GetBytes("garbageData=broken" + Record())).Count == 1, "Malformed stream recovery");
        Check(recover.Feed(Encoding.ASCII.GetBytes(new string('x', 3000))).Count == 0, "Oversized garbage");
        Check(recover.Feed(Encoding.ASCII.GetBytes(Record())).Count == 1, "Oversize recovery");
        Check(new StreamDecoder().Feed(Encoding.ASCII.GetBytes(Record(7))).Count == 0, "Reject invalid active flag");
    }
    static void Reports()
    {
        var encoder = new ReportEncoder(3420, 2052); var first = encoder.Encode(new[] { F(2, 3420, 2052) }, 65535);
        Check(first.Length == 35 && first[0] == 1 && first[1] == 3 && first[2] == 2 && first[33] == 1 && first[34] == 0, "HID layout");
        Check(BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(3)) == 4095 && BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(5)) == 4095, "Coordinate scaling");
        var up = encoder.Release(65536); Check(up[1] == 1 && up[2] == 2 && up[31] == 0 && up[32] == 0 && up[33] == 1, "Lift and scan wrap");
        Check(encoder.Release(65537)[33] == 0, "Release idempotence");
        encoder.Encode(Enumerable.Range(0, 5).Select(i => F(i)), 1); var all = encoder.Release(2);
        Check(all[33] == 5 && Enumerable.Range(0, 5).All(i => all[1 + i * 6] == 1), "Five-finger release");
        encoder.Encode(new[] { F(0), F(1) }, 3); var partial = encoder.Encode(new[] { F(1, 200, 200) }, 4);
        Check(partial[33] == 2 && partial[1] == 3 && partial[2] == 1 && partial[7] == 1 && partial[8] == 0, "Partial release retains IDs");
        var inverted = new ReportEncoder(100, 100, true).Encode(new[] { F(0, 200, 0) }, 1);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(inverted.AsSpan(3)) == 4095 && BinaryPrimitives.ReadUInt16LittleEndian(inverted.AsSpan(5)) == 4095, "Clamp and inversion");
    }
    static void Zones()
    {
        var config = new Settings(); var zone = new ButtonZone(1000, 1000); var router = new Router();
        var pair = new[] { F(1, 600, 400), F(2, 800, 500) }; var button = F(0, 100, 950);
        Check(zone.Filter(new[] { button }, 15).Length == 0 && zone.Reserved[0] == "left", "Left button excluded");
        Check(zone.Filter(new[] { F(0, 800, 400) }, 15).Length == 0, "Button reservation latched");
        Check(router.Forward(Array.Empty<Contact>(), config, 1, true, 1).Length == 0 && router.DragBlocked, "Press before scrolling");
        var filtered = zone.Filter(new[] { button }.Concat(pair), 15);
        Check(router.Forward(filtered, config, 3, true, zone.Reserved.Count).Length == 2 && !router.DragBlocked, "Two hands can scroll while clicking");
        zone.ProtectGesture(filtered);
        var crossed = new[] { F(1, 600, 950), F(2, 800, 950) };
        Check(zone.Filter(new[] { button }.Concat(crossed), 15).Length == 2, "Active scroll crosses strip");
        zone.Filter(new[] { button }, 15); Check(zone.GestureSlots.Count == 0, "Exemption cleared on lift");
        zone.Filter(Array.Empty<Contact>(), 15); Check(zone.Reserved.Count == 0, "Reservation cleared on lift");
        Check(zone.Filter(new[] { F(0, 900, 950) }, 15).Length == 0 && zone.Reserved[0] == "right", "Right button excluded");
        var low = new ButtonZone(1000, 1000); Check(low.Filter(new[] { F(0, 200, 100) }, 15, true).Length == 0, "Reversed sensor Y");
        Check(new ButtonZone(1000, 1000).Filter(new[] { F(0, 100, 1000) }, 0).Length == 1, "Zero disables zone");
        var physical = new Router(); Check(physical.Forward(pair, config, 2, true).Length == 0 && physical.Forward(pair, config, 2).Length == 0, "Drag latch prevents ghost scroll");
        physical.Forward(pair.Take(1), config, 1); Check(physical.Forward(pair, config, 2).Length == 2, "Drag latch clears after finger drop");
        config.Windows["3"] = false; var disabled = new Router(); disabled.Forward(pair.Append(F(0)), config, 3);
        Check(disabled.Forward(pair, config, 2).Length == 0, "Disabled 3->2 cannot restart");
        disabled.Forward(Array.Empty<Contact>(), config, 0); Check(disabled.Forward(pair, config, 2).Length == 2, "Disabled latch clears on all-up");
        config.Windows["2"] = false; Check(new Router().Forward(pair, config, 3, true, 1).Length == 0, "Button exception respects switches");
    }
    static void Chords()
    {
        var config = new Settings(); var chord = new ChordRightClick(3420, 2052);
        chord.Update(new[] { F(0) }, 0, config); chord.Update(new[] { F(0), F(1, 2000) }, .079, config);
        Check(chord.Update(new[] { F(0) }, .125, config), "79ms stagger + 46ms tap accepted");
        Check(chord.Attempt is { Accepted: true, Reason: "Right-click accepted" } && Math.Abs(chord.Attempt.HoldMs - 79) < .01 && Math.Abs(chord.Attempt.TapMs - 46) < .01, "Timeline matches accepted tap");
        chord.Update(new[] { F(0), F(1) }, .3, config); Check(chord.Update(new[] { F(0) }, .4, config), "Repeated second taps");
        var simultaneous = new ChordRightClick(3420, 2052); simultaneous.Update(new[] { F(0), F(1) }, 0, config);
        Check(!simultaneous.Update(new[] { F(0) }, .1, config) && !simultaneous.Update(Array.Empty<Contact>(), .11, config), "Simultaneous tap rejected");
        var early = new ChordRightClick(3420, 2052); early.Update(new[] { F(0) }, 0, config); early.Update(new[] { F(0), F(1) }, .01, config);
        Check(!early.Update(new[] { F(0) }, .1, config) && early.Attempt!.Reason.Contains("not held"), "Near-simultaneous tap explained");
        foreach (string cancel in new[] { "motion", "time", "third", "anchor", "physical", "disabled", "both" })
        {
            var r = new ChordRightClick(3420, 2052); var profile = new Settings(); r.Update(new[] { F(0) }, 0, profile); r.Update(new[] { F(0), F(1) }, .4, profile);
            var eventContacts = cancel switch { "motion" => new[] { F(0), F(1, 1500) }, "third" => new[] { F(0), F(1), F(2) }, "anchor" => new[] { F(1) }, "both" => Array.Empty<Contact>(), _ => new[] { F(0), F(1) } };
            if (cancel == "disabled") profile.ChordRightClickEnabled = false;
            Check(!r.Update(eventContacts, cancel == "time" ? .8 : .45, profile, cancel == "physical"), cancel + " cancellation event");
            Check(!r.Update(cancel == "anchor" || cancel == "both" ? Array.Empty<Contact>() : new[] { F(0) }, .9, profile), cancel + " cancellation persists");
        }
    }
    static void Momentum()
    {
        var config = new Settings(); var zone = new ButtonZone(1000, 1000); var router = new Router(); var motion = new GestureMotionTracker(1000, 1000);
        var encoder = new ReportEncoder(1000, 1000); var pair = new[] { F(0, 400, 400), F(1, 600, 500) };
        byte[] Frame(Contact[] contacts, int time)
        {
            var routed = router.Forward(zone.Filter(contacts, 15, true), config, contacts.Length);
            if (motion.Update(routed, 2)) zone.ProtectGesture(routed);
            return encoder.Encode(routed, time);
        }
        Frame(pair, 0); Frame(new[] { F(0, 400, 500), F(1, 600, 600) }, 1); Frame(Array.Empty<Contact>(), 2);
        var retouch = Frame(pair, 3); Check(retouch[33] == 2 && retouch[1] == 3 && retouch[7] == 3, "Stationary retouch reaches Windows immediately");
        Check(!motion.Open && zone.GestureSlots.Count == 0, "Stationary retouch does not earn strip immunity");
        Check(Frame(pair, 4)[33] == 2, "Holding stationary stays reported");
    }
    static void Asus()
    {
        var source = new byte[AsusSettings.Size]; BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(12), AsusSettings.Size);
        for (int i = 0; i < 17; i++) BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(16 + i * 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(16 + 17 * 4), 7); BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(16 + 18 * 4), 9);
        var config = new Settings(); var request = AsusSettings.GestureRequest(source, GestureOwnership.AsusValues(config));
        Check(source[0] == 0 && request[0] == 1, "ASUS snapshot not mutated");
        Check(request.AsSpan(16 + 15 * 4).SequenceEqual(source.AsSpan(16 + 15 * 4)), "Reserved ASUS DWORDs preserved");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(16 + 5 * 4)) == 0, "Ordinary ASUS tap suppressed");
        config.Windows["3"] = false; var originalThree = GestureOwnership.AsusValues(config); Check(originalThree[9] == 1 && originalThree[10] == 1 && originalThree[11] == 1, "ASUS ownership retained");
        var muted = GestureOwnership.AsusValues(config, true); Check(Enumerable.Range(5, 7).All(i => muted[i] == 0) && muted[1] == 1, "Zone mute retains pointer taps");
        bool rejected = false; try { AsusSettings.GestureRequest(source, new Dictionary<int, uint> { [15] = 0 }); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unknown ASUS index rejected");
    }
    static void Validation()
    {
        string folder = Path.Combine(Path.GetTempPath(), "n56-validation-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "config.json");
            foreach (var invalid in new[] { new Settings { ButtonZonePercent = -1 }, new Settings { RightClickHoldMs = -1 }, new Settings { RightClickTapMaxMs = 0 }, new Settings { RightClickSlopMm = 11 }, new Settings { Version = 2 } })
            {
                File.WriteAllText(path, JsonSerializer.Serialize(invalid)); bool rejected = false;
                try { Settings.Load(path); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Invalid settings rejected");
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
