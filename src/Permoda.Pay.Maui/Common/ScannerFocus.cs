namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Deja un campo listo para el lector de código de barras sin quitarle al cajero la posibilidad
/// de escribir a mano.
/// <para>
/// El lector wedge escribe como si fuera un teclado: si ningún campo tiene el cursor, escanear no
/// hace nada. Por eso se enfoca el campo al entrar a la pantalla. Pero enfocarlo a secas levanta
/// el teclado en pantalla, que en la terminal tapa media interfaz.
/// </para>
/// <para>
/// La solución anterior era <c>ShowSoftInputOnFocus = false</c> y nada más. Eso funcionaba para el
/// lector y rompía la escritura: la propiedad NO es momentánea, se queda pegada en el campo, así
/// que cuando el cajero tocaba el serial —o el correo del cliente, que comparte el mismo campo en
/// Activar— Android le daba el cursor y jamás abría el teclado. Se veía como un campo muerto.
/// </para>
/// <para>
/// Acá se separan los dos momentos: el foco automático entra sin teclado, y el primer toque del
/// cajero lo devuelve a su comportamiento normal y abre el teclado explícitamente (ya tiene el
/// cursor, así que Android no lo abriría solo).
/// </para>
/// </summary>
public static class ScannerFocus
{
#if ANDROID
    /// <summary>
    /// Campos ya preparados. Evita encadenar un listener de toque por cada visita a la pantalla:
    /// <c>OnAppearing</c> corre muchas veces sobre la misma instancia de página.
    /// </summary>
    private static readonly HashSet<global::Android.Widget.EditText> Armed =
        new(ReferenceEqualityComparer<global::Android.Widget.EditText>.Instance);

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
#endif

    /// <summary>
    /// Pone el cursor en el campo para que el lector descargue ahí lo que escanee, sin abrir el
    /// teclado. El campo sigue siendo escribible: al tocarlo, el teclado aparece.
    /// </summary>
    /// <summary>
    /// Deja el campo listo para el lector, REINTENTANDO hasta que el handler nativo exista.
    /// <para>
    /// <see cref="Arm"/> a secas se pierde en silencio cuando se llama antes de que MAUI cree el
    /// <c>EditText</c> — que es lo normal en la primera navegación a una pantalla. El campo
    /// quedaba sin cursor, el primer escaneo no caía en ningún lado y el cajero tenía que tocar
    /// el campo y volver a escanear. Es el fallo que más se notaba en mostrador.
    /// </para>
    /// <para>
    /// Reintenta durante ~1,5 s. Si en ese tiempo la pantalla no montó el campo, es que el cajero
    /// ya se fue a otra parte y no hay nada que armar.
    /// </para>
    /// </summary>
    public static async Task ArmWhenReadyAsync(Entry? entry)
    {
        if (entry is null)
        {
            return;
        }

        const int attempts = 30;
        const int delayMs = 50;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            Arm(entry);

            // IsFocused es la única confirmación real de que el foco entró. El handler puede
            // existir y aun así no haberlo tomado todavía.
            if (entry.Handler?.PlatformView is not null && entry.IsFocused)
            {
                return;
            }

            await Task.Delay(delayMs);
        }
    }

    public static void Arm(Entry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.Focus();

#if ANDROID
        if (entry.Handler?.PlatformView is not global::Android.Widget.EditText editText)
        {
            return;
        }

        editText.ShowSoftInputOnFocus = false;
        editText.RequestFocus();

        if (!Armed.Add(editText))
        {
            return;
        }

        // Un solo listener por campo, para toda la vida del campo. No marca el toque como
        // manejado: el gesto sigue su curso normal (mover el cursor, seleccionar texto).
        editText.Touch += (_, args) =>
        {
            args.Handled = false;

            if (args.Event?.Action != global::Android.Views.MotionEventActions.Up)
            {
                return;
            }

            editText.ShowSoftInputOnFocus = true;
            ShowKeyboard(editText);
        };
#endif
    }

#if ANDROID
    /// <summary>
    /// Abre el teclado a mano. Hace falta porque el campo YA tiene el cursor —se lo dimos al
    /// entrar—, y Android solo lo abre solo cuando el toque es el que otorga el foco.
    /// </summary>
    private static void ShowKeyboard(global::Android.Widget.EditText editText)
    {
        try
        {
            if (editText.Context?.GetSystemService(global::Android.Content.Context.InputMethodService)
                is global::Android.Views.InputMethods.InputMethodManager manager)
            {
                manager.ShowSoftInput(editText, global::Android.Views.InputMethods.ShowFlags.Implicit);
            }
        }
        catch (Exception)
        {
            // Si la plataforma no entrega el servicio, el campo queda escribible igual con el
            // teclado físico o el del lector. Peor sería tumbar la pantalla por el teclado.
        }
    }
#endif
}
