using Microsoft.Maui.Controls.Shapes;

namespace sample.Services;

/// <summary>
/// Implementação de <see cref="IAnchoredMenu"/> com um popup estilizado em MAUI puro (sem código
/// nativo nem dependências), consistente nas quatro plataformas. O menu é um cartão arredondado
/// com sombra, ancorado abaixo/à direita da View informada, sobre um scrim que fecha ao tocar fora.
/// </summary>
public sealed class AnchoredMenu : IAnchoredMenu
{
    // Paleta alinhada aos tokens claros do app (Surface/OnSurface/Icon/Outline).
    private static readonly Color Surface    = Color.FromArgb("#FFFFFF");
    private static readonly Color OnSurface   = Color.FromArgb("#2A2A2E");
    private static readonly Color IconColor   = Color.FromArgb("#44444A");
    private static readonly Color Outline     = Color.FromArgb("#E2E2E6");
    private static readonly Color PressedTint = Color.FromArgb("#14000000");

    private const double CardWidth = 230;

    private Grid? _overlay;   // overlay ativo (scrim + cartão)
    private Grid? _host;      // Grid raiz da página onde o overlay foi injetado

    public void Show(View anchor, IReadOnlyList<MenuAction> actions, VisualElement? verticalAnchor = null)
    {
        if (actions.Count == 0) return;

        // Precisa de uma página cujo conteúdo seja um Grid para sobrepor o overlay sem
        // reparentar a árvore (evita flash). É o caso das páginas deste app.
        if (FindPage(anchor) is not { Content: Grid host } page) return;

        DismissImmediate();

        var items = new VerticalStackLayout { Spacing = 2 };
        foreach (var action in actions)
            items.Children.Add(BuildItem(action));

        var card = new Border
        {
            BackgroundColor     = Surface,
            Stroke              = Outline,
            StrokeThickness     = 1,
            StrokeShape         = new RoundRectangle { CornerRadius = 12 },
            Padding             = 6,
            WidthRequest        = CardWidth,
            Content             = items,
            HorizontalOptions   = LayoutOptions.End,
            VerticalOptions     = LayoutOptions.Start,
            Opacity             = 0,
            Scale               = 0.92,
            AnchorX             = 1,   // anima a partir do canto superior direito (junto da âncora)
            AnchorY             = 0,
            Shadow              = new Shadow { Brush = Brush.Black, Opacity = 0.18f, Radius = 16, Offset = new Point(0, 3) },
        };

        var scrim = new Grid { BackgroundColor = Colors.Transparent };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Dismiss) });

        var overlay = new Grid();
        overlay.Children.Add(scrim);
        overlay.Children.Add(card);

        Grid.SetRow(overlay, 0);
        Grid.SetColumn(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));
        host.Children.Add(overlay);

        _overlay = overlay;
        _host    = host;

        // Posiciona o cartão logo abaixo da âncora. Como o overlay cobre o Grid raiz inteiro,
        // medir contra o próprio host evita diferenças de origem nativa no Android.
        void Position()
        {
            var topAnchor = verticalAnchor ?? anchor;
            var (ax, _)   = AnchorOffset(anchor, host);
            var (_, ty)   = AnchorOffset(topAnchor, host);
            double top    = ty + topAnchor.Height;
            double right  = Math.Max(0, host.Width - (ax + anchor.Width));
            card.Margin   = new Thickness(0, top, right, 0);
            _ = AnimateInAsync(card);
        }

        if (overlay.Width > 0)
        {
            Position();
        }
        else
        {
            void OnLaidOut(object? s, EventArgs e)
            {
                overlay.SizeChanged -= OnLaidOut;
                Position();
            }
            overlay.SizeChanged += OnLaidOut;
        }
    }

    // ── Construção dos itens ──────────────────────────────────────────────────

    private View BuildItem(MenuAction action)
    {
        var row = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };

        if (!string.IsNullOrEmpty(action.Icon))
        {
            row.Children.Add(new Label
            {
                Text                = action.Icon,
                FontFamily          = Icons.FontFamily,
                FontSize            = 18,
                TextColor           = IconColor,
                WidthRequest        = 22,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions     = LayoutOptions.Center,
            });
        }

        row.Children.Add(new Label
        {
            Text            = action.Text,
            FontSize        = 14,
            TextColor       = OnSurface,
            VerticalOptions = LayoutOptions.Center,
        });

        var item = new Border
        {
            Padding         = new Thickness(12, 10),
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 8 },
            Content         = row,
        };

        if (action.Enabled)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                Dismiss();
                action.Invoke();
            };
            item.GestureRecognizers.Add(tap);

            // Realce sutil ao pressionar (feedback visual leve, multiplataforma).
            var pressed = new PointerGestureRecognizer();
            pressed.PointerEntered += (_, _) => item.BackgroundColor = PressedTint;
            pressed.PointerExited  += (_, _) => item.BackgroundColor = Colors.Transparent;
            item.GestureRecognizers.Add(pressed);
        }
        else
        {
            item.Opacity = 0.4;
        }

        return item;
    }

    // ── Animação / fechamento ─────────────────────────────────────────────────

    private static async Task AnimateInAsync(VisualElement card)
        => await Task.WhenAll(card.FadeToAsync(1, 120), card.ScaleToAsync(1, 120, Easing.CubicOut));

    private async void Dismiss()
    {
        var overlay = _overlay;
        var host    = _host;
        if (overlay is null || host is null) return;
        _overlay = null;
        _host    = null;

        await overlay.FadeToAsync(0, 90);
        host.Children.Remove(overlay);
    }

    private void DismissImmediate()
    {
        if (_overlay is not null && _host is not null)
            _host.Children.Remove(_overlay);
        _overlay = null;
        _host    = null;
    }

    // ── Helpers de layout ─────────────────────────────────────────────────────

    // Offset da âncora relativo ao host, em unidades MAUI (DIP). Quando o host é ancestral
    // direto da âncora, o caminho de Bounds é mais estável que coordenadas de janela no Android.
    private static (double X, double Y) AnchorOffset(VisualElement anchor, VisualElement host)
    {
        if (IsAncestor(host, anchor))
            return BoundsWalk(anchor, host);

#if ANDROID
        if (anchor.Handler?.PlatformView is Android.Views.View av &&
            host.Handler?.PlatformView is Android.Views.View hv)
        {
            var a = new int[2]; av.GetLocationInWindow(a);
            var h = new int[2]; hv.GetLocationInWindow(h);
            double d = av.Context?.Resources?.DisplayMetrics?.Density ?? 1.0;
            if (d <= 0) d = 1.0;
            return ((a[0] - h[0]) / d, (a[1] - h[1]) / d);
        }
#elif IOS || MACCATALYST
        if (anchor.Handler?.PlatformView is UIKit.UIView av &&
            host.Handler?.PlatformView is UIKit.UIView hv)
        {
            var rect = av.ConvertRectToView(av.Bounds, hv);
            return (rect.X, rect.Y);
        }
#endif
        return BoundsWalk(anchor, host);
    }

    private static bool IsAncestor(Element ancestor, Element element)
    {
        Element? current = element.Parent;
        while (current is not null)
        {
            if (current == ancestor)
                return true;

            current = current.Parent;
        }

        return false;
    }

    // Fallback: posição relativa ao container 'stop' (exclusivo) somando os offsets de layout.
    private static (double X, double Y) BoundsWalk(VisualElement element, VisualElement stop)
    {
        double x = 0, y = 0;
        Element? current = element;
        while (current is VisualElement ve && current != stop)
        {
            x += ve.X;
            y += ve.Y;
            current = ve.Parent;
        }
        return (x, y);
    }

    private static ContentPage? FindPage(Element? element)
    {
        while (element is not null and not ContentPage)
            element = element.Parent;
        return element as ContentPage;
    }
}
