using System.Globalization;

namespace UdsOnCan.Flashing;

/// <summary>A contiguous block of firmware bytes at an absolute address.</summary>
public sealed record FlashSegment(long Address, byte[] Data)
{
    public long EndAddress => Address + Data.Length;
}

/// <summary>
/// A firmware image as one or more contiguous segments. Parses Intel HEX
/// (record types 00/01/02/04/05); adjacent records are merged into segments.
/// </summary>
public sealed class HexImage
{
    public IReadOnlyList<FlashSegment> Segments { get; }

    public long TotalBytes
    {
        get { long n = 0; foreach (var s in Segments) n += s.Data.Length; return n; }
    }

    private HexImage(IReadOnlyList<FlashSegment> segments) => Segments = segments;

    public static HexImage FromSegments(IEnumerable<FlashSegment> segments)
        => new(Merge(segments));

    public static HexImage LoadIntelHex(string path)
        => ParseIntelHex(File.ReadAllLines(path));

    public static HexImage ParseIntelHex(IEnumerable<string> lines)
    {
        var blocks = new List<FlashSegment>();
        long upper = 0;          // upper 16 bits from type 04 (linear) or segment base from type 02
        var cur = new List<byte>();
        long curStart = -1;
        long curNext = -1;

        void Flush()
        {
            if (curStart >= 0 && cur.Count > 0)
                blocks.Add(new FlashSegment(curStart, cur.ToArray()));
            cur.Clear();
            curStart = -1;
            curNext = -1;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != ':') continue;

            byte len = Hex8(line, 1);
            int addr16 = (Hex8(line, 3) << 8) | Hex8(line, 5);
            byte type = Hex8(line, 7);

            // checksum verify
            int sum = 0;
            for (int i = 1; i + 1 < line.Length; i += 2) sum += Hex8(line, i);
            if ((sum & 0xFF) != 0)
                throw new FormatException($"Intel HEX checksum error: {line}");

            switch (type)
            {
                case 0x00: // data
                    long abs = upper + addr16;
                    if (curStart < 0) { curStart = abs; curNext = abs; }
                    if (abs != curNext) { Flush(); curStart = abs; curNext = abs; }
                    for (int i = 0; i < len; i++) cur.Add(Hex8(line, 9 + i * 2));
                    curNext += len;
                    break;
                case 0x01: // EOF
                    Flush();
                    return new HexImage(Merge(blocks));
                case 0x02: // extended segment address (×16)
                    Flush();
                    upper = ((Hex8(line, 9) << 8) | Hex8(line, 11)) << 4;
                    break;
                case 0x04: // extended linear address (upper 16 bits)
                    Flush();
                    upper = (long)((Hex8(line, 9) << 8) | Hex8(line, 11)) << 16;
                    break;
                case 0x05: // start linear address — ignored (entry point)
                    break;
                default:
                    throw new FormatException($"unsupported Intel HEX record type 0x{type:X2}");
            }
        }
        Flush();
        return new HexImage(Merge(blocks));
    }

    private static IReadOnlyList<FlashSegment> Merge(IEnumerable<FlashSegment> input)
    {
        var sorted = input.Where(s => s.Data.Length > 0).OrderBy(s => s.Address).ToList();
        var outp = new List<FlashSegment>();
        foreach (var s in sorted)
        {
            if (outp.Count > 0 && outp[^1].EndAddress == s.Address)
            {
                var prev = outp[^1];
                var merged = new byte[prev.Data.Length + s.Data.Length];
                Array.Copy(prev.Data, merged, prev.Data.Length);
                Array.Copy(s.Data, 0, merged, prev.Data.Length, s.Data.Length);
                outp[^1] = new FlashSegment(prev.Address, merged);
            }
            else outp.Add(s);
        }
        return outp;
    }

    private static byte Hex8(string s, int index)
        => byte.Parse(s.AsSpan(index, 2), NumberStyles.HexNumber);
}
