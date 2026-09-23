using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Permoda.Pay.Maui.Lifecycle;

namespace Permoda.Pay.Maui;

public partial class App : Microsoft.Maui.Controls.Application
{
    // DIAG TEMPORAL: Android.Util.Log, no Console.WriteLine — Console.Out no
    // se redirige a logcat sin depurador adjunto (adb install + am start), así que un
    // Console.WriteLine puede no aparecer nunca en logcat aunque sí se ejecute.
    private const string LogTag = "TefOgloba";

    private static bool _globalHandlersInstalled;

    public App()
    {
        InstallGlobalExceptionHandlers();
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        ScheduleLifecycleBootstrap();

        try
        {
            // AppShell se resuelve del IServiceProvider del MAUI host. Antes se construía
            // con `new AppShell()` (sin dependencias), pero ahora el header del menú
            // hamburguesa necesita el PosHeaderViewModel por DI.
            var services = AppServices.Current;
            var shell = services?.GetService(typeof(AppShell)) as AppShell;

            if (shell is null)
            {
                Log.Error(LogTag, "[App] AppShell no se encontró en DI; el menú no tendrá datos.");
                throw new InvalidOperationException(
                    "AppShell no fue registrado en el contenedor de DI. Revisa MauiProgram.cs.");
            }

            return new Window(shell);
        }
        catch (Exception exception)
        {
            // Si AppShell() falla al construirse (p. ej. un XamlParseException), este es el
            // único punto donde lo veríamos antes de que la app quede con la Activity vacía.
            Log.Error(LogTag, $"[App] CreateWindow failed building AppShell: {exception}");
            throw;
        }
    }

    /// <summary>
    /// Bitácora de fallos en disco. Se construye aparte del contenedor de DI a propósito: los
    /// manejadores globales se instalan en el constructor de <see cref="App"/>, que puede correr
    /// antes de que haya servicios resueltos — y un fallo durante el arranque es justamente el que
    /// más falta hace registrar.
    /// </summary>
    private static readonly Services.CrashJournal Journal = new();

    public static Services.CrashJournal CrashJournal => Journal;

    private static void InstallGlobalExceptionHandlers()
    {
        if (_globalHandlersInstalled)
        {
            return;
        }

        _globalHandlersInstalled = true;

        // Los fallos van a DISCO además de a logcat.
        // ==========================================================================
        // logcat es un buffer circular: se borra al reiniciar la terminal y lo sobreescribe
        // cualquier app ruidosa. Con 512 tiendas eso significaba que si el módulo se caía en
        // una de ellas, nadie se enteraba nunca — el cajero perdía la venta y a lo sumo
        // llamaba a decir "la app no sirve", que no es un reporte sino un síntoma.
        //
        // El archivo sobrevive al reinicio y sale por el exportador que ya existe, así que el
        // soporte puede pedirlo por teléfono en vez de mandar a alguien con un cable.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "Fallo desconocido.");

            Journal.Record("AppDomain.UnhandledException", exception, e.IsTerminating);
            Log.Error(LogTag, $"[App] AppDomain.UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Una excepción en un Task "fire and forget" (async void, o Task nunca esperado)
            // NO crashea el proceso por defecto: queda completamente invisible. Este es el
            // sospechoso principal de una pantalla en blanco sin ningún log de error.
            Journal.Record("TaskScheduler.UnobservedTaskException", e.Exception);
            Log.Error(LogTag, $"[App] TaskScheduler.UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

#if ANDROID
        // Lo que se escapa por el lado de Java. Las dos vías anteriores solo ven excepciones
        // gestionadas; un fallo en un handler de plataforma, en el inflado de una vista o en el
        // propio runtime sube por acá y no deja rastro en ninguna de ellas.
        //
        // Se ENCADENA el manejador anterior en vez de reemplazarlo: quitarlo cambiaría cómo
        // muere el proceso, y eso es de Android, no nuestro. Nosotros solo queremos el rastro.
        var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;

        Java.Lang.Thread.DefaultUncaughtExceptionHandler =
            new JavaCrashRecorder(Journal, previous);
#endif
    }

#if ANDROID
    private sealed class JavaCrashRecorder : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly Services.CrashJournal _journal;
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _next;

        public JavaCrashRecorder(
            Services.CrashJournal journal,
            Java.Lang.Thread.IUncaughtExceptionHandler? next)
        {
            _journal = journal;
            _next = next;
        }

        public void UncaughtException(Java.Lang.Thread thread, Java.Lang.Throwable throwable)
        {
            try
            {
                _journal.Record($"Java · hilo {thread.Name}", throwable, isTerminating: true);
            }
            catch (Exception)
            {
                // Pase lo que pase, el manejador de Android tiene que correr igual.
            }

            _next?.UncaughtException(thread, throwable);
        }
    }
#endif

    /// <summary>
    /// Cuánto espera la recuperación de pagos pendientes antes de arrancar.
    /// <para>
    /// No es una pausa cosmética. La recuperación abre la base cifrada (SQLCipher deriva la llave
    /// al abrir) y hace un handshake TLS contra el APIM por cada transacción huérfana. Sin espera,
    /// todo eso caía EXACTAMENTE en el segundo en que MAUI infla el Shell y el runtime jitea la
    /// ruta de arranque, peleando por los cuatro núcleos de la C9H con lo único que el cajero está
    /// mirando: que la pantalla aparezca.
    /// </para>
    /// <para>
    /// Retrasarla no cuesta nada. Lo que recupera son transacciones que quedaron a medias en una
    /// ejecución ANTERIOR — llevan ahí desde el cobro pasado y no dependen de este arranque. El
    /// margen se eligió corto a propósito: si el módulo se cierra antes de cumplirlo, la
    /// recuperación no corre y hay que esperar a la siguiente apertura.
    /// </para>
    /// </summary>
    private const int LifecycleBootstrapDelayMs = 4_000;

    private void ScheduleLifecycleBootstrap()
    {
        var services = AppServices.Current;

        if (services is null)
        {
            return;
        }

        var runner = services.GetService<Lifecycle.IAppLifecycleBootstrapper>();

        if (runner is null)
        {
            return;
        }

        // El retraso es toda la solución, y a propósito no hay nada más. Bajarle la prioridad al
        // hilo NO funcionaría: RunAsync es trabajo de E/S, y en cuanto llega al primer await sus
        // continuaciones vuelven al ThreadPool en prioridad normal sin importar en qué hilo
        // empezó. Lo único que de verdad saca esto del arranque es no empezarlo todavía.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(LifecycleBootstrapDelayMs);
                await runner.RunAsync();
            }
            catch (System.Exception exception)
            {
                var logger = services.GetService<ILoggerFactory>()
                    ?.CreateLogger("Permoda.Pay.Maui.Lifecycle");
                if (logger is not null)
                {
                    logger.LogError(exception, "Lifecycle bootstrap reported an unexpected error.");
                }

                Log.Error(LogTag, $"[App] Lifecycle bootstrap failed: {exception}");
            }
        });
    }
}
