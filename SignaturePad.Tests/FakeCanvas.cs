using System.Numerics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Text;

namespace Agile.Maui.SignaturePadTests;

/// <summary>
/// ICanvas fake que registra as chamadas de desenho relevantes para os testes do
/// <see cref="SignaturePadDrawable"/> (linhas, elipses, paths, textos e estado de traço).
/// </summary>
internal sealed class FakeCanvas : ICanvas
{
    public List<string> Chamadas { get; } = new();
    public List<(float X1, float Y1, float X2, float Y2, float StrokeSize)> Linhas { get; } = new();
    public List<(float X, float Y, float W, float H)> ElipsesPreenchidas { get; } = new();
    public List<(PathF Caminho, float StrokeSize)> CaminhosDesenhados { get; } = new();
    public List<(float X, float Y, float W, float H)> RetangulosPreenchidos { get; } = new();
    public List<string> Textos { get; } = new();
    public List<Color> CoresDeTraco { get; } = new();
    public List<Color> CoresDePreenchimento { get; } = new();

    public float StrokeSizeAtual { get; private set; }
    public Color? StrokeColorAtual { get; private set; }
    public Color? FillColorAtual { get; private set; }
    public LineCap? StrokeLineCapAtual { get; private set; }
    public LineJoin? StrokeLineJoinAtual { get; private set; }
    public bool? AntialiasAtual { get; private set; }

    // ------------------------------------------------------------ Estado

    public float DisplayScale { get; set; } = 1f;

    public float StrokeSize { set => StrokeSizeAtual = value; }
    public float MiterLimit { set { } }

    public Color StrokeColor
    {
        set
        {
            StrokeColorAtual = value;
            CoresDeTraco.Add(value);
        }
    }

    public LineCap StrokeLineCap { set => StrokeLineCapAtual = value; }
    public LineJoin StrokeLineJoin { set => StrokeLineJoinAtual = value; }
    public float Alpha { set { } }
    public float[] StrokeDashPattern { set { } }
    public float StrokeDashOffset { set { } }

    public Color FillColor
    {
        set
        {
            FillColorAtual = value;
            CoresDePreenchimento.Add(value);
        }
    }

    public Color FontColor { set { } }
    public IFont Font { set { } }
    public float FontSize { set { } }
    public BlendMode BlendMode { set { } }
    public bool Antialias { set => AntialiasAtual = value; }

    // ------------------------------------------------------------ Desenho

    public void DrawLine(float x1, float y1, float x2, float y2)
    {
        Chamadas.Add("DrawLine");
        Linhas.Add((x1, y1, x2, y2, StrokeSizeAtual));
    }

    public void DrawPath(PathF path)
    {
        Chamadas.Add("DrawPath");
        CaminhosDesenhados.Add((path, StrokeSizeAtual));
    }

    public void FillPath(PathF path, WindingMode windingMode) => Chamadas.Add("FillPath");

    public void FillEllipse(float x, float y, float width, float height)
    {
        Chamadas.Add("FillEllipse");
        ElipsesPreenchidas.Add((x, y, width, height));
    }

    public void DrawEllipse(float x, float y, float width, float height) => Chamadas.Add("DrawEllipse");

    public void FillRectangle(float x, float y, float width, float height)
    {
        Chamadas.Add("FillRectangle");
        RetangulosPreenchidos.Add((x, y, width, height));
    }

    public void DrawRectangle(float x, float y, float width, float height) => Chamadas.Add("DrawRectangle");

    public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        Chamadas.Add("DrawRoundedRectangle");

    public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        Chamadas.Add("FillRoundedRectangle");

    public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle,
        bool clockwise, bool closed) => Chamadas.Add("DrawArc");

    public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle,
        bool clockwise) => Chamadas.Add("FillArc");

    public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) =>
        Chamadas.Add("DrawImage");

    public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment)
    {
        Chamadas.Add("DrawString");
        Textos.Add(value);
    }

    public void DrawString(string value, float x, float y, float width, float height,
        HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
        TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0)
    {
        Chamadas.Add("DrawString");
        Textos.Add(value);
    }

    public void DrawText(IAttributedText value, float x, float y, float width, float height) =>
        Chamadas.Add("DrawText");

    // ------------------------------------------------------------ Transformações e estado

    public void Rotate(float degrees, float x, float y) { }
    public void Rotate(float degrees) { }
    public void Scale(float sx, float sy) { }
    public void Translate(float tx, float ty) { }
    public void ConcatenateTransform(Matrix3x2 transform) { }
    public void SaveState() { }
    public bool RestoreState() => true;
    public void ResetState() { }
    public void SetShadow(SizeF offset, float blur, Color color) { }
    public void SetFillPaint(Paint paint, RectF rectangle) { }
    public void SubtractFromClip(float x, float y, float width, float height) { }
    public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) { }
    public void ClipRectangle(float x, float y, float width, float height) { }

    public SizeF GetStringSize(string value, IFont font, float fontSize) => default;

    public SizeF GetStringSize(string value, IFont font, float fontSize,
        HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) => default;
}
