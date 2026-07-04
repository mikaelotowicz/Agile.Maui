using System;
using Agile.Maui.PdfGen.Layout;

namespace Agile.Maui.PdfGen.Api;

/// <summary>Raiz fluente do documento: define uma ou mais seções de página.</summary>
public interface IDocumentContainer
{
    void Page(Action<IPageDescriptor> build);
}

internal sealed class DocumentContainer : IDocumentContainer
{
    readonly DocumentModel _model = new();

    public void Page(Action<IPageDescriptor> build)
    {
        var descriptor = new PageDescriptor(_model.Context);
        build(descriptor);
        _model.Pages.Add(descriptor.Build());
    }

    internal DocumentModel Build() => _model;
}
