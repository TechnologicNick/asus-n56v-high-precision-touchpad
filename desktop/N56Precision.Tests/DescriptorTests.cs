using System.Text.RegularExpressions;

static class DescriptorTests
{
    public static void Run()
    {
        string source = Regex.Replace(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "report_descriptor.h")), @"/\*.*?\*/", "", RegexOptions.Singleline);
        string finger = source.Split("#define PTP_FINGER")[1].Split("static const")[0];
        string body = source.Split("PtpDescriptor[] = {")[1].Split("};")[0].Replace("PTP_FINGER", finger);
        byte[] bytes = Regex.Matches(body, "0x([0-9a-fA-F]+)").Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToArray();
        int page = 0, usage = 0, report = 0, width = 0, count = 0, depth = 0, contacts = 0;
        var tops = new List<(int, int)>(); var sizes = new Dictionary<(int, int), int>();
        for (int i = 0; i < bytes.Length;)
        {
            byte prefix = bytes[i]; int size = new[] { 0, 1, 2, 4 }[prefix & 3];
            if (prefix == 0xfe || i + 1 + size > bytes.Length) throw new Exception("Invalid HID item");
            int value = 0; for (int j = 0; j < size; j++) value |= bytes[i + 1 + j] << (8 * j);
            int kind = (prefix >> 2) & 3, tag = prefix >> 4; i += 1 + size;
            if (kind == 1) { switch (tag) { case 0: page = value; break; case 7: width = value; break; case 8: report = value; break; case 9: count = value; break; } }
            else if (kind == 2 && tag == 0) usage = value;
            else if (kind == 0)
            {
                if (tag == 10) { if (depth == 0) tops.Add((page, usage)); if (page == 13 && usage == 34 && report == 1) contacts++; depth++; }
                else if (tag == 12) { if (--depth < 0) throw new Exception("Unbalanced HID collection"); }
                else if (tag is 8 or 9 or 11) sizes[(tag, report)] = sizes.GetValueOrDefault((tag, report)) + width * count;
                usage = 0;
            }
        }
        if (depth != 0 || !tops.SequenceEqual(new[] { (13, 5), (13, 14) }) || contacts != 5 || sizes[(8, 1)] != 34 * 8 ||
            sizes[(11, 2)] != 16 || sizes[(11, 3)] != 256 * 8 || sizes[(11, 4)] != 8 || sizes[(11, 5)] != 8)
            throw new Exception("HID descriptor collection/report widths do not match the C# encoder");
        if (!Convert.ToHexString(bytes).Contains("050909012501750195018102") || !Convert.ToHexString(bytes).Contains("85050922A10009570958"))
            throw new Exception("Missing Windows button/switch usages");
        Console.WriteLine("PASS: independently decoded HID descriptor widths, collections and Windows usages.");
    }
}
