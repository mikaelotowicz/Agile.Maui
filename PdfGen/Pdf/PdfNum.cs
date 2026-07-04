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
            int code = c;
            if (code > 255)
                code = '?';   // fora de WinAnsi/Latin-1

            char ch = (char)code;
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
}
