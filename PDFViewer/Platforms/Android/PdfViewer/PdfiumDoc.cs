// Platforms/Android/PdfViewer/PdfiumDoc.cs
//
// Motor único de PDF no Android via PDFium (PDFiumCore, P/Invoke; mesmo motor do Edge/Chrome e do
// handler Windows). Substitui o android.graphics.pdf.PdfRenderer nativo: o PdfRenderer rasteriza
// conteúdo vetorial/texto de certos PDFs em BRANCO (só desenha imagens embutidas), enquanto o
// PDFium renderiza tudo corretamente — paridade total com Windows/iOS/Mac.
//
// O MESMO documento serve render (FPDF_RenderPageBitmap) e a camada de texto (FPDFText_*, usada
// pela seleção/busca), abrindo o arquivo UMA vez. Coordenadas de texto/tamanho em PONTOS PDF
// (1/72", origem inferior-esquerda).
//
// PDFium NÃO é thread-safe (tem estado global) — TODA chamada à lib é serializada pelo lock de
// processo 'Lib'. A rasterização é síncrona/CPU-bound; o handler já a invoca a partir do thread
// pool (Task.Run), então RenderAndroidBitmapAsync faz o trabalho de forma síncrona sob o lock.

using Android.Graphics;
using PDFiumCore;
using System.Runtime.InteropServices;
using AColor = Android.Graphics.Color;

namespace Agile.Maui.Platforms.Android;

internal sealed class PdfiumDoc : IDisposable
{
    // PDFium NÃO é thread-safe — nem entre documentos distintos. TODA chamada à lib é serializada
    // por este lock de PROCESSO (cobre render + texto + impressão simultâneos).
    internal static readonly object Lib = new();
    private  static bool            _libInit;

    private const int FPDFBitmap_BGRA = 4;     // 4 bytes/pixel, ordem de byte B,G,R,A
    private const int FPDF_ANNOT      = 0x01;  // renderiza anotações

    private FpdfDocumentT?    _doc;
    private readonly double[] _wPt;             // largura da página em PONTOS PDF (1/72")
    private readonly double[] _hPt;             // altura  da página em PONTOS PDF

    // Cache de UMA página de texto (a que está em seleção/busca). Abrir a text page é caro para
    // repetir; mantemos a atual aberta e trocamos sob demanda.
    private int            _txtIdx = -1;
    private FpdfPageT?      _txtPage;
    private FpdfTextpageT?  _txtText;

    public int PageCount => _wPt.Length;

    public PdfiumDoc(string path, string? password = null)
    {
        lock (Lib)
        {
            if (!_libInit) { fpdfview.FPDF_InitLibrary(); _libInit = true; }

            _doc = fpdfview.FPDF_LoadDocument(path, password)
                   ?? throw new InvalidOperationException(
                       "PDFium não conseguiu abrir o documento (arquivo inválido ou senha incorreta).");

            int count = fpdfview.FPDF_GetPageCount(_doc);
            _wPt = new double[count];
            _hPt = new double[count];
            for (int i = 0; i < count; i++)
            {
                double w = 0, h = 0;
                fpdfview.FPDF_GetPageSizeByIndex(_doc, i, ref w, ref h);
                _wPt[i] = Math.Max(1.0, w);
                _hPt[i] = Math.Max(1.0, h);
            }
        }
    }

    public bool IsOpen => _doc is not null;

    public double Ratio(int i) => (i >= 0 && i < _hPt.Length) ? _hPt[i] / _wPt[i] : 1.414;

    /// <summary>Tamanho da página em PONTOS PDF (72 DPI). A proporção é a base do layout das células.</summary>
    public SizeF GetPageSize(int i)
        => (i >= 0 && i < _wPt.Length) ? new SizeF((float)_wPt[i], (float)_hPt[i]) : SizeF.Zero;

    public (double w, double h) PageSizePt(int i)
        => (i >= 0 && i < _wPt.Length) ? (_wPt[i], _hPt[i]) : (612, 792);

    // ── Render (PDFium → Android Bitmap) ─────────────────────────────────────────

    /// <summary>
    /// Rasteriza a página na largura pedida (proporção preservada) para um <see cref="Bitmap"/>
    /// ARGB_8888. SÍNCRONO/CPU-bound: o handler já chama de Task.Run. Serializado pelo lock de
    /// processo. Retorna null se cancelado/falhou. OutOfMemoryError é repropagado (tratado no
    /// chamador, que reduz o cache e re-tenta).
    /// </summary>
    public Task<Bitmap?> RenderAndroidBitmapAsync(int idx, int widthPx, AColor bg, CancellationToken ct)
        => Task.FromResult(RenderBitmap(idx, widthPx, bg, ct));

    private Bitmap? RenderBitmap(int idx, int widthPx, AColor bg, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        lock (Lib)
        {
            if (_doc is null || idx < 0 || idx >= _wPt.Length || ct.IsCancellationRequested) return null;

            int w = Math.Max(1, widthPx);
            int h = Math.Max(1, (int)Math.Round(w * (_hPt[idx] / _wPt[idx])));

            var page = fpdfview.FPDF_LoadPage(_doc, idx);
            if (page is null) return null;
            try
            {
                int stride   = w * 4;
                var pixels   = new byte[stride * h];
                var pin      = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    var bmp = fpdfview.FPDFBitmapCreateEx(w, h, FPDFBitmap_BGRA, pin.AddrOfPinnedObject(), stride);
                    if (bmp is null) return null;
                    try
                    {
                        // FPDFBitmap_FillRect usa cor 8888 ARGB (independente do formato do bitmap);
                        // ToArgb() entrega 0xAARRGGBB.
                        fpdfview.FPDFBitmapFillRect(bmp, 0, 0, w, h, (uint)bg.ToArgb());
                        fpdfview.FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, FPDF_ANNOT);
                    }
                    finally { fpdfview.FPDFBitmapDestroy(bmp); }
                }
                finally { pin.Free(); }

                if (ct.IsCancellationRequested) return null;

                // PDFium produz ordem de byte B,G,R,A; o Bitmap ARGB_8888 do Android, via
                // CopyPixelsFromBuffer, interpreta o buffer como R,G,B,A. Troca B↔R in-place.
                for (int p = 0; p < pixels.Length; p += 4)
                    (pixels[p], pixels[p + 2]) = (pixels[p + 2], pixels[p]);

                var abmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);
                if (abmp is null) return null;
                try
                {
                    using var bb = Java.Nio.ByteBuffer.Wrap(pixels)!;
                    abmp.CopyPixelsFromBuffer(bb);
                }
                catch
                {
                    abmp.Recycle();   // bmp recém-alocado, nunca exibido/cacheado → seguro reciclar
                    throw;
                }
                return abmp;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    // ── Camada de texto (seleção/busca) ──────────────────────────────────────────

    private void EnsureTextPage(int idx)
    {
        if (_txtIdx == idx && _txtText is not null) return;
        CloseTextPage();
        if (_doc is null || idx < 0 || idx >= _wPt.Length) return;
        _txtPage = fpdfview.FPDF_LoadPage(_doc, idx);
        if (_txtPage is null) return;
        _txtText = fpdf_text.FPDFTextLoadPage(_txtPage);
        _txtIdx  = idx;
    }

    private void CloseTextPage()
    {
        if (_txtText is not null) { fpdf_text.FPDFTextClosePage(_txtText); _txtText = null; }
        if (_txtPage is not null) { fpdfview.FPDF_ClosePage(_txtPage);     _txtPage = null; }
        _txtIdx = -1;
    }

    // Índice do caractere na posição (em PONTOS PDF, origem inferior-esquerda) ou -1.
    public int CharIndexAtPagePoint(int idx, double xPt, double yPt, double tol)
    {
        lock (Lib)
        {
            EnsureTextPage(idx);
            if (_txtText is null) return -1;
            return fpdf_text.FPDFTextGetCharIndexAtPos(_txtText, xPt, yPt, tol, tol);
        }
    }

    /// <summary>Total de caracteres da página (para delimitar a seleção/expansão de palavra).</summary>
    public int CharCount(int idx)
    {
        lock (Lib)
        {
            EnsureTextPage(idx);
            return _txtText is null ? 0 : fpdf_text.FPDFTextCountChars(_txtText);
        }
    }

    /// <summary>Texto da página entre [from, from+count) — usado para achar limites de palavra e copiar.</summary>
    public string GetText(int idx, int from, int count)
    {
        lock (Lib)
        {
            EnsureTextPage(idx);
            if (_txtText is null || from < 0 || count <= 0) return string.Empty;
            var buf = new ushort[count + 1];   // UTF-16 terminado em null
            int got = fpdf_text.FPDFTextGetText(_txtText, from, count, ref buf[0]);
            if (got <= 1) return string.Empty;
            var chars = new char[got - 1];
            for (int i = 0; i < got - 1; i++) chars[i] = (char)buf[i];
            return new string(chars);
        }
    }

    // Retângulos de realce (em PONTOS PDF) e texto entre dois índices de caractere (inclusivo).
    public (List<(double l, double t, double r, double b)> rects, string text) GetSelection(int idx, int from, int to)
    {
        var rects = new List<(double, double, double, double)>();
        lock (Lib)
        {
            EnsureTextPage(idx);
            if (_txtText is null) return (rects, string.Empty);

            int a = Math.Min(from, to), z = Math.Max(from, to);
            if (a < 0) return (rects, string.Empty);
            int count = z - a + 1;
            if (count <= 0) return (rects, string.Empty);

            int n = fpdf_text.FPDFTextCountRects(_txtText, a, count);
            for (int i = 0; i < n; i++)
            {
                double l = 0, t = 0, r = 0, b = 0;
                fpdf_text.FPDFTextGetRect(_txtText, i, ref l, ref t, ref r, ref b);
                rects.Add((l, t, r, b));
            }

            var buf = new ushort[count + 1];   // UTF-16 terminado em null
            int got = fpdf_text.FPDFTextGetText(_txtText, a, count, ref buf[0]);
            string text = string.Empty;
            if (got > 1)
            {
                var chars = new char[got - 1];
                for (int i = 0; i < got - 1; i++) chars[i] = (char)buf[i];
                text = new string(chars);
            }
            return (rects, text);
        }
    }

    // Busca 'term' (case-insensitive) no documento → (página, índice do 1º char, nº de chars).
    public List<(int page, int index, int count)> FindAll(string term, int maxHits = 5000)
    {
        var hits = new List<(int, int, int)>();
        if (string.IsNullOrEmpty(term)) return hits;

        lock (Lib)
        {
            if (_doc is null) return hits;
            var wbuf = new ushort[term.Length + 1];
            for (int i = 0; i < term.Length; i++) wbuf[i] = term[i];

            for (int p = 0; p < _wPt.Length && hits.Count < maxHits; p++)
            {
                var page = fpdfview.FPDF_LoadPage(_doc, p);
                if (page is null) continue;
                var tp = fpdf_text.FPDFTextLoadPage(page);
                if (tp is not null)
                {
                    var sh = fpdf_text.FPDFTextFindStart(tp, ref wbuf[0], 0, 0);
                    if (sh is not null)
                    {
                        while (hits.Count < maxHits && fpdf_text.FPDFTextFindNext(sh) != 0)
                        {
                            int idx = fpdf_text.FPDFTextGetSchResultIndex(sh);
                            int cnt = fpdf_text.FPDFTextGetSchCount(sh);
                            if (idx >= 0 && cnt > 0) hits.Add((p, idx, cnt));
                        }
                        fpdf_text.FPDFTextFindClose(sh);
                    }
                    fpdf_text.FPDFTextClosePage(tp);
                }
                fpdfview.FPDF_ClosePage(page);
            }
        }
        return hits;
    }

    // ── Links ─────────────────────────────────────────────────────────────────────
    // Link sob o ponto (em PONTOS PDF): retorna a URI externa OU o índice da página de destino
    // (link interno). (null, -1) se não há link no ponto.
    public (string? uri, int destPage) LinkAtPagePoint(int idx, double xPt, double yPt)
    {
        lock (Lib)
        {
            if (_doc is null || idx < 0 || idx >= _wPt.Length) return (null, -1);
            var page = fpdfview.FPDF_LoadPage(_doc, idx);
            if (page is null) return (null, -1);
            try
            {
                var link = fpdf_doc.FPDFLinkGetLinkAtPoint(page, xPt, yPt);
                if (link is null) return (null, -1);

                // 1) destino direto (link interno → página).
                var dest = fpdf_doc.FPDFLinkGetDest(_doc, link);
                if (dest is not null)
                {
                    int dp = fpdf_doc.FPDFDestGetDestPageIndex(_doc, dest);
                    if (dp >= 0) return (null, dp);
                }

                // 2) ação: GoTo (interno) ou URI (externa).
                var action = fpdf_doc.FPDFLinkGetAction(link);
                if (action is not null)
                {
                    var type = fpdf_doc.FPDFActionGetType(action);   // 1=GoTo,2=RemoteGoTo,3=URI,4=Launch
                    if (type == 1)
                    {
                        var d2 = fpdf_doc.FPDFActionGetDest(_doc, action);
                        if (d2 is not null)
                        {
                            int dp = fpdf_doc.FPDFDestGetDestPageIndex(_doc, d2);
                            if (dp >= 0) return (null, dp);
                        }
                    }
                    else if (type == 3)
                    {
                        // buffer: IntPtr; tamanho/retorno: ulong (bytes, inclui o terminador null).
                        ulong len = fpdf_doc.FPDFActionGetURIPath(_doc, action, System.IntPtr.Zero, 0);
                        int n = (int)len;
                        if (n > 1)
                        {
                            var buf = new byte[n];
                            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
                            try { fpdf_doc.FPDFActionGetURIPath(_doc, action, h.AddrOfPinnedObject(), (ulong)n); }
                            finally { h.Free(); }
                            if (buf[n - 1] == 0) n--;   // remove o terminador null
                            if (n > 0) return (System.Text.Encoding.ASCII.GetString(buf, 0, n), -1);
                        }
                    }
                }
                return (null, -1);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    public void Dispose()
    {
        lock (Lib)
        {
            CloseTextPage();
            if (_doc is not null) { fpdfview.FPDF_CloseDocument(_doc); _doc = null; }
        }
    }
}
