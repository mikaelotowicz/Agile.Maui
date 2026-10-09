using AndroidX.Activity;

namespace Agile.Maui;

public partial class PdfReaderView
{
    private SearchBackCallback? _searchBackCallback;

    // O callback registrado por último no OnBackPressedDispatcher tem prioridade sobre o do MAUI: enquanto
    // a busca está aberta, o Voltar fecha a busca em vez de sair da página.
    partial void OnSearchOpenChanged(bool open)
    {
        if (!open)
        {
            _searchBackCallback?.Remove();
            _searchBackCallback = null;
            return;
        }

        if (_searchBackCallback is not null ||
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not ComponentActivity activity)
            return;

        _searchBackCallback = new SearchBackCallback(this);
        activity.OnBackPressedDispatcher.AddCallback(activity, _searchBackCallback);
    }

    private sealed class SearchBackCallback(PdfReaderView reader) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => reader.HandleBackPressed();
    }
}
