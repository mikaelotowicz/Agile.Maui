namespace Agile.Maui.SignaturePadTests;

/// <summary>
/// Helpers para dirigir o pad pelos métodos internos de toque, simulando
/// assinaturas completas de forma determinística (sem UI, sem sleeps).
/// </summary>
internal static class PadDriver
{
    public const double PassoMs = 10;

    /// <summary>
    /// Assinatura completa: down no primeiro ponto, moves nos intermediários e up no
    /// último, com <see cref="PassoMs"/> entre amostras e sem pressão de hardware.
    /// Um único ponto vira um tap (down+up no mesmo lugar).
    /// </summary>
    public static void Assinar(this SignaturePad pad, double tInicialMs, params (float X, float Y)[] pontos)
    {
        if (pontos.Length == 0)
            throw new ArgumentException("Informe ao menos um ponto.", nameof(pontos));

        var t = tInicialMs;
        pad.OnTouchDown(pontos[0].X, pontos[0].Y, 0f, false, t);

        for (var i = 1; i < pontos.Length - 1; i++)
        {
            t += PassoMs;
            pad.OnTouchMove(pontos[i].X, pontos[i].Y, 0f, false, t);
        }

        if (pontos.Length > 1)
            t += PassoMs;
        pad.OnTouchUp(pontos[^1].X, pontos[^1].Y, 0f, false, t);
    }

    /// <summary>Larguras calculadas do stroke completado de índice <paramref name="indice"/>.</summary>
    public static IReadOnlyList<float> LargurasDoStroke(this SignaturePad pad, int indice) =>
        pad.AllStrokesForRender[indice].Widths;
}
