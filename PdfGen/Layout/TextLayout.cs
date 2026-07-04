using System.Collections.Generic;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Layout;

/// <summary>Uma linha já quebrada, com sua largura medida.</summary>
public readonly struct TextLine
{
    public readonly string Text;
    public readonly float Width;
    public readonly bool CanJustify;

    public TextLine(string text, float width, bool canJustify = false)
    {
        Text = text;
        Width = width;
        CanJustify = canJustify;
    }
}

/// <summary>Quebra texto em linhas (word wrap) usando as larguras da fonte. Independente de plataforma.</summary>
internal static class TextLayout
{
    public static List<TextLine> Wrap(string text, TextStyle style, float maxWidth)
    {
        var result = new List<TextLine>();
        if (string.IsNullOrEmpty(text))
        {
            result.Add(new TextLine(string.Empty, 0f));
            return result;
        }

        // Respeita quebras de linha explícitas.
        foreach (string paragraph in SplitLines(text))
            WrapParagraph(paragraph, style, maxWidth, result);

        if (result.Count == 0)
            result.Add(new TextLine(string.Empty, 0f));

        return result;
    }

    static void WrapParagraph(string paragraph, TextStyle style, float maxWidth, List<TextLine> result)
    {
        if (paragraph.Length == 0)
        {
            result.Add(new TextLine(string.Empty, 0f));
            return;
        }

        string[] words = paragraph.Split(' ');
        var current = new System.Text.StringBuilder();
        float currentWidth = 0f;
        float spaceWidth = style.MeasureWidth(" ");

        foreach (string word in words)
        {
            float wordWidth = style.MeasureWidth(word);

            if (current.Length == 0)
            {
                // Palavra sozinha maior que a largura: quebra por caractere.
                if (wordWidth > maxWidth && maxWidth > 0f)
                {
                    BreakLongWord(word, style, maxWidth, result);
                    if (result.Count > 0)
                    {
                        TextLine last = result[^1];
                        current.Append(last.Text);
                        currentWidth = last.Width;
                        result.RemoveAt(result.Count - 1);
                    }
                }
                else
                {
                    current.Append(word);
                    currentWidth = wordWidth;
                }
                continue;
            }

            float withNext = currentWidth + spaceWidth + wordWidth;
            if (withNext <= maxWidth)
            {
                current.Append(' ').Append(word);
                currentWidth = withNext;
            }
            else
            {
                result.Add(new TextLine(current.ToString(), currentWidth, canJustify: true));
                current.Clear();

                if (wordWidth > maxWidth && maxWidth > 0f)
                {
                    BreakLongWord(word, style, maxWidth, result);
                    if (result.Count > 0)
                    {
                        TextLine last = result[^1];
                        current.Append(last.Text);
                        currentWidth = last.Width;
                        result.RemoveAt(result.Count - 1);
                    }
                }
                else
                {
                    current.Append(word);
                    currentWidth = wordWidth;
                }
            }
        }

        if (current.Length > 0)
            result.Add(new TextLine(current.ToString(), currentWidth));
    }

    static void BreakLongWord(string word, TextStyle style, float maxWidth, List<TextLine> result)
    {
        var chunk = new System.Text.StringBuilder();
        float chunkWidth = 0f;

        for (int i = 0; i < word.Length; i++)
        {
            int codepoint = word[i];
            string glyphText;
            if (char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]))
            {
                codepoint = char.ConvertToUtf32(word[i], word[i + 1]);
                glyphText = word.Substring(i, 2);
                i++;
            }
            else
            {
                glyphText = word[i].ToString();
            }

            float cw = style.GlyphWidth(codepoint) / 1000f * style.FontSize;
            if (chunk.Length > 0 && chunkWidth + cw > maxWidth)
            {
                result.Add(new TextLine(chunk.ToString(), chunkWidth));
                chunk.Clear();
                chunkWidth = 0f;
            }
            chunk.Append(glyphText);
            chunkWidth += cw;
        }

        if (chunk.Length > 0)
            result.Add(new TextLine(chunk.ToString(), chunkWidth));
    }

    static IEnumerable<string> SplitLines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int end = i;
                if (end > start && text[end - 1] == '\r')
                    end--;
                yield return text.Substring(start, end - start);
                start = i + 1;
            }
        }
        yield return text.Substring(start);
    }
}
