namespace Permoda.Pay.Maui.Common.Behaviors;

/// <summary>
/// Hace que un escaneo nuevo REEMPLACE al anterior en vez de pegarse detrás.
/// <para>
/// El lector wedge escribe como un teclado: descarga el serial carácter por carácter y termina
/// con Enter. Si el campo ya traía el serial de la consulta anterior, el nuevo quedaba concatenado
/// —<c>11381700255159371138170025515938</c>— y el cajero tenía que acordarse de borrar antes de
/// cada escaneo. Peor: el resultado de esa concatenación es un serial inválido, así que el error
/// aparecía recién al consultar y no decía nada del verdadero problema.
/// </para>
/// <para>
/// Cómo funciona: al recibir Enter el campo queda "consumido". El primer carácter que entre
/// después se toma como el comienzo de una captura nueva y se descarta lo que hubiera. A partir
/// del segundo carácter el campo se comporta normal, así que el resto del escaneo —y cualquier
/// corrección que el cajero escriba a mano— se acumula como siempre.
/// </para>
/// </summary>
public sealed class ScanReplaceBehavior : Behavior<Entry>
{
    private Entry? _entry;

    /// <summary>El campo ya se usó; lo próximo que llegue empieza de cero.</summary>
    private bool _consumed;

    /// <summary>Evita reentrada: reescribir Text dentro de TextChanged vuelve a dispararlo.</summary>
    private bool _rewriting;

    protected override void OnAttachedTo(Entry bindable)
    {
        base.OnAttachedTo(bindable);
        _entry = bindable;
        bindable.Completed += OnCompleted;
        bindable.TextChanged += OnTextChanged;
    }

    protected override void OnDetachingFrom(Entry bindable)
    {
        bindable.Completed -= OnCompleted;
        bindable.TextChanged -= OnTextChanged;
        _entry = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnCompleted(object? sender, EventArgs e) => _consumed = true;

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_rewriting || _entry is null)
        {
            return;
        }

        var oldText = e.OldTextValue ?? string.Empty;
        var newText = e.NewTextValue ?? string.Empty;

        // Si el campo quedó vacío (lo limpió el ViewModel al agregar a la lista, por ejemplo),
        // no hay nada que reemplazar y la próxima captura ya empieza limpia.
        if (newText.Length == 0)
        {
            _consumed = false;
            return;
        }

        if (!_consumed)
        {
            return;
        }

        // Solo cuenta como "captura nueva" si se AGREGÓ texto al final. Si el cajero borró, está
        // corrigiendo a mano lo que ya había y quitárselo sería pelearle al usuario.
        if (newText.Length <= oldText.Length || !newText.StartsWith(oldText, StringComparison.Ordinal))
        {
            _consumed = false;
            return;
        }

        _consumed = false;

        var typed = newText[oldText.Length..];

        _rewriting = true;
        _entry.Text = typed;
        _entry.CursorPosition = typed.Length;
        _rewriting = false;
    }
}
