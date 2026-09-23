using Android.Util;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui;

public partial class AppShell : Shell
{
    private const string LogTag = "TefOgloba";

    // Pestañas operativas: exigen terminal configurada. "setup" queda fuera a propósito (el
    // admin debe poder entrar a Config sin cajero, protegido por su propio PIN) y "login"
    // tampoco se evalúa (es la ruta modal, no una pestaña).
    private static readonly string[] GatedRoutes = { "home", "activate", "redeem", "balance", "audit" };

    // El chequeo de "¿hay cajero en turno?" solo se hace UNA VEZ, al abrir la app (ver más abajo
    // por qué). El de "¿está configurada la terminal?" sí corre en cada navegación.
    private bool _hasCheckedCashierOnStart;

    // Bug de primer arranque: al ingresar el cajero, CashierLoginViewModel hace PopToRootAsync
    // y enseguida GoToAsync("//home"). El Pop dispara su propio Navigated, este guard corría
    // sobre un estado intermedio y empujaba Configuración; medio segundo después el GoToAsync
    // de login la reemplazaba. Resultado visible: la pantalla de contraseña aparecía y
    // desaparecía sola. Con este flag el guard ignora las navegaciones que ocurren mientras él
    // mismo está redirigiendo, que es cuando el estado no es de fiar.
    private bool _isRedirecting;

    public AppShell(PosHeaderViewModel header)
    {
        Log.Debug(LogTag, "[AppShell] ctor: start (before InitializeComponent)");
        InitializeComponent();
        Log.Debug(LogTag, "[AppShell] ctor: InitializeComponent done");

        WireFlyoutHeader(header);

        // El guard de "terminal sin configurar" corre en CADA Navigated (no solo el primero):
        // el TabBar declara las 5 pestañas siempre visibles/habilitadas, así que sin un chequeo
        // persistente el usuario puede tocar "Inicio" y saltarse Configuración/PIN antes de
        // completarlos (Bug #3 del diagnóstico).
        Navigated += OnShellNavigated;
    }

    private void WireFlyoutHeader(PosHeaderViewModel header)
    {
        // El FlyoutHeader (cabecera del menú hamburguesa) y el FlyoutFooter muestran datos
        // del header compartido (tienda, cajero, ambiente). Sin este BindingContext el header
        // queda con DataContext nulo y los {Binding …} no resuelven.
        if (FlyoutHeader is Microsoft.Maui.Controls.View headerView)
        {
            headerView.BindingContext = header;
        }

        if (FlyoutFooter is Microsoft.Maui.Controls.View footerView)
        {
            footerView.BindingContext = header;
        }
    }

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (_isRedirecting)
        {
            return;
        }

        try
        {
            // COBRO ORDENADO POR HiPOS: no hay guard que valga. El POS está esperando respuesta y
            // el cajero ya se identificó allá para poder facturar; en este flujo el turno del
            // módulo sencillamente no existe.
            //
            // Sin esta salida, MainActivity navegaba derecho a Redimir y este guard le empujaba
            // encima el login de cajero: el cajero veía una pantalla de perfil en mitad de la
            // venta, que no tenía por qué contestar. La ruta ya era la correcta — lo que sobraba
            // era la pantalla que se apilaba después.
            if (Services.HiPosFlow.IsActive)
            {
                return;
            }

            var location = e.Current?.Location?.OriginalString ?? string.Empty;

            if (!IsGatedRoute(location))
            {
                return;
            }

            // Si el login o la configuración siguen en la pila, la navegación que acabamos de
            // recibir es un paso intermedio (p. ej. el PopToRoot que cierra el modal de cajero):
            // no hay nada que decidir todavía.
            if (location.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                location.Contains("setup", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var configured =
                Preferences.Default.Get(PosSession.SetupCompletedPreferenceKey, false) &&
                Preferences.Default.Get(PosSession.AdminPinSetPreferenceKey, false);

            if (!configured)
            {
                // Terminal sin configurar: cualquier pestaña operativa rebota a Configuración.
                // Esta comprobación SÍ debe repetirse en cada toque de pestaña.
                _isRedirecting = true;
                try
                {
                    await GoToAsync(AppRoutes.Setup);
                }
                finally
                {
                    _isRedirecting = false;
                }

                return;
            }

            if (_hasCheckedCashierOnStart)
            {
                // Ya se hizo el chequeo de cajero al abrir la app. De aquí en más el cajero
                // puede navegar libremente entre pestañas aunque cierre turno ("Cambiar
                // cajero" empuja su propio modal de login solo sobre la pestaña donde se tocó,
                // eso ya es suficiente). Los comandos de Activar/Redimir validan por su cuenta
                // "Sin cajero" con su propio banner antes de dejar operar.
                return;
            }

            _hasCheckedCashierOnStart = true;

            var activeCashier = Preferences.Default.Get(PosSession.ActiveCashierPreferenceKey, string.Empty);

            if (string.IsNullOrWhiteSpace(activeCashier))
            {
                // Primer arranque de esta sesión de Shell, configurado pero sin turno abierto:
                // sugerir el ingreso de cajero una sola vez (ruta modal registrada en
                // MauiProgram.cs, no es una pestaña más del Shell).
                _isRedirecting = true;
                try
                {
                    await GoToAsync(AppRoutes.CashierLogin);
                }
                finally
                {
                    _isRedirecting = false;
                }
            }
        }
        catch (Exception exception)
        {
            // Blindaje: si GoToAsync falla (ruta inexistente, plataforma ocupada, etc.),
            // mostramos el tab por defecto en vez de dejar el Shell sin contenido.
            Log.Error(LogTag, $"[AppShell] Navigation guard failed: {exception}");
        }
    }

    private static bool IsGatedRoute(string location)
    {
        foreach (var route in GatedRoutes)
        {
            if (location.Contains(route, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Comando reutilizable para "Volver al inicio" desde cualquier pestaña operativa
    /// (Activar / Redimir / Consultar). Se asigna en XAML con
    /// <c>{x:Static shell:AppShell.BackHomeCommand}</c>.
    /// </summary>
    public static System.Windows.Input.ICommand BackHomeCommand { get; } =
        new Command(async () => await Shell.Current.GoToAsync(AppRoutes.Home));

    /// <summary>
    /// Abre el menú lateral. Las pantallas operativas ocultan la barra del Shell
    /// (<c>Shell.NavBarIsVisible="False"</c>) para no duplicar encabezado, y con ella se iba el
    /// botón de hamburguesa: sin esto el menú queda inalcanzable salvo por gesto de borde.
    /// </summary>
    public static System.Windows.Input.ICommand OpenMenuCommand { get; } =
        new Command(() =>
        {
            if (Shell.Current is { } shell)
            {
                shell.FlyoutIsPresented = true;
            }
        });
}