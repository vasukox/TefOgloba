using System.Collections;
using System.Windows.Input;

namespace Permoda.Pay.Maui.Views.Controls;

/// <summary>
/// Cierre de operación: qué bonos salieron, a qué correo o serial, y por cuánto.
/// <para>
/// La coreografía está en código y no en behaviors porque es una secuencia, no un efecto suelto:
/// el fondo se apaga, la tarjeta sube, el sello marca el resultado y recién ahí entran las líneas
/// una por una. Ese orden es el que hace que el cajero lea el titular antes que el detalle.
/// </para>
/// </summary>
public partial class ResultOverlayView : ContentView
{
    public static readonly BindableProperty HeadlineProperty = BindableProperty.Create(
        nameof(Headline), typeof(string), typeof(ResultOverlayView), string.Empty);

    public static readonly BindableProperty SublineProperty = BindableProperty.Create(
        nameof(Subline), typeof(string), typeof(ResultOverlayView), string.Empty,
        propertyChanged: (b, _, _) => ((ResultOverlayView)b).OnPropertyChanged(nameof(HasSubline)));

    public static readonly BindableProperty FootNoteProperty = BindableProperty.Create(
        nameof(FootNote), typeof(string), typeof(ResultOverlayView), string.Empty,
        propertyChanged: (b, _, _) => ((ResultOverlayView)b).OnPropertyChanged(nameof(HasFootNote)));

    public static readonly BindableProperty LinesProperty = BindableProperty.Create(
        nameof(Lines), typeof(IEnumerable), typeof(ResultOverlayView), null);

    public static readonly BindableProperty IsSuccessProperty = BindableProperty.Create(
        nameof(IsSuccess), typeof(bool), typeof(ResultOverlayView), true,
        propertyChanged: (b, _, _) => ((ResultOverlayView)b).ApplyTone());

    public static readonly BindableProperty DismissCommandProperty = BindableProperty.Create(
        nameof(DismissCommand), typeof(ICommand), typeof(ResultOverlayView), null);

    public string Headline
    {
        get => (string)GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string Subline
    {
        get => (string)GetValue(SublineProperty);
        set => SetValue(SublineProperty, value);
    }

    /// <summary>Aclaración al pie, p. ej. que el correo puede tardar unos minutos en llegar.</summary>
    public string FootNote
    {
        get => (string)GetValue(FootNoteProperty);
        set => SetValue(FootNoteProperty, value);
    }

    public IEnumerable? Lines
    {
        get => (IEnumerable?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public bool IsSuccess
    {
        get => (bool)GetValue(IsSuccessProperty);
        set => SetValue(IsSuccessProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public bool HasSubline => !string.IsNullOrWhiteSpace(Subline);

    public bool HasFootNote => !string.IsNullOrWhiteSpace(FootNote);

    public ResultOverlayView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(IsVisible) && IsVisible)
        {
            _ = PlayAsync();
        }
    }

    private void ApplyTone()
    {
        if (Band is null || BadgeGlyph is null)
        {
            return;
        }

        var key = IsSuccess ? "SuccessButtonBrush" : "DangerButtonBrush";

        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var brush) == true
            && brush is Brush resolved)
        {
            Band.Background = resolved;
        }

        BadgeGlyph.Text = IsSuccess ? "✓" : "✗";
    }

    private void OnDismissRequested(object? sender, EventArgs e)
    {
        if (DismissCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>
    /// Fondo (160ms) → tarjeta (380ms, sube 28dp) → sello (pop) → líneas escalonadas cada 55ms.
    /// Solo se animan Opacity, TranslationY y Scale; nada de Width/Height, que en MAUI cuesta
    /// un layout completo por frame.
    /// </summary>
    private async Task PlayAsync()
    {
        var rows = LinesHost.Children.OfType<View>().ToList();

        try
        {
            Scrim.CancelAnimations();
            Card.CancelAnimations();
            Badge.CancelAnimations();

            Scrim.Opacity = 0;
            Card.Opacity = 0;
            Card.TranslationY = 28;
            Card.Scale = 0.96;
            Badge.Opacity = 0;
            Badge.Scale = 0.4;

            // Las filas se atenúan antes de que la tarjeta entre; si se hiciera después ya se
            // habrían visto un frame en su sitio y el escalonado se leería como un parpadeo.
            foreach (var row in rows)
            {
                row.Opacity = 0;
                row.TranslationY = 14;
            }

            await Scrim.FadeToAsync(0.55, 160, Easing.CubicOut);

            var cardEntrance = Task.WhenAll(
                Card.FadeToAsync(1, 380, Easing.CubicOut),
                Card.TranslateToAsync(0, 0, 380, Easing.CubicOut),
                Card.ScaleToAsync(1, 380, Easing.CubicOut));

            await Task.Delay(110);

            _ = Badge.FadeToAsync(1, 200, Easing.CubicOut);
            await Badge.ScaleToAsync(1.14, 220, Easing.CubicOut);
            await Badge.ScaleToAsync(1, 140, Easing.CubicIn);

            await cardEntrance;

            foreach (var row in rows)
            {
                _ = Task.WhenAll(
                    row.FadeToAsync(1, 260, Easing.CubicOut),
                    row.TranslateToAsync(0, 0, 260, Easing.CubicOut));

                await Task.Delay(55);
            }
        }
        finally
        {
            // Pase lo que pase con la animación, el panel queda visible y usable.
            //
            // La coreografía arranca poniendo la tarjeta en Opacity 0. Si algo la interrumpe a
            // mitad —la página se va, el handler todavía no existe, la animación se cancela—,
            // el fondo oscuro quedaba puesto y la tarjeta invisible: la pantalla se veía negra
            // y sin nada que tocar, con el cajero encerrado a mitad de un cobro.
            ForceFinalState(rows);
        }
    }

    /// <summary>Deja el panel en su estado final, sin animación.</summary>
    private void ForceFinalState(IReadOnlyList<View> rows)
    {
        Scrim.Opacity = 0.55;

        Card.Opacity = 1;
        Card.TranslationY = 0;
        Card.Scale = 1;

        Badge.Opacity = 1;
        Badge.Scale = 1;

        foreach (var row in rows)
        {
            row.Opacity = 1;
            row.TranslationY = 0;
        }
    }
}
