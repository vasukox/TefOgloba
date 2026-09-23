using Permoda.Pay.Maui.HiPos;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Puente entre el intent de HiPOS y las pantallas del módulo.
/// <para>
/// Cuando HiPOS ordena un cobro, el TEF no dibuja una pantalla propia: levanta la app en
/// Activar bono o en Redimir saldo —las mismas que el cajero usa a diario— y espera aquí a que
/// él termine de capturar. Sin esto, HiPOS abría un formulario nativo que no se parecía a nada
/// del resto de la app.
/// </para>
/// <para>
/// Es estático a propósito: la Activity de HiPOS y la de MAUI son dos Activities distintas, no
/// comparten contenedor de DI ni ciclo de vida. Solo puede haber un cobro en curso —HiPOS no
/// lanza dos a la vez—, así que un único slot alcanza y hace evidente cuándo hay uno.
/// </para>
/// </summary>
public static class HiPosFlow
{
    private static readonly object Gate = new();
    private static HiPosFlowRequest? _current;

    /// <summary>
    /// La pantalla que está abierta la levantó HiPOS.
    /// <para>
    /// Se necesita aparte de <see cref="Current"/> porque el cierre ocurre DESPUÉS de
    /// <see cref="Complete"/>, que ya dejó <c>Current</c> en null. Preguntar por
    /// <c>IsActive</c> dentro de <see cref="CloseModuleScreen"/> daba siempre falso, y la
    /// pantalla se cerraba con la estrategia de la apertura manual — que es la contraria.
    /// </para>
    /// <para>
    /// Se prende al empezar un cobro y se apaga al cerrar la pantalla, que son los dos únicos
    /// momentos en que cambia. Un dato explícito con un dueño claro, en vez de deducirlo de
    /// estado residual que queda colgado cuando un flujo se abandona.
    /// </para>
    /// </summary>
    private static bool _screenOpenedByHiPos;


    /// <summary>El cobro en curso, o <c>null</c> si el cajero está usando la app por su cuenta.</summary>
    public static HiPosFlowRequest? Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    public static bool IsActive => Current is not null;

    /// <summary>
    /// Empezó un cobro NUEVO. Las pantallas se enganchan acá para volver a cero.
    /// <para>
    /// Existe porque el reinicio NO puede depender del ciclo de vida de la página. Al terminar un
    /// cobro la pantalla no se destruye —<see cref="CloseModuleScreen"/> manda la tarea al fondo,
    /// no hace Finish—, así que el Shell sigue montado en Redimir. Cuando llega el siguiente
    /// cobro, navegar a la ruta donde YA estás no vuelve a disparar <c>OnAppearing</c>, y con él
    /// se perdía lo único que preparaba la pantalla.
    /// </para>
    /// <para>
    /// El síntoma en mostrador: un pago combinado —parte en efectivo, el resto con bono— mostraba
    /// el total de la venta ANTERIOR en vez del saldo que faltaba, y los bonos del cobro pasado
    /// seguían en la lista. Cerrando del todo y relanzando no pasaba, porque ahí la pantalla sí se
    /// reconstruía. Medido en terminal el 2026-09-22.
    /// </para>
    /// <para>
    /// Atarlo al COBRO y no a la pantalla hace que funcione igual se haya recreado la página, esté
    /// abierta, o el cajero venga de otra pestaña.
    /// </para>
    /// </summary>
    public static event Action? FlowStarted;

    /// <summary>
    /// Registra el cobro que pide HiPOS y devuelve la espera. La Activity de HiPOS se queda en
    /// este Task hasta que la pantalla del módulo llame a <see cref="Complete"/>.
    /// </summary>
    public static Task<HiPosOperationOutcome?> Begin(
        HiPosSaleIntent intent,
        long amountPesos,
        string currency)
    {
        var completion = new TaskCompletionSource<HiPosOperationOutcome?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Gate)
        {
            // Si quedó uno colgado (la Activity murió sin responder), se cierra como cancelado
            // antes de abrir el nuevo: dos esperas vivas dejarían a HiPOS sin respuesta.
            _current?.Completion.TrySetResult(null);
            _current = new HiPosFlowRequest(intent, amountPesos, currency, completion);
            _screenOpenedByHiPos = true;
        }

        // FUERA del lock: un suscriptor que toque la UI se despacha al hilo principal, y sostener
        // el candado mientras corre código ajeno es cómo se fabrica un interbloqueo.
        //
        // Y envuelto: una pantalla que falle al reiniciarse NO puede impedir que el cobro arranque.
        // Lo peor que puede pasar acá es quedarse sin responderle a HiPOS.
        try
        {
            FlowStarted?.Invoke();
        }
        catch (Exception exception)
        {
#if ANDROID
            Android.Util.Log.Error(
                "TefOgloba.HiPos",
                $"Una pantalla falló al reiniciarse para el cobro nuevo: {exception}");
#endif
        }

        return completion.Task;
    }

    /// <summary>
    /// Cierra el cobro en curso. <c>null</c> significa que el cajero salió sin completarlo.
    /// Llamarlo de más es inofensivo: si no hay cobro en curso, no hace nada.
    /// </summary>
    public static void Complete(HiPosOperationOutcome? outcome)
    {
        HiPosFlowRequest? current;

        lock (Gate)
        {
            current = _current;
            _current = null;
        }

        // Se registra acá porque es el momento exacto en que la pantalla suelta el resultado.
        // Sin esta línea no había forma de distinguir "el cajero no terminó" de "terminó y algo
        // se perdió en el camino": las dos se veían como silencio.
#if ANDROID
        Android.Util.Log.Info(
            "TefOgloba.HiPos",
            "El módulo cierra el cobro → {0}",
            outcome is null
                ? "SIN RESULTADO (el cajero salió sin operar)"
                : $"aceptado={outcome.Accepted} pendiente={outcome.Pending} "
                  + $"aplicado={outcome.AmountAppliedPesos} ref={outcome.Reference} "
                  + $"error={outcome.ErrorMessage ?? "-"}");
#endif

        current?.Completion.TrySetResult(outcome);
    }

    /// <summary>
    /// Cierra la pantalla del módulo para devolverle el control a HiPOS.
    /// <para>
    /// El <c>ClearFocus</c> no es cosmético: si el campo de texto sigue enfocado cuando la
    /// Activity se destruye, el <c>EditText</c> dispara <c>OnFocusChange</c> y MAUI intenta
    /// resolver un servicio del contenedor de DI que ya se desechó
    /// (<c>ObjectDisposedException: IServiceProvider</c> en <c>InputView.MapIsFocused</c>). Eso
    /// mataba el proceso justo al terminar el cobro, con el resultado ya entregado.
    /// </para>
    /// <para>
    /// El cierre va diferido un tick para que Android alcance a procesar la pérdida de foco
    /// antes de empezar a destruir la Activity.
    /// </para>
    /// </summary>
    public static void CloseModuleScreen()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        // Se lee y se apaga ACÁ, antes de diferir el cierre: para cuando corra el callback la
        // pantalla ya no está abierta, y dejarlo prendido haría que la próxima apertura manual
        // se cerrara como si viniera de HiPOS.
        bool hiPosDriven;

        lock (Gate)
        {
            hiPosDriven = _screenOpenedByHiPos;
            _screenOpenedByHiPos = false;
        }

        if (activity is null)
        {
            return;
        }

        activity.CurrentFocus?.ClearFocus();

        var decorView = activity.Window?.DecorView;

        if (decorView is not null)
        {
            decorView.PostDelayed(CloseOrSendToBack, 80);
        }
        else
        {
            CloseOrSendToBack();
        }

        // En flujo HiPOS-driven: la MainActivity vive en la MISMA tarea que HiPOS
        // (SaleCapture ya no usa ActivityFlags.NewTask). MoveTaskToBack aquí arrastraría a
        // HiPOS al fondo junto con la MainActivity y el POS nunca recuperaría el foco.
        // Finish() saca solo la MainActivity de la pila; HiPOS queda en primer plano y la
        // siguiente venta empieza con la pila limpia. No hay MAUI huérfana que pueda
        // quedarse sin Shell —que era el bug que motivaba el MoveTaskToBack original— porque
        // cada venta recrea la MainActivity desde cero.
        //
        // Fuera de flujo HiPOS (apertura manual): se sigue mandando al fondo igual que antes,
        // para que el cajero pueda volver a la app sin perder el Shell.
        void CloseOrSendToBack()
        {
            try
            {
                // El módulo tiene TAREA PROPIA (ver SaleCapture: se lanza con NewTask), así que
                // mandarla al fondo deja al descubierto la tarea de HiPOS —que nunca se tocó— y
                // el POS vuelve al frente con su venta intacta. Eso es "volver a HiPOS".
                //
                // No se usa Finish(): destruir la MainActivity en cada cobro hacía que MAUI
                // reconstruyera el Shell y reventara. Al fondo, la sesión queda viva y el
                // siguiente cobro la encuentra sana.
                //
                // Se probó Finish() con tarea compartida el 2026-09-02 y se revirtió: nuestra
                // pantalla quedaba en la misma pila que la del módulo fiscal y lo tumbaba a
                // mitad de la facturación. Ver la nota en SaleCapture.
                //
                // hiPosDriven ya no decide la estrategia —es la misma en los dos casos— pero se
                // sigue leyendo y limpiando arriba: es el rastro explícito de si la pantalla la
                // abrió el POS, y lo necesitamos si algún día vuelven a divergir.
                _ = hiPosDriven;

                activity.MoveTaskToBack(true);
            }
            catch (Exception)
            {
                // Si no se puede (la Activity ya no está en primer plano), no pasa nada: HiPOS
                // ya tiene su respuesta y es quien manda la pantalla.
            }
        }
#endif
    }
}

/// <param name="Intent">Activar (venta) o Redimir (abono), según lo decidió HiPOS.</param>
/// <param name="AmountPesos">Importe de la factura, en pesos enteros.</param>
public sealed record HiPosFlowRequest(
    HiPosSaleIntent Intent,
    long AmountPesos,
    string Currency,
    TaskCompletionSource<HiPosOperationOutcome?> Completion);
