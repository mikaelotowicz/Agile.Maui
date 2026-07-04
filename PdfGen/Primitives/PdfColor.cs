namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Cor RGBA de 8 bits por canal.</summary>
public readonly struct PdfColor : IEquatable<PdfColor>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    public PdfColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public bool IsTransparent => A == 0;

    public float RedF => R / 255f;
    public float GreenF => G / 255f;
    public float BlueF => B / 255f;
    public float AlphaF => A / 255f;

    /// <summary>Cria a partir de hex "#RGB", "#RRGGBB" ou "#RRGGBBAA" (com ou sem '#').</summary>
    public static PdfColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("Hex vazio.", nameof(hex));

        ReadOnlySpan<char> s = hex.AsSpan().Trim();
        if (s[0] == '#')
            s = s[1..];

        if (s.Length == 3)
        {
            byte r = HexPair(s[0], s[0]);
            byte g = HexPair(s[1], s[1]);
            byte b = HexPair(s[2], s[2]);
            return new PdfColor(r, g, b);
        }

        if (s.Length == 6)
            return new PdfColor(HexPair(s[0], s[1]), HexPair(s[2], s[3]), HexPair(s[4], s[5]));

        if (s.Length == 8)
            return new PdfColor(HexPair(s[0], s[1]), HexPair(s[2], s[3]), HexPair(s[4], s[5]), HexPair(s[6], s[7]));

        throw new ArgumentException($"Hex inválido: '{hex}'.", nameof(hex));
    }

    static byte HexPair(char hi, char lo) => (byte)((HexDigit(hi) << 4) | HexDigit(lo));

    static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';
        if (c >= 'a' && c <= 'f')
            return c - 'a' + 10;
        if (c >= 'A' && c <= 'F')
            return c - 'A' + 10;
        throw new ArgumentException($"Dígito hex inválido: '{c}'.");
    }

    public bool Equals(PdfColor other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is PdfColor other && Equals(other);
    public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
