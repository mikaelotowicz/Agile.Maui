using System.Globalization;

namespace Agile.Maui.PdfGen.Pdf;

/// <summary>Formatação de números e strings para o conteúdo PDF (cultura invariante).</summary>
internal static class PdfNum
{
    public static string F(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
            return "0";
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Escapa uma string para um literal PDF, mapeando cada char para um byte WinAnsi (0..255).</summary>
    public static string EscapeLiteral(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            char ch = (char)WinAnsiByte(c);
            if (ch == '(' || ch == ')' || ch == '\\')
                sb.Append('\\').Append(ch);
            else if (ch == '\r')
                sb.Append("\\r");
            else if (ch == '\n')
                sb.Append("\\n");
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    static byte WinAnsiByte(char c)
    {
        if (c <= 0xFF)
            return (byte)c;

        return c switch
        {
            '€' => 0x80,
            '‚' => 0x82,
            'ƒ' => 0x83,
            '„' => 0x84,
            '…' => 0x85,
            '†' => 0x86,
            '‡' => 0x87,
            'ˆ' => 0x88,
            '‰' => 0x89,
            'Š' => 0x8A,
            '‹' => 0x8B,
            'Œ' => 0x8C,
            'Ž' => 0x8E,
            '‘' => 0x91,
            '’' => 0x92,
            '“' => 0x93,
            '”' => 0x94,
            '•' => 0x95,
            '–' => 0x96,
            '—' => 0x97,
            '˜' => 0x98,
            '™' => 0x99,
            'š' => 0x9A,
            '›' => 0x9B,
            'œ' => 0x9C,
            'ž' => 0x9E,
            'Ÿ' => 0x9F,
            _ => (byte)'?',
        };
    }
}
