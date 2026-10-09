using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// Congela os DEFAULTS públicos do <see cref="PdfViewer"/> — mudanças aqui são breaking change
/// do pacote e precisam ser intencionais.
/// </summary>
public class PdfViewerDefaultsTests
{
    private readonly PdfViewer _v = new();

    [Fact] public void Source_padrao_e_nulo()              => Assert.Null(_v.Source);
    [Fact] public void PdfStream_padrao_e_nulo()           => Assert.Null(_v.PdfStream);
    [Fact] public void Password_padrao_e_nulo()            => Assert.Null(_v.Password);

    [Fact] public void CurrentPage_padrao_e_zero()         => Assert.Equal(0, _v.CurrentPage);
    [Fact] public void PageCount_padrao_e_zero()           => Assert.Equal(0, _v.PageCount);

    [Fact] public void ZoomFactor_padrao_e_1()             => Assert.Equal(1.0, _v.ZoomFactor);
    [Fact] public void MinZoom_padrao_e_meio()             => Assert.Equal(0.5, _v.MinZoom);
    [Fact] public void MaxZoom_padrao_e_8()                => Assert.Equal(8.0, _v.MaxZoom);

    [Fact] public void EnableThumbnailBar_padrao_e_false() => Assert.False(_v.EnableThumbnailBar);
    [Fact] public void IsThumbnailBarOpen_padrao_e_false() => Assert.False(_v.IsThumbnailBarOpen);
    [Fact] public void ThumbnailBarPlacement_padrao_e_None()
        => Assert.Equal(PdfThumbnailPlacement.None, _v.ThumbnailBarPlacement);

    [Fact] public void EnablePageCaching_padrao_e_true()   => Assert.True(_v.EnablePageCaching);
    [Fact] public void IsPinchZoomEnabled_padrao_e_true()  => Assert.True(_v.IsPinchZoomEnabled);

    [Fact] public void PageBackgroundColor_padrao_e_branco() => Assert.Equal(Colors.White, _v.PageBackgroundColor);
    [Fact] public void PageSpacing_padrao_e_8()             => Assert.Equal(8.0, _v.PageSpacing);
    [Fact] public void ScrollOrientation_padrao_e_vertical()
        => Assert.Equal(PdfScrollOrientation.Vertical, _v.ScrollOrientation);

    [Fact] public void CopyButtonText_padrao_e_Copy()          => Assert.Equal("Copy", _v.CopyButtonText);
    [Fact] public void CopiedMessageText_padrao_e_Copied()     => Assert.Equal("Copied", _v.CopiedMessageText);
    [Fact] public void ThumbnailBarTitleText_padrao_e_Pages()  => Assert.Equal("Pages", _v.ThumbnailBarTitleText);
    [Fact] public void PrintJobName_padrao_e_Document()        => Assert.Equal("Document", _v.PrintJobName);

    [Fact] public void RenderScale_padrao_e_1_5()           => Assert.Equal(1.5, _v.RenderScale);
    [Fact] public void MaxCacheMB_padrao_e_200()            => Assert.Equal(200, _v.MaxCacheMB);
    [Fact] public void PrefetchAbove_padrao_e_2()           => Assert.Equal(2, _v.PrefetchAbove);
    [Fact] public void PrefetchBelow_padrao_e_3()           => Assert.Equal(3, _v.PrefetchBelow);

    [Fact] public void Commands_padrao_sao_nulos()
    {
        Assert.Null(_v.DocumentLoadedCommand);
        Assert.Null(_v.DocumentLoadFailedCommand);
        Assert.Null(_v.PageChangedCommand);
    }

    [Fact] public void PdfViewer_e_uma_View_Maui() => Assert.IsAssignableFrom<View>(_v);

    // ── Modos de binding (contrato para MVVM) ─────────────────────────────────────
    [Fact] public void CurrentPage_e_TwoWay()
        => Assert.Equal(BindingMode.TwoWay, PdfViewer.CurrentPageProperty.DefaultBindingMode);

    [Fact] public void ZoomFactor_e_TwoWay()
        => Assert.Equal(BindingMode.TwoWay, PdfViewer.ZoomFactorProperty.DefaultBindingMode);

    [Fact] public void IsThumbnailBarOpen_e_TwoWay()
        => Assert.Equal(BindingMode.TwoWay, PdfViewer.IsThumbnailBarOpenProperty.DefaultBindingMode);

    [Fact] public void Source_e_OneWay()
        => Assert.Equal(BindingMode.OneWay, PdfViewer.SourceProperty.DefaultBindingMode);

    [Fact] public void PageCount_nao_tem_setter_publico()
    {
        var set = typeof(PdfViewer).GetProperty(nameof(PdfViewer.PageCount))!.SetMethod;
        Assert.NotNull(set);
        Assert.False(set!.IsPublic);   // private set — só o handler (via RaiseDocumentLoaded) escreve
    }
}
