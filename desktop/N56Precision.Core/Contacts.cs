using System.Buffers.Binary;
using System.Text;

namespace N56Precision.Backend;

public sealed record Contact(int Slot, bool Active, int X, int Y, int Width = 3);

public sealed class StreamDecoder
{
    private string buffer = "";
    private bool emitted;
    public List<Contact[]> Feed(ReadOnlySpan<byte> data)
    {
        buffer += Encoding.ASCII.GetString(data);
        var frames = new List<Contact[]>();
        while (true)
        {
            int start = buffer.IndexOf("Data=", StringComparison.Ordinal);
            if (start < 0) { buffer = buffer[^Math.Min(4, buffer.Length)..]; break; }
            if (start > 0) { buffer = buffer[start..]; emitted = false; }
            int end = buffer.IndexOf("Data=", 5, StringComparison.Ordinal);
            string record = end < 0 ? buffer : buffer[..end];
            if (!emitted && record.Count(c => c == ',') >= 25)
            {
                var fields = record[5..].Split(',').Take(25).ToArray();
                var values = new int[25]; bool valid = fields.Length == 25;
                for (int i = 0; i < fields.Length; i++) valid &= int.TryParse(fields[i], out values[i]);
                valid &= values[0] == 5;
                var contacts = new Contact[5];
                for (int slot = 0; slot < 5; slot++)
                {
                    int offset = 1 + slot * 5;
                    int active = values[offset], x = values[offset + 1], y = values[offset + 2], width = values[offset + 3];
                    valid &= active is 0 or 1 && x is >= 0 and <= 65535 && y is >= 0 and <= 65535 && width is >= 0 and <= 255;
                    contacts[slot] = new(slot, active == 1, x, y, width);
                }
                if (valid) frames.Add(contacts);
                emitted = true;
            }
            if (end < 0)
            {
                if (buffer.Length > 2048) { buffer = buffer[^4..]; emitted = false; }
                break;
            }
            buffer = buffer[end..]; emitted = false;
        }
        return frames;
    }
}

public sealed class ReportEncoder(int width, int height, bool invertY = false)
{
    public Dictionary<int, (int X, int Y)> Previous { get; private set; } = new();
    public byte[] Encode(IEnumerable<Contact> contacts, long scanTime)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Invalid sensor dimensions");
        var current = new Dictionary<int, (int X, int Y)>();
        foreach (var c in contacts.Where(c => c.Active))
        {
            int x = (int)Math.Clamp(Math.Round(c.X * 4095.0 / width), 0, 4095);
            int y = (int)Math.Clamp(Math.Round(c.Y * 4095.0 / height), 0, 4095);
            if (!current.TryAdd(c.Slot, (x, invertY ? 4095 - y : y))) throw new ArgumentException("Duplicate contact ID");
        }
        if (current.Count > 5 || current.Keys.Any(slot => slot is < 0 or > 4)) throw new ArgumentException("Invalid contact IDs");
        var entries = current.OrderBy(p => p.Key).Select(p => (p.Key, p.Value, true)).ToList();
        entries.AddRange(Previous.Where(p => !current.ContainsKey(p.Key)).OrderBy(p => p.Key).Select(p => (p.Key, p.Value, false)));
        // Both old and new contacts use the same five physical slots, so the union fits.
        var report = new byte[35]; report[0] = 1;
        for (int i = 0; i < entries.Count; i++)
        {
            var (slot, xy, down) = entries[i]; int offset = 1 + i * 6;
            report[offset] = down ? (byte)3 : (byte)1; report[offset + 1] = (byte)slot;
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(offset + 2), (ushort)xy.X);
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(offset + 4), (ushort)xy.Y);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(31), unchecked((ushort)scanTime));
        report[33] = (byte)entries.Count; Previous = current; return report;
    }
    public byte[] Release(long scanTime) => Encode(Array.Empty<Contact>(), scanTime);
}
