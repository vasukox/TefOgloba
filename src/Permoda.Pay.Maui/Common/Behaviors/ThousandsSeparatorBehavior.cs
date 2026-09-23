namespace Permoda.Pay.Maui.Common.Behaviors;

/// <summary>
/// Muestra el importe con separador de miles (<c>50000</c> → <c>50.000</c>) mientras el cajero
/// teclea, y de nuevo al salir del campo por si el texto llegó por binding o por el lector.
/// <para>
/// El formateo en vivo tumbaba la app en la terminal C9H
/// (<c>IllegalArgumentException: end should be &lt; than charSequence length</c>) porque el
/// watcher de emoji2 que Android encadena a cada campo procesaba el texto con la longitud
/// anterior. La causa se elimina en <c>MauiProgram.DisableEmojiProcessingOnEntries</c>, no aquí:
/// sin ese watcher, reescribir el texto desde el evento es seguro.
/// </para>
/// <para>
/// El valor sigue viajando como texto y todos los consumidores lo leen con
/// <see cref="MoneyInput.TryParsePesos"/>, que descarta los separadores: el campo puede mostrar
/// puntos sin que ningún ViewModel tenga que saberlo.
/// </para>
/// </summary>
public sealed class ThousandsSeparatorBehavior : Behavior<Entry>
{
    private bool _isFormatting;

    protected override void OnAttachedTo(Entry bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.TextChanged += OnTextChanged;
        bindable.Unfocused += OnFormatRequested;
        bindable.Completed += OnFormatRequested;
    }

    protected override void OnDetachingFrom(Entry bindable)
    {
        bindable.TextChanged -= OnTextChanged;
        bindable.Unfocused -= OnFormatRequested;
        bindable.Completed -= OnFormatRequested;
        base.OnDetachingFrom(bindable);
    }

    /// <summary>
    /// Formatea mientras el cajero teclea. Es seguro porque <c>MauiProgram</c> desactiva el
    /// watcher de emoji2 en todos los Entry: ese watcher era el que reventaba al reescribir el
    /// texto desde este evento. El guardia evita la reentrada de nuestro propio cambio.
    /// </summary>
    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormatting || sender is not Entry entry)
        {
            return;
        }

        var formatted = MoneyInput.Format(e.NewTextValue);

        if (string.IsNullOrEmpty(formatted) || formatted == e.NewTextValue)
        {
            return;
        }

        _isFormatting = true;

        try
        {
            entry.Text = formatted;

            // Solo se toca el cursor si la longitud ya coincide: moverlo a ciegas es otra vía
            // para el mismo desajuste de índices que causó el crash.
            if (entry.Text is { } current && current.Length == formatted.Length)
            {
                entry.CursorPosition = formatted.Length;
            }
        }
        finally
        {
            _isFormatting = false;
        }
    }

    private static void OnFormatRequested(object? sender, EventArgs e)
    {
        if (sender is not Entry entry)
        {
            return;
        }

        var formatted = MoneyInput.Format(entry.Text);

        // Sin dígitos no hay nada que formatear: se deja lo que el cajero tenga escrito para no
        // borrarle el campo por sorpresa.
        if (string.IsNullOrEmpty(formatted) || formatted == entry.Text)
        {
            return;
        }

        entry.Text = formatted;
    }
}
