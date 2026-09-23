using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;

namespace Permoda.Pay.Maui;

// Ogloba.Launch en vez de Maui.SplashTheme: sin barra de título y con el fondo de la app, para
// que abrir el módulo no muestre una ventana en blanco con el nombre encima antes de la pantalla
// real. Ver Resources/values/styles.xml.
// KeyboardHidden, Keyboard, Locale, LayoutDirection y FontScale se agregaron a la lista que ya
// estaba: son los cambios de configuración que faltaban. Sin declararlos, Android RECREA la
// Activity cuando el cajero abre o cierra el teclado —cosa que hace en cada escaneo manual— y el
// estado en memoria se pierde a mitad de una operación. El síntoma no se parece a la causa.
[Activity(
    Theme = "@style/Ogloba.Launch",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
                         | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                         | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
                         | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard
                         | ConfigChanges.Locale | ConfigChanges.LayoutDirection
                         | ConfigChanges.FontScale)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Ya se mandó al selector de cajero en esta vida de la Activity. Sin esto, cualquier
    /// OnResume posterior —volver de la firma en la pantalla del cliente, por ejemplo— sacaría
    /// al cajero de lo que estaba haciendo.
    /// </summary>
    private bool _askedForCashier;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // DIAG TEMPORAL: prueba de humo del logging, ANTES de que corra
        // cualquier código de MAUI/MauiProgram. Si esto no aparece en logcat, el problema
        // es de visibilidad de logs, no de nuestro código de arranque.
        Log.Error("TefOgloba", "[MainActivity] OnCreate: ENTERED (before base.OnCreate)");

        try
        {
            base.OnCreate(savedInstanceState);
            Log.Error("TefOgloba", "[MainActivity] OnCreate: base.OnCreate completed OK");
        }
        catch (Exception exception)
        {
            Log.Error("TefOgloba", $"[MainActivity] OnCreate: base.OnCreate THREW: {exception}");
            throw;
        }
    }

    /// <summary>
    /// Toda entrada a la app pasa primero por "Seleccione su cajero" — la levante HiPOS para un
    /// cobro o la abra el cajero a mano.
    /// <para>
    /// Se pregunta siempre porque la operación queda firmada con ese cajero (en la bitácora y
    /// en lo que se le manda a Ogloba) y en una caja compartida el turno anterior puede haber
    /// quedado abierto con otra persona. Un toque de más contra atribuirle plata a quien no la
    /// movió.
    /// </para>
    /// <para>
    /// Va en <c>OnResume</c> y no en <c>OnCreate</c> porque la app puede ya estar abierta: con
    /// <c>SingleTop</c>, Android la reutiliza y <c>OnCreate</c> no vuelve a correr. El guard de
    /// <see cref="_askedForCashier"/> evita que volver de la pantalla de firma —que también
    /// dispara OnResume— rebote al cajero al selector a mitad de una activación.
    /// </para>
    /// </summary>
    /// <summary>
    /// La app se reutiliza entre cobros (vive en su propia tarea y se manda al fondo en vez de
    /// cerrarse), así que un cobro nuevo llega por aquí y no por <c>OnCreate</c>. Se reabre el
    /// selector de cajero para que la operación quede firmada por quien realmente la hace.
    /// </summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        _askedForCashier = false;
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Una sola vez por entrada. Un cobro nuevo llega por OnNewIntent, que baja el guard;
        // volver de otra app o de apagar la pantalla NO debe sacar al cajero de lo que estaba
        // haciendo. Antes esto se saltaba el guard mientras hubiera un cobro de HiPOS en curso,
        // así que cualquier resume a mitad de la captura lo devolvía al principio.
        if (_askedForCashier)
        {
            return;
        }

        _askedForCashier = true;

        // El Shell puede no estar montado todavía en el primer arranque; se reintenta en el
        // dispatcher hasta que exista, porque si no navegamos el cajero queda en Inicio con un
        // cobro de HiPOS esperando y sin saber qué tocar.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Sondeo corto: 25 ms en vez de 100. El Shell suele estar montado en el primer o
            // segundo intento, y con 100 ms se le sumaba hasta una décima de espera a algo que
            // ya estaba listo — eso era buena parte de la sensación de lentitud al abrir.
            // La ventana total sigue siendo la misma (~4 s) para el arranque en frío.
            for (var attempt = 0; attempt < 160 && Shell.Current is null; attempt++)
            {
                await Task.Delay(25);
            }

            if (Shell.Current is null)
            {
                Log.Error("TefOgloba", "[MainActivity] Cobro HiPOS sin Shell: no se pudo navegar.");
                return;
            }

            // El destino se decide ACÁ, antes de navegar. Antes se iba siempre al selector de
            // cajero y era esa pantalla la que rebotaba a Configuración si la terminal no
            // estaba configurada — el cajero alcanzaba a ver el selector de refilón antes del
            // salto. Una terminal recién instalada tiene que abrir directo en Configuración.
            var route = Common.AppRoutes.CashierLogin;

            if (AppServices.Current?.GetService<Services.PosSession>() is { } session)
            {
                try
                {
                    await session.EnsureLoadedAsync(CancellationToken.None);

                    // "Configurada" no es solo tener la tienda: la primera configuración es
                    // donde el administrador crea el PIN y da de alta a los cajeros. Mientras
                    // falte cualquiera de las tres cosas, la app abre en Configuración —
                    // mandarla al selector con la lista vacía deja al cajero sin nada que tocar.
                    var cashiers = session.IsConfigured
                        ? await session.GetCashiersAsync(CancellationToken.None)
                        : [];

                    // Cobro ordenado por HiPOS: se entra DERECHO a operar, sin preguntar cajero.
                    //
                    // Acá el cajero ya se identificó en el POS para poder facturar, y el POS está
                    // esperando una respuesta: meterle una pantalla de login en medio de una venta
                    // es fricción sobre una identificación que ya ocurrió. La apertura manual sí
                    // la pide, porque ahí no hubo ninguna.
                    if (Services.HiPosFlow.Current is { } flow)
                    {
                        route = flow.Intent == Permoda.Pay.Maui.HiPos.HiPosSaleIntent.Activation
                            ? Common.AppRoutes.Activate
                            : Common.AppRoutes.Redeem;
                    }
                    else
                    {
                        // Apertura manual: siempre cajero y contraseña. La operación se firma con
                        // ese nombre —en la bitácora y en lo que se le manda a Ogloba— y en una
                        // caja compartida el turno anterior puede ser de otra persona.
                        route = !session.IsAdminReady
                            ? Common.AppRoutes.Setup            // paso 1 y 2: PIN e identidad
                            : cashiers.Count == 0
                                ? Common.AppRoutes.CashierSetup // paso 3: registrar cajeros
                                : Common.AppRoutes.CashierLogin; // quién opera

                        // El turno anterior se cierra ANTES de preguntar. Si no, el encabezado
                        // muestra al cajero saliente mientras el entrante digita su contraseña.
                        if (route == Common.AppRoutes.CashierLogin)
                        {
                            session.LogoutCashier();
                        }
                    }
                }
                catch (Exception exception)
                {
                    // Sin poder leer la configuración se asume lo más seguro: pedirla.
                    Log.Error("TefOgloba", $"[MainActivity] No se pudo leer la configuración: {exception}");
                    route = Common.AppRoutes.Setup;
                }
            }

            try
            {
                // El selector es una ruta GLOBAL, y una global no puede ser la única página de
                // la pila ("Global routes currently cannot be the only page on the stack"). Se
                // asienta primero una raíz y se apila encima.
                if (route == Common.AppRoutes.CashierLogin)
                {
                    await Shell.Current.GoToAsync(Common.AppRoutes.Home);
                }

                await Shell.Current.GoToAsync(route);
            }
            catch (Exception exception)
            {
                Log.Error("TefOgloba", $"[MainActivity] No se pudo abrir {route}: {exception}");
            }
        });
    }
}
