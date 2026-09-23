using System.ComponentModel;

namespace Permoda.Pay.Maui.Common.Behaviors;

/// <summary>
/// Entrada suave (fade + slide desde abajo + leve scale) para el primer render de un
/// bloque. Se dispara una sola vez cuando el behavior se adjunta. Usar
/// <see cref="DelayMs"/> para escalonar varios bloques en la misma pantalla
/// (60, 120, 180…). Curva de easing <see cref="Easing.CubicInOut"/> (en lugar del
/// <see cref="Easing.CubicOut"/> plano) y duración 360ms para un feel "premium" — la
/// salida se siente con peso, no como un fade genérico.
/// </summary>
public sealed class EntranceBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty DelayMsProperty = BindableProperty.Create(
        nameof(DelayMs), typeof(int), typeof(EntranceBehavior), 0);

    public int DelayMs
    {
        get => (int)GetValue(DelayMsProperty);
        set => SetValue(DelayMsProperty, value);
    }

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.Opacity = 0;
        bindable.TranslationY = 12;
        bindable.Scale = 0.97;

        bindable.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(DelayMs), async () =>
        {
            await Task.WhenAll(
                bindable.FadeToAsync(1, 360, Easing.CubicInOut),
                bindable.TranslateToAsync(0, 0, 360, Easing.CubicInOut),
                bindable.ScaleToAsync(1, 360, Easing.CubicInOut));
        });
    }
}

/// <summary>
/// Aparición tipo "pop" (0 → 1.12 → 1 con fade) para íconos de resultado (✓/✗). Se dispara cada
/// vez que cambia el BindingContext, así que sirve para animar resultados sucesivos (activar
/// varios bonos seguidos) sin recrear la vista.
/// </summary>
public sealed class PopInBehavior : Behavior<VisualElement>
{
    private VisualElement? _target;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _target = bindable;
        bindable.BindingContextChanged += OnBindingContextChanged;
        Animate(bindable);
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.BindingContextChanged -= OnBindingContextChanged;
        _target = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_target is { } target)
        {
            Animate(target);
        }
    }

    private static async void Animate(VisualElement target)
    {
        target.Opacity = 0;
        target.Scale = 0;

        await Task.WhenAll(
            target.FadeToAsync(1, 240, Easing.CubicOut),
            AnimatePopScale(target));
    }

    private static async Task AnimatePopScale(VisualElement target)
    {
        await target.ScaleToAsync(1.12, 240, Easing.CubicOut);
        await target.ScaleToAsync(1, 140, Easing.CubicIn);
    }
}

/// <summary>
/// Feedback táctil explícito (Scale 0.98 al presionar) para controles que no son <see cref="Button"/>
/// — por ejemplo un <see cref="Border"/> usado como tarjeta clicable — donde el VisualStateManager
/// "Pressed" de los estilos de botón no aplica. Requiere que el elemento tenga un
/// <see cref="TapGestureRecognizer"/> para recibir el toque; este behavior solo anima la escala.
/// </summary>
public sealed class PressableBehavior : Behavior<View>
{
    private TapGestureRecognizer? _recognizer;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        _recognizer = new TapGestureRecognizer();
        _recognizer.Tapped += async (_, _) =>
        {
            await bindable.ScaleToAsync(0.98, 60, Easing.CubicOut);
            await bindable.ScaleToAsync(1.0, 90, Easing.CubicOut);
        };

        bindable.GestureRecognizers.Add(_recognizer);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        if (_recognizer is not null)
        {
            bindable.GestureRecognizers.Remove(_recognizer);
            _recognizer = null;
        }

        base.OnDetachingFrom(bindable);
    }
}

/// <summary>
/// Entrada que se REPITE cada vez que el elemento vuelve a hacerse visible, a diferencia de
/// <see cref="EntranceBehavior"/>, que solo corre al montarse. Es lo que necesita el panel de
/// resultado: aparece al terminar un lote, se oculta al empezar el siguiente y debe volver a
/// entrar con la misma autoridad.
/// <para>
/// Sube 18dp con desaceleración exponencial y sin rebote. Solo se animan Opacity, TranslationY y
/// Scale — animar Width/Height es lento en MAUI y está prohibido por el design system.
/// </para>
/// </summary>
public sealed class RevealBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty DelayMsProperty = BindableProperty.Create(
        nameof(DelayMs), typeof(int), typeof(RevealBehavior), 0);

    /// <summary>Retardo para escalonar hermanos (0, 70, 140…). Tope recomendado: 4 elementos.</summary>
    public int DelayMs
    {
        get => (int)GetValue(DelayMsProperty);
        set => SetValue(DelayMsProperty, value);
    }

    private VisualElement? _target;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _target = bindable;
        bindable.PropertyChanged += OnPropertyChanged;

        if (bindable.IsVisible)
        {
            _ = RevealAsync(bindable, DelayMs);
        }
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.PropertyChanged -= OnPropertyChanged;
        _target = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualElement.IsVisible)
            && _target is { IsVisible: true } target)
        {
            _ = RevealAsync(target, DelayMs);
        }
    }

    private static async Task RevealAsync(VisualElement target, int delayMs)
    {
        target.CancelAnimations();
        target.Opacity = 0;
        target.TranslationY = 18;
        target.Scale = 0.98;

        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
        }

        await Task.WhenAll(
            target.FadeToAsync(1, 420, Easing.CubicOut),
            target.TranslateToAsync(0, 0, 420, Easing.CubicOut),
            target.ScaleToAsync(1, 420, Easing.CubicOut));
    }
}

/// <summary>
/// Pulso (fade + scale) cuando una propiedad espec�fica del BindingContext cambia de valor.
/// Pensado para animar filas del carrito cuando su <c>Status</c> transita
/// Pending ? Processing ? Approved/Rejected: cada cambio dispara un latido visual que reemplaza
/// al toast. Adjuntar al <c>Border</c> de la fila y setear <see cref="PropertyName"/> = "Status".
/// </summary>
public sealed class PulseOnPropertyChangeBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty PropertyNameProperty = BindableProperty.Create(
        nameof(PropertyName), typeof(string), typeof(PulseOnPropertyChangeBehavior), default(string));

    public string? PropertyName
    {
        get => (string?)GetValue(PropertyNameProperty);
        set => SetValue(PropertyNameProperty, value);
    }

    private VisualElement? _target;
    private INotifyPropertyChanged? _context;
    private PropertyChangedEventHandler? _handler;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _target = bindable;
        bindable.BindingContextChanged += OnBindingContextChanged;
        UpdateSubscription(bindable.BindingContext);
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.BindingContextChanged -= OnBindingContextChanged;
        Unsubscribe();
        _target = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_target is { } target)
        {
            UpdateSubscription(target.BindingContext);
        }
    }

    private void UpdateSubscription(object? context)
    {
        Unsubscribe();
        if (context is INotifyPropertyChanged inpc && !string.IsNullOrEmpty(PropertyName))
        {
            _context = inpc;
            _handler = (s, e) =>
            {
                if (e.PropertyName == PropertyName && _target is { } target)
                {
                    _ = AnimateAsync(target);
                }
            };
            inpc.PropertyChanged += _handler;
        }
    }

    private void Unsubscribe()
    {
        if (_context is not null && _handler is not null)
        {
            _context.PropertyChanged -= _handler;
        }
        _context = null;
        _handler = null;
    }

    /// <summary>
    /// Realce breve: la fila sube un poco y vuelve.
    /// <para>
    /// Antes parpadeaba —bajaba a 35% de opacidad y encogía— y se leía como si la fila se
    /// fuera a desaparecer justo cuando el cajero acababa de agregarla. Un cambio de estado no
    /// es una salida: la fila tiene que confirmarse, no desvanecerse. Por eso ahora solo se
    /// desplaza, sin tocar la opacidad.
    /// </para>
    /// </summary>
    private static async Task AnimateAsync(VisualElement target)
    {
        target.CancelAnimations();

        target.Opacity = 1;

        await target.TranslateToAsync(0, -6, 110, Easing.CubicOut);
        await target.TranslateToAsync(0, 0, 220, Easing.CubicOut);
    }
}
