using System.Collections.Generic;
using System.IO;

namespace Agile.Maui.PdfGen.Text;

/// <summary>
/// Reduz uma fonte TrueType aos glifos efetivamente usados, preservando a numeração dos GIDs
/// (mantém CIDToGIDMap /Identity e os GIDs já gravados no content stream). Glifos não usados
/// ficam com contorno vazio. Inclui componentes de glifos compostos. Mantém apenas as tabelas
/// necessárias para um FontFile2 (head, hhea, maxp, hmtx, loca, glyf e, se houver, cvt/fpgm/prep).
/// </summary>
internal static class TrueTypeSubsetter
{
    static readonly string[] KeepExtra = { "cvt ", "fpgm", "prep" };

    public static byte[] Subset(byte[] font, IEnumerable<ushort> usedGids)
    {
        var dir = ReadDirectory(font, out _);
        var head = dir["head"];
        var hhea = dir["hhea"];
        var maxp = dir["maxp"];
        var hmtx = dir["hmtx"];
        var loca = dir["loca"];
        var glyf = dir["glyf"];

        int numGlyphs = U16(font, maxp.offset + 4);
        int indexToLocFormat = U16(font, head.offset + 50);
        int origNumHMetrics = U16(font, hhea.offset + 34);

        int[] locaOffsets = ReadLoca(font, loca.offset, numGlyphs, indexToLocFormat);

        // Conjunto de glifos usados + componentes de compostos + .notdef (gid 0).
        var used = new HashSet<int> { 0 };
        var work = new Stack<int>();
        foreach (ushort g in usedGids)
            if (used.Add(g)) work.Push(g);
        work.Push(0);
        while (work.Count > 0)
        {
            int gid = work.Pop();
            if (gid < 0 || gid >= numGlyphs)
                continue;
            int start = glyf.offset + locaOffsets[gid];
            int end = glyf.offset + locaOffsets[gid + 1];
            if (end <= start)
                continue; // glifo vazio
            AddComposites(font, start, used, work);
        }

        int maxGid = 0;
        foreach (int g in used)
            if (g < numGlyphs && g > maxGid) maxGid = g;
        int newNumGlyphs = maxGid + 1;

        // Novo glyf + novo loca (formato longo).
        using var newGlyf = new MemoryStream();
        var newLoca = new int[newNumGlyphs + 1];
        for (int gid = 0; gid < newNumGlyphs; gid++)
        {
            newLoca[gid] = (int)newGlyf.Length;
            if (used.Contains(gid))
            {
                int start = glyf.offset + locaOffsets[gid];
                int end = glyf.offset + locaOffsets[gid + 1];
                if (end > start)
                    newGlyf.Write(font, start, end - start);
            }
            // Alinha em 2 bytes.
            if ((newGlyf.Length & 1) != 0)
                newGlyf.WriteByte(0);
        }
        newLoca[newNumGlyphs] = (int)newGlyf.Length;

        byte[] glyfTable = newGlyf.ToArray();
        byte[] locaTable = BuildLoca(newLoca);
        byte[] hmtxTable = BuildHmtx(font, hmtx.offset, origNumHMetrics, newNumGlyphs);
        byte[] headTable = BuildHead(font, head);
        byte[] hheaTable = BuildHhea(font, hhea, newNumGlyphs);
        byte[] maxpTable = BuildMaxp(font, maxp, newNumGlyphs);

        var tables = new List<(string tag, byte[] data)>
        {
            ("glyf", glyfTable),
            ("head", headTable),
            ("hhea", hheaTable),
            ("hmtx", hmtxTable),
            ("loca", locaTable),
            ("maxp", maxpTable),
        };
        foreach (string tag in KeepExtra)
            if (dir.TryGetValue(tag, out var t))
                tables.Add((tag, Slice(font, t.offset, t.length)));

        return Assemble(tables);
    }

    static void AddComposites(byte[] font, int glyphStart, HashSet<int> used, Stack<int> work)
    {
        int numberOfContours = I16(font, glyphStart);
        if (numberOfContours >= 0)
            return; // glifo simples

        int p = glyphStart + 10; // após numberOfContours + bbox
        bool more = true;
        while (more)
        {
            int flags = U16(font, p);
            int compGid = U16(font, p + 2);
            p += 4;
            if (used.Add(compGid))
                work.Push(compGid);

            p += (flags & 0x0001) != 0 ? 4 : 2;         // args
            if ((flags & 0x0008) != 0) p += 2;          // escala
            else if ((flags & 0x0040) != 0) p += 4;     // x e y
            else if ((flags & 0x0080) != 0) p += 8;     // 2x2
            more = (flags & 0x0020) != 0;               // MORE_COMPONENTS
        }
    }

    static int[] ReadLoca(byte[] font, int offset, int numGlyphs, int format)
    {
        var loca = new int[numGlyphs + 1];
        if (format == 0)
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = U16(font, offset + i * 2) * 2;
        else
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = (int)U32(font, offset + i * 4);
        return loca;
    }

    static byte[] BuildLoca(int[] offsets)
    {
        var b = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++)
            WriteU32(b, i * 4, (uint)offsets[i]);
        return b;
    }

    static byte[] BuildHmtx(byte[] font, int hmtxOffset, int origNumHMetrics, int newNumGlyphs)
    {
        var b = new byte[newNumGlyphs * 4]; // numberOfHMetrics = newNumGlyphs (todos long)
        int lastAdvance = origNumHMetrics > 0 ? U16(font, hmtxOffset + (origNumHMetrics - 1) * 4) : 0;
        for (int gid = 0; gid < newNumGlyphs; gid++)
        {
            int advance, lsb;
            if (gid < origNumHMetrics)
            {
                advance = U16(font, hmtxOffset + gid * 4);
                lsb = I16(font, hmtxOffset + gid * 4 + 2);
            }
            else
            {
                advance = lastAdvance;
                int lsbOffset = hmtxOffset + origNumHMetrics * 4 + (gid - origNumHMetrics) * 2;
                lsb = lsbOffset + 1 < font.Length ? I16(font, lsbOffset) : 0;
            }
            WriteU16(b, gid * 4, (ushort)advance);
            WriteU16(b, gid * 4 + 2, (ushort)(short)lsb);
        }
        return b;
    }

    static byte[] BuildHead(byte[] font, (int offset, int length) head)
    {
        byte[] b = Slice(font, head.offset, head.length);
        WriteU32(b, 8, 0);          // checkSumAdjustment = 0 (corrigido depois)
        WriteU16(b, 50, 1);         // indexToLocFormat = long
        return b;
    }

    static byte[] BuildHhea(byte[] font, (int offset, int length) hhea, int newNumGlyphs)
    {
        byte[] b = Slice(font, hhea.offset, hhea.length);
        WriteU16(b, 34, (ushort)newNumGlyphs);   // numberOfHMetrics
        return b;
    }

    static byte[] BuildMaxp(byte[] font, (int offset, int length) maxp, int newNumGlyphs)
    {
        byte[] b = Slice(font, maxp.offset, maxp.length);
        WriteU16(b, 4, (ushort)newNumGlyphs);    // numGlyphs
        return b;
    }

    static byte[] Assemble(List<(string tag, byte[] data)> tables)
    {
        tables.Sort((a, b) => string.CompareOrdinal(a.tag, b.tag));
        int n = tables.Count;

        int maxPow2 = 1, entrySelector = 0;
        while (maxPow2 * 2 <= n) { maxPow2 *= 2; entrySelector++; }
        int searchRange = maxPow2 * 16;
        int rangeShift = n * 16 - searchRange;

        int headerSize = 12 + n * 16;
        int offset = headerSize;
        var placed = new List<(string tag, byte[] data, int offset, uint checksum)>();
        foreach (var (tag, data) in tables)
        {
            int padded = (data.Length + 3) & ~3;
            placed.Add((tag, data, offset, TableChecksum(data)));
            offset += padded;
        }
        int total = offset;

        var buf = new byte[total];
        WriteU32(buf, 0, 0x00010000);
        WriteU16(buf, 4, (ushort)n);
        WriteU16(buf, 6, (ushort)searchRange);
        WriteU16(buf, 8, (ushort)entrySelector);
        WriteU16(buf, 10, (ushort)rangeShift);

        int rec = 12;
        foreach (var (tag, data, off, checksum) in placed)
        {
            for (int i = 0; i < 4; i++)
                buf[rec + i] = (byte)tag[i];
            WriteU32(buf, rec + 4, checksum);
            WriteU32(buf, rec + 8, (uint)off);
            WriteU32(buf, rec + 12, (uint)data.Length);
            System.Array.Copy(data, 0, buf, off, data.Length);
            rec += 16;
        }

        // Corrige head.checkSumAdjustment com o checksum do arquivo inteiro.
        int headOff = 0;
        foreach (var (tag, _, off, _) in placed)
            if (tag == "head") { headOff = off; break; }
        uint fileChecksum = TableChecksum(buf);
        WriteU32(buf, headOff + 8, unchecked(0xB1B0AFBAu - fileChecksum));

        return buf;
    }

    static uint TableChecksum(byte[] data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 3 < data.Length; i += 4)
            sum = unchecked(sum + U32(data, i));
        if (i < data.Length)
        {
            uint last = 0;
            for (int j = 0; j < 4; j++)
                last |= (uint)(i + j < data.Length ? data[i + j] : 0) << (24 - j * 8);
            sum = unchecked(sum + last);
        }
        return sum;
    }

    static Dictionary<string, (int offset, int length)> ReadDirectory(byte[] font, out int numTables)
    {
        numTables = U16(font, 4);
        var dir = new Dictionary<string, (int, int)>();
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(font, rec, 4);
            int off = (int)U32(font, rec + 8);
            int len = (int)U32(font, rec + 12);
            dir[tag] = (off, len);
        }
        return dir;
    }

    static byte[] Slice(byte[] src, int offset, int length)
    {
        var b = new byte[length];
        System.Array.Copy(src, offset, b, 0, length);
        return b;
    }

    static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    static int I16(byte[] d, int o) => (short)((d[o] << 8) | d[o + 1]);
    static uint U32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    static void WriteU16(byte[] d, int o, ushort v) { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }
    static void WriteU32(byte[] d, int o, uint v)
    {
        d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
    }
}
