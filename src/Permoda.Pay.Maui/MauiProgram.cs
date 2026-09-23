using Android.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Features.Lifecycle;
using Permoda.Pay.Application.Features.Recovery;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Infrastructure.Ogloba;
using Permoda.Pay.Infrastructure.Ogloba.Authentication;
using Permoda.Pay.Infrastructure.Persistence;
using Permoda.Pay.Infrastructure.Security;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Configuration;
using Permoda.Pay.Maui.HiPos;
using Permoda.Pay.Maui.Lifecycle;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;
using Permoda.Pay.Maui.ViewModels.Pos;
using Permoda.Pay.Maui.Views.Admin;
using Permoda.Pay.Maui.Views.Pos;
using Permoda.Pay.Maui.Platforms.Android.Security;

namespace Permoda.Pay.Maui;

public static class MauiProgram
{
    private const string LogTag = "TefOgloba";

    public static MauiApp CreateMauiApp()
    {
        // DIAG TEMPORAL: Android.Util.Log (no Console.WriteLine, que no llega a
        // logcat sin depurador adjunto). Marcadores para acotar hasta dónde llega el arranque
        // si algo falla en silencio antes de que exista cualquier ventana/página.
        Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: start");

        try
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Feedback de presión animado para todo Button de la app (antes era un salto
            // instantáneo de Scale vía VisualStateManager) — ver PremiumButtonPressAnimation.
            PremiumButtonPressAnimation.Register();

            DisableEmojiProcessingOnEntries();

            Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: builder created, registering services");
            RegisterServices(builder);
            Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: services registered");

            // El Shell raíz solo declara las 5 pestañas operativas (Inicio/Activar/Redimir/
            // Bitácora/Config). El ingreso de cajero NO es una pestaña más (sería el bug
            // Bug #1 que dejaba el Shell sin contenido): es una ruta global que se empuja sobre
            // la pestaña activa y se descarta al entrar.
            //
            // UNA sola ruta de ingreso. Antes había dos —"login" y "cashierpicker"— y la primera
            // daba el turno solo con elegir un nombre, sin contraseña. Ver AppRoutes.CashierLogin.
            Routing.RegisterRoute("cashierlogin", typeof(Views.Pos.CashierLoginPage));

            // Paso previo a activar: físico o virtual. Ver AppRoutes.ActivateMode.
            Routing.RegisterRoute("activatemode", typeof(Views.Pos.ActivateModePage));

            // Replicación de configuración entre cajas de la misma tienda. Rutas GLOBALES: el
            // servicio de red del lado emisor vive lo que dura la página, así que tiene que poder
            // apilarse y descartarse. Ver AppRoutes.ReplicationHost.
            Routing.RegisterRoute("replicationhost", typeof(Views.Setup.ReplicationHostPage));
            Routing.RegisterRoute("replicationjoin", typeof(Views.Setup.ReplicationJoinPage));

            // Construimos el host UNA sola vez y capturamos el IServiceProvider del MAUI host
            // (no uno paralelo via BuildServiceProvider, que duplicaba singletons y HttpClient).
            // Bug #3: el provider paralelo se quedaba sin los handlers de plataforma y rompía
            // HiPosPaymentActivity. MauiApp implementa IHost, así que app.Services es el
            // mismo IServiceProvider que consume el resto de MAUI (no requiere cast a
            // IPlatformApplication, que solo se rellena cuando el host ya está montado).
            Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: building host");
            var app = builder.Build();
            Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: host built, capturing services");
            AppServices.Capture(app.Services);

            // Conecta OglobaGiftCardProvider.Log a Android.Util.Log para que las requests
            // a Ogloba aparezcan en logcat (release y debug). En tests, ConfigureLogging
            // no se llama y el provider cae a System.Diagnostics.Debug.
            OglobaGiftCardProvider.ConfigureLogging(line =>
                Android.Util.Log.Info("TefOgloba.Ogloba", line));

            Log.Debug(LogTag, "[MauiProgram] CreateMauiApp: done");
            return app;
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[MauiProgram] CreateMauiApp FAILED: {exception}");
            throw;
        }
    }

    /// <summary>
    /// Apaga el procesamiento de emoji en todos los <see cref="Entry"/> de la app.
    /// <para>
    /// Android encadena un <c>TextWatcher</c> de emoji2 a cada campo de texto. Cuando el
    /// separador de miles reescribía el monto mientras el cajero tecleaba, ese watcher procesaba
    /// el texto con la longitud anterior y tumbaba la app en la terminal C9H:
    /// <c>IllegalArgumentException: end should be &lt; than charSequence length</c>. Se reprodujo
    /// dos veces en dispositivo real, y aplazar la escritura al dispatcher no bastó porque el
    /// watcher seguía en la cadena.
    /// </para>
    /// <para>
    /// Ningún campo de esta app acepta emoji —seriales, montos, correos, PIN—, así que quitar
    /// ese watcher no cuesta nada y elimina la causa en vez del síntoma.
    /// </para>
    /// </summary>
    private static void DisableEmojiProcessingOnEntries() =>
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
            "PermodaDisableEmojiCompat",
            (handler, _) =>
            {
                try
                {
                    if (handler.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
                    {
                        // Vía JNI: `setEmojiCompatEnabled` existe en AppCompat 1.4+ pero no está
                        // proyectado en el binding de .NET para Android, así que se invoca por
                        // reflexión de Java en lugar de agregar una dependencia solo por esto.
                        if (Java.Lang.Boolean.Type is { } booleanType)
                        {
                            editText.Class
                                .GetMethod("setEmojiCompatEnabled", booleanType)
                                ?.Invoke(editText, Java.Lang.Boolean.ValueOf(false));
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Warn(LogTag, $"[MauiProgram] No se pudo desactivar emoji2 en un Entry: {exception.Message}");
                }
            });

    private static void RegisterServices(MauiAppBuilder builder)
    {
        var environment = AppEnvironment.FromBuildConfiguration(builder.Configuration["environment"]);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(environment);

        builder.Services.AddSingleton<SecureStorageStoreConfigurationProvider>();
        builder.Services.AddSingleton<SecureStorageOglobaCredentialProvider>();
        builder.Services.AddSingleton<PosSession>();
        builder.Services.AddSingleton<InMemoryTransactionAudit>();
        builder.Services.AddSingleton<ITransactionAudit>(sp => sp.GetRequiredService<InMemoryTransactionAudit>());
        builder.Services.AddSingleton<InMemoryOglobaTrafficLog>();
        builder.Services.AddSingleton<IOglobaTrafficLog>(sp => sp.GetRequiredService<InMemoryOglobaTrafficLog>());
        builder.Services.AddSingleton<UatLogExporter>();

        // LA MISMA instancia que usan los manejadores globales, no una nueva. Los manejadores se
        // instalan antes de que exista el contenedor —un fallo durante el arranque es el que más
        // falta hace registrar—, así que el dueño del archivo es App y DI solo lo reparte. Dos
        // instancias escribirían el mismo archivo desde candados distintos.
        builder.Services.AddSingleton(App.CrashJournal);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ITransactionNumberGenerator, MonotonicTransactionNumberGenerator>();

        builder.Services.AddSingleton<SecureStorageDatabaseEncryptionKeyProvider>();
        builder.Services.AddSingleton<IDatabaseEncryptionKeyProvider>(sp =>
            sp.GetRequiredService<SecureStorageDatabaseEncryptionKeyProvider>());

        builder.Services.AddSingleton<IOglobaCredentialProvider>(sp =>
            sp.GetRequiredService<SecureStorageOglobaCredentialProvider>());

        builder.Services.AddHttpClient<OglobaGiftCardProvider>((sp, client) =>
        {
            var activeEnvironment = sp.GetRequiredService<AppEnvironment>();
            var options = new OglobaOptions(
                new Uri(activeEnvironment.OglobaBaseUrl),
                activeEnvironment.OglobaApiVersion,
                requestTimeout: null,
                usesApiManagement: activeEnvironment.UsesApiManagement);
            client.Timeout = options.RequestTimeout;
        });

        builder.Services.AddSingleton<IGiftCardProvider>(sp => sp.GetRequiredService<OglobaGiftCardProvider>());

        builder.Services.AddSingleton(sp =>
        {
            var activeEnvironment = sp.GetRequiredService<AppEnvironment>();
            return new OglobaOptions(
                new Uri(activeEnvironment.OglobaBaseUrl),
                activeEnvironment.OglobaApiVersion,
                requestTimeout: null,
                usesApiManagement: activeEnvironment.UsesApiManagement);
        });

        builder.Services.AddSingleton<IPendingPaymentRepository>(sp =>
        {
            var databasePath = Path.Combine(
                FileSystem.AppDataDirectory ?? Path.GetTempPath(),
                "pending-payments.db");
            return new SqlCipherPendingPaymentRepository(
                databasePath,
                sp.GetRequiredService<IDatabaseEncryptionKeyProvider>());
        });

        builder.Services.AddSingleton<ProcessSaleHandler>();
        builder.Services.AddSingleton<ActivateVirtualGiftCardHandler>();
        builder.Services.AddSingleton<RecoverPendingPaymentsHandler>();
        builder.Services.AddSingleton<RecoveryStartupRunner>();
        builder.Services.AddSingleton<IAppLifecycleBootstrapper, AppLifecycleBootstrapper>();
        builder.Services.AddSingleton<HiPosPaymentOrchestrator>();

        // Transient: cada ViewModel tiene su propio banner para que los mensajes no se
        // filtren entre pestañas (p. ej. un error de Activar apareciendo en Redimir).
        builder.Services.AddTransient<StatusBanner>();
        builder.Services.AddSingleton<CashierActivityLog>();
        builder.Services.AddSingleton<ActivationReceiptStore>();
        builder.Services.AddSingleton<ViewModels.PosHeaderViewModel>();

        // Toast nativo Android — reemplaza al StatusBannerView grande.
        builder.Services.AddSingleton<Services.IToastService, Services.AndroidToastService>();

        // AppShell y las páginas son TRANSIENT. La regla: lo que tiene handler de plataforma se
        // recrea con la Activity; lo que guarda estado (los ViewModels) sobrevive.
        //
        // Eran singleton, y desde que el módulo comparte tarea con HiPOS eso pasó a ser un
        // crash seguro: cada cobro termina con Finish() de la MainActivity, y al abrir el
        // siguiente MAUI le pide a DI el Shell y recibe el MISMO, con su handler ya desconectado.
        // Se veía como dos fallos distintos —
        //
        //   ObjectDisposedException: 'ShellToolbarTracker'  (ShellFlyoutRenderer.Disconnect)
        //   ArgumentException: A resource with the key 'Microsoft.Maui.Controls.Entry'
        //                      is already present in the ResourceDictionary
        //
        // — y como una app que "a veces no levanta": si el crash ocurre en OnCreate, el proceso
        // muere antes de dibujar y el cajero solo ve que no abre. Medido en terminal el
        // 2026-08-28 (logcat: tres crashes seguidos en 60 segundos).
        //
        // Los ViewModels siguen siendo singleton a propósito: ahí vive el estado del turno y del
        // lote, y ESO sí tiene que sobrevivir a que la pantalla se reconstruya.
        builder.Services.AddTransient<AppShell>();

        builder.Services.AddSingleton<HomeTabViewModel>();
        builder.Services.AddSingleton<ActivateTabViewModel>();
        builder.Services.AddSingleton<RedeemTabViewModel>();
        builder.Services.AddSingleton<BalanceTabViewModel>();
        builder.Services.AddSingleton<AuditTabViewModel>();

        builder.Services.AddTransient<HomeTabPage>();
        builder.Services.AddTransient<ActivateTabPage>();
        builder.Services.AddTransient<RedeemTabPage>();
        builder.Services.AddTransient<BalanceTabPage>();
        builder.Services.AddTransient<AuditTabPage>();

        // Ingreso del cajero. TRANSIENT también el ViewModel, no solo la página: guarda usuario y
        // contraseña, y un singleton los conservaría entre turnos. Cada ingreso empieza en blanco.
        builder.Services.AddTransient<CashierLoginViewModel>();
        builder.Services.AddTransient<CashierLoginPage>();

        // Selector de tipo de bono. No guarda estado propio: escribe el modo en
        // ActivateTabViewModel (singleton) y navega.
        // Gestion de usuarios. TRANSIENT: el ViewModel guarda el PIN escrito y el estado de
        // desbloqueo, y un singleton los conservaria entre visitas — la puerta quedaria abierta.
        builder.Services.AddTransient<ViewModels.Admin.UserManagementViewModel>();
        builder.Services.AddTransient<Views.Admin.UserManagementPage>();
        builder.Services.AddTransient<ActivateModeViewModel>();
        builder.Services.AddTransient<ActivateModePage>();

        builder.Services.AddSingleton<AdminLogExportViewModel>();
        builder.Services.AddTransient<AdminLogExportPage>();

        // Misma regla: ViewModel singleton (guarda lo que el usuario lleva escrito), página
        // transient (se recrea con la Activity).
        builder.Services.AddSingleton<ViewModels.Setup.SetupViewModel>();
        builder.Services.AddTransient<Views.Setup.SetupPage>();

        builder.Services.AddSingleton<ViewModels.Setup.CashierSetupViewModel>();
        builder.Services.AddTransient<Views.Setup.CashierSetupPage>();

        // Replicación entre cajas. TRANSIENT los dos ViewModels, y no por costumbre:
        //
        // · El emisor ES dueño de un socket abierto y de un código vivo. Como singleton, ese
        //   socket sobreviviría a salir de la pantalla y quedaría una caja ofreciendo la llave de
        //   producción de la tienda sin que nadie lo esté mirando.
        // · El receptor guarda el código tecleado y la configuración traída de otra caja. Un
        //   singleton los conservaría entre visitas.
        builder.Services.AddTransient<ViewModels.Setup.ReplicationHostViewModel>();
        builder.Services.AddTransient<Views.Setup.ReplicationHostPage>();
        builder.Services.AddTransient<ViewModels.Setup.ReplicationJoinViewModel>();
        builder.Services.AddTransient<Views.Setup.ReplicationJoinPage>();
    }
}
