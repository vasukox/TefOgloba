using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Maui.HiPos;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.Platforms.Android.HiPos;

// Tema translúcido y sin barra de título a propósito. Esta Activity no es una pantalla: recibe
// el intent de HiPOS, levanta la app y espera para responder. Con el tema por defecto se veía
// un rectángulo blanco con "Permoda Pay" arriba durante el segundo que tarda el arranque en
// frío de MAUI — el cajero veía una pantalla ajena antes de llegar al módulo. Translúcida, no
// se ve nada: se pasa de HiPOS directo a la pantalla del módulo.
[Activity(
    Name = "permoda.pay.maui.android.hipos.HiPosPaymentActivity",
    Label = "Ogloba",
    Theme = "@style/Ogloba.Invisible",
    Exported = true)]
public sealed class HiPosPaymentActivity : Activity
{
    // HioPos enruta con un intent IMPLÍCITO cuya acción tiene la forma
    // icg.actions.electronicpayment.{apk_name}.{OPERACIÓN}. La llave de enrutamiento es el
    // apk_name — no el package, ni la firma, ni el nombre del APK en disco. HiPOS nunca busca
    // "la app de Permoda": le pregunta a Android si hay algo que responda a ESA acción. Si no
    // coincide, Android contesta "nada" y para HiPOS eso es idéntico a "el módulo no está
    // instalado": ofrece instalarlo en cada arranque y la factura sale sin cobrar.
    //
    // apk_name con el que ICG dio de alta el módulo en CloudLicense. NO se adivinó: se leyó con
    // aapt2 del APK que la propia nube distribuye — que resultó ser el nuestro, publicado bajo
    // un nombre que el APK no declaraba. Eso cerraba un bucle en el que HiPOS lo daba por no
    // instalado y lo reinstalaba en cada arranque, para siempre.
    //
    // Este valor ya NO decide si atendemos: el despacho se hace por la OPERACIÓN (último
    // segmento) y el manifiesto declara varios candidatos. Cada intent registra con qué
    // apk_name llegó, para que la próxima vez no haya que adivinar.
    public const string PrimaryApkName = "permoglobal";

    // Paquetes desde los que aceptamos intents. En Release se rechaza cualquier otro caller
    // para evitar que un APK malicioso suplante al POS. En Debug también dejamos pasar adb
    // shell (uid 2000) para poder probar con `adb shell am start`.
    private const string TrustedCallerIcgStart = "icg.android.start";
    private const string TrustedCallerHioPos = "com.icg.hiopos";
    private const string DebugShellCaller = "com.android.shell";

    // Las operaciones del contrato. Son el ÚLTIMO segmento de la acción; lo que va antes es el
    // apk_name, que lo decide CloudLicense y no nosotros.
    public const string InitializationAction = "INITIALIZE";
    public const string FinalizationAction = "FINALIZE";
    public const string GetVersionAction = "GET_VERSION";
    public const string GetBehaviorAction = "GET_BEHAVIOR";
    public const string GetCustomParamsAction = "GET_CUSTOM_PARAMS";
    public const string GetPrintInfoAction = "GET_PRINT_INFO";
    public const string ShowSetupScreenAction = "SHOW_SETUP_SCREEN";
    public const string TransactionAction = "TRANSACTION";
    public const string ReadCardAction = "READ_CARD";
    public const string ChargeCardAction = "CHARGE_CARD";
    public const string GetCardDataAction = "GET_CARD_DATA";

    private const string ElectronicPaymentPrefix = "icg.actions.electronicpayment.";

    /// <summary>
    /// La operación que pide HiPOS, sin importar con qué apk_name la haya construido.
    /// </summary>
    private static string? OperationOf(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || !action.StartsWith(ElectronicPaymentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var lastDot = action.LastIndexOf('.');
        return lastDot >= 0 && lastDot < action.Length - 1 ? action[(lastDot + 1)..] : null;
    }

    /// <summary>
    /// El apk_name con el que HiPOS nos llamó. Solo para el log: es el dato que responde
    /// "¿con qué nombre nos está buscando el POS?", que es justo lo que no se puede saber
    /// cuando la acción no resuelve y no aparece nada en ningún lado.
    /// </summary>
    private static string? ApkNameOf(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || !action.StartsWith(ElectronicPaymentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var middle = action[ElectronicPaymentPrefix.Length..];
        var lastDot = middle.LastIndexOf('.');
        return lastDot > 0 ? middle[..lastDot] : null;
    }

    private readonly Dictionary<string, string?> _extras = new(StringComparer.OrdinalIgnoreCase);
    private HiPosPaymentActivityContext? _context;
    private Intent? _pendingIntent;
    private bool _resumed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // En cold-start, AppServices.Current es null en
        // OnCreate (MauiProgram corre cuando MAUI resuelve el primer IPlatformApplication
        // accesado desde MAUI Shell). Si procesamos el intent aquí, o se cae con
        // NullReferenceException o se dispara la creación del host MAUI que abre la
        // MainActivity por encima de nosotros y HiPOS nunca recibe respuesta.
        // El patrón correcto: guardar el Intent aquí y procesarlo en OnResume, cuando
        // MAUI ya garantizó que los servicios están montados.
        _pendingIntent = Intent;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // Re-launch de la misma Activity (LaunchMode SingleTask implícito por HiPOS):
        // guardamos el nuevo Intent para procesarlo en el próximo OnResume.
        _pendingIntent = intent;
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (_resumed)
        {
            return;
        }

        _resumed = true;

        if (_pendingIntent is null)
        {
            return;
        }

        var intent = _pendingIntent;
        _pendingIntent = null;

        var services = AppServices.Current;
        if (services is null)
        {
            FinishWithFailure("MAUI application services are unavailable.");
            return;
        }

        var orchestrator = services.GetService<HiPosPaymentOrchestrator>();
        if (orchestrator is null)
        {
            FinishWithFailure("HiPOS orchestrator was not registered.");
            return;
        }

        var session = services.GetService<PosSession>();
        if (session is null)
        {
            FinishWithFailure("PosSession was not registered.");
            return;
        }

        var sink = new HiPosResponseSink(this);
        _context = new HiPosPaymentActivityContext(orchestrator, sink, services, session);

        HandleIntent(intent, _context);
    }

    // Regla dura del contrato con ICG: ningún handler puede terminar sin devolver resultado. Si
    // se lanza una excepción y no respondemos, HioPos se queda esperando indefinidamente y el
    // cajero pierde la venta. Como este método es `async void`, un try/catch en OnCreate NO
    // captura lo que falle después del primer await — por eso el try/catch vive aquí dentro.
    private async void HandleIntent(Intent? intent, HiPosPaymentActivityContext context)
    {
        try
        {
            global::Android.Util.Log.Info(
                "TefOgloba.HiPos",
                "HandleIntent entered. action={0} caller={1}",
                intent?.Action ?? "<null>",
                CallingPackage ?? "<null>");

            if (intent is null)
            {
                FinishWithFailure("The HiPOS intent was null.");
                return;
            }

            // Defensa contra intents suplantados: el contrato de ICG dice que solo el POS nos
            // llama, y dejamos pasar `adb shell` únicamente en Debug para no romper el flujo de
            // pruebas manuales. Si llega cualquier otro caller en Release, respondemos
            // Canceled para que HioPos siga intentando con otro medio de pago.
#if !DEBUG
            var caller = CallingPackage;
            if (caller is not (TrustedCallerIcgStart or TrustedCallerHioPos))
            {
                context.Logger?.LogWarning(
                    "HiPOS intent from untrusted caller {Caller}; rejecting.", caller);
                context.Sink.SetResultCanceled();
                context.Sink.FinishActivity();
                return;
            }
#else
            var caller = CallingPackage;
            if (caller is not (TrustedCallerIcgStart or TrustedCallerHioPos or null or DebugShellCaller))
            {
                context.Logger?.LogWarning(
                    "HiPOS intent from unexpected caller {Caller} (debug build, allowed).", caller);
            }
#endif

            CopyExtras(intent);

            // Se despacha por la OPERACIÓN, no por la acción completa. HiPOS construye la
            // acción como `icg.actions.electronicpayment.{apk_name}.{OPERACIÓN}` y el apk_name
            // lo dicta CloudLicense — nosotros no lo controlamos y ya cambió tres veces
            // (`oglobapay` → `permoda` → lo que ICG deje). Comparar la acción entera contra un
            // prefijo fijo significaba que un cambio en la nube nos dejaba mudos, y sin
            // evidencia: si nadie resuelve la acción, Android no arranca nada y no hay ni una
            // línea en el log. "No nos llamaron" y "nos llamaron con otro nombre" se ven igual.
            //
            // El manifiesto declara varios candidatos y acá se registra CUÁL llegó de verdad.
            var operation = OperationOf(intent.Action);

            global::Android.Util.Log.Info(
                "TefOgloba.HiPos",
                "Acción resuelta: {0} → operación {1} (apk_name={2})",
                intent.Action ?? "<null>",
                operation ?? "<desconocida>",
                ApkNameOf(intent.Action) ?? "<desconocido>");

            switch (operation)
            {
                case InitializationAction:
                    var configurationXml = intent.GetStringExtra("Configuration");
                    await context.Orchestrator.HandleInitializationAsync(
                        configurationXml,
                        new PosSessionConfigurationSink(context.Session),
                        context.Sink,
                        CancellationToken.None);
                    return;
                case FinalizationAction:
                    context.Orchestrator.HandleFinalization(context.Sink);
                    return;
                case GetVersionAction:
                    context.Orchestrator.HandleVersion(context.Sink);
                    return;
                case GetBehaviorAction:
                    context.Orchestrator.HandleBehavior(context.Sink);
                    return;
                case GetCustomParamsAction:
                    context.Orchestrator.HandleCustomParams(context.Sink, await LoadLogoAsync(context));
                    return;
                case GetPrintInfoAction:
                    context.Orchestrator.HandlePrintInfo(context.Sink);
                    return;
                case ShowSetupScreenAction:
                    context.Orchestrator.HandleSetupScreen(context.Sink);
                    return;
                case ReadCardAction:
                case ChargeCardAction:
                case GetCardDataAction:
                    context.Orchestrator.HandleUnsupportedCardOperation(
                        intent.Action ?? operation ?? "<desconocida>", context.Sink);
                    return;
                case TransactionAction:
                    // HiPOS no envía TerminalId/CashierId en el intent
                    // TRANSACTION — solo llegan en INITIALIZE/SETUP. Resolvemos desde PosSession
                    // (Configuration.TerminalId + ActiveCashierId) y los pasamos como fallback al
                    // mapper. StoreId también sale de la configuración persistida.
                    await context.Session.EnsureLoadedAsync(CancellationToken.None);
                    var configurationStoreId = context.Session.Configuration?.StoreId;
                    var sessionTerminalId = context.Session.Configuration?.TerminalId;
                    // OperatingCashierId: un cobro de HiPOS no abre turno (el módulo no pide
                    // cajero), así que se firma con el último que operó en esta caja.
                    var sessionCashierId = context.Session.OperatingCashierId;

                    // Marcador de logcat que va al
                    // log nativo (no al ILogger de MAUI, que en Release se silencia) para
                    // confirmar en terminal qué TransactionType llegó y a qué flujo se ruteó.
                    var txType = _extras.TryGetValue("TransactionType", out var tx)
                        ? tx
                        : "<missing>";
                    var tender = _extras.TryGetValue("TenderType", out var tn) ? tn : "<missing>";
                    var amount = _extras.TryGetValue("Amount", out var am) ? am : "<missing>";
                    global::Android.Util.Log.Info(
                        "TefOgloba.HiPos",
                        "TRANSACTION incoming: type={0} tender={1} amount={2} isAdvanced={3}",
                        txType,
                        tender,
                        amount,
                        _extras.TryGetValue("IsAdvancedPayment", out var iap) ? iap : "<missing>");

                    // Volcado COMPLETO por logcat (no por ILogger, que no siempre sale con este
                    // tag). Venta y abono llegan las dos como SALE con IsAdvancedPayment=false,
                    // así que lo único que puede decirnos cómo distinguirlas es ver todos los
                    // extras de cada caso lado a lado en una terminal real.
                    //
                    // En PRODUCCIÓN va tapado. Este volcado sale sin filtrar a logcat, que
                    // cualquier app con permiso de lectura de logs puede leer, y hoy no llega el
                    // serial del bono solo porque HiPOS no lo manda — no porque algo lo impida.
                    // Depender de eso es depender de que el POS no cambie. Misma regla que la
                    // bitácora de tráfico: literal en sandbox (es la evidencia de certificación),
                    // tapado en producción, donde el literal solo es riesgo.
                    global::Android.Util.Log.Info(
                        "TefOgloba.HiPos",
                        "TRANSACTION extras completos: {0}",
                        DescribeExtras(context));

                    // Se registran los extras con VALOR, no solo las llaves: el enrutamiento
                    // venta/abono depende de IsAdvancedPayment, y este log es la única forma de
                    // confirmar en terminal qué manda HiPOS realmente para cada caso.
                    context.Logger?.LogInformation(
                        "HiPOS TRANSACTION intent received. Caller={Caller}. Extras: [{Extras}]. Session storeId={StoreId}, terminalId={TerminalId}, cashierId={CashierId}.",
                        caller,
                        DescribeExtras(context),
                        configurationStoreId,
                        sessionTerminalId,
                        sessionCashierId);

                    // Validar cashierId contra la lista de cajeros
                    // registrados antes de despachar (evita gastar una llamada a Ogloba que
                    // devolvería error 59/230040 con mensaje incomprensible para el operador).
                    var cashierIdForValidation = _extras.TryGetValue("CashierId", out var c) && !string.IsNullOrWhiteSpace(c)
                        ? c
                        : sessionCashierId;
                    var isCashierRegistered = await context.Session.IsCashierRegisteredAsync(
                        cashierIdForValidation,
                        CancellationToken.None);

                    global::Android.Util.Log.Info(
                        "TefOgloba.HiPos",
                        "Cashier check: validating='{0}' (fromExtras='{1}', session='{2}', result={3})",
                        cashierIdForValidation ?? "<null>",
                        _extras.TryGetValue("CashierId", out var ce) ? ce : "<missing>",
                        sessionCashierId ?? "<null>",
                        isCashierRegistered);

                    // Datos que solo conoce esta capa y que hacen falta si el cajero activa un
                    // bono virtual: el producto digital del ambiente y el nombre que el cliente
                    // ve como remitente del correo (la tienda real, o su ID si Ogloba aún no
                    // devolvió el nombre — no se inventa nada).
                    var senderName = string.IsNullOrWhiteSpace(context.Session.StoreName)
                        ? configurationStoreId ?? string.Empty
                        : context.Session.StoreName;

                    await context.Orchestrator.HandleTransactionAsync(
                        _extras,
                        configurationStoreId,
                        sessionTerminalId,
                        sessionCashierId,
                        isCashierRegistered,
                        new SaleCapture(this),
                        context.Session.Environment.DigitalProductCode,
                        senderName,
                        context.Sink,
                        CancellationToken.None);
                    return;
                default:
                    FinishWithFailure($"The HiPOS intent action '{intent.Action}' is not supported.");
                    return;
            }
        }
        catch (System.Exception exception)
        {
            // El contrato de ICG pide devolver Canceled ante cualquier fallo inesperado: lo
            // único inaceptable es no responder, porque HioPos se queda esperando para siempre
            // y el cajero pierde la venta.
            context.Logger?.LogError(exception, "HiPOS payment activity reported an unexpected failure.");
            context.Sink.SetResultCanceled();
            context.Sink.FinishActivity();
        }
    }

    /// <summary>
    /// Carga el PNG del logo empaquetado como MauiAsset. Si no está, devolvemos null y HioPos
    /// muestra su ícono genérico: no vale la pena tumbar GET_CUSTOM_PARAMS por el logo.
    /// </summary>
    private static async Task<byte[]?> LoadLogoAsync(HiPosPaymentActivityContext context)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(
                HiPosPaymentOrchestrator.LogoAssetFileName);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
        catch (System.Exception exception)
        {
            context.Logger?.LogWarning(
                exception,
                "No se pudo leer {Asset}; GET_CUSTOM_PARAMS responderá solo con el nombre.",
                HiPosPaymentOrchestrator.LogoAssetFileName);
            return null;
        }
    }

    /// <summary>
    /// Los extras del intent en una línea, tapados en producción.
    /// <para>
    /// Reutiliza <see cref="TrafficBodyRedactor"/> —el mismo que protege la bitácora de tráfico—
    /// en vez de inventar otra máscara: así un serial se ve igual en los dos sitios y nadie duda
    /// si es el mismo. Su regla de corridas largas de dígitos alcanza lo que importa acá
    /// (seriales, cédulas, identificadores de transacción).
    /// </para>
    /// <para>
    /// Si la sesión todavía no cargó, se tapa igual. Ante la duda sobre el ambiente, la opción
    /// segura es la que no publica nada.
    /// </para>
    /// </summary>
    private string DescribeExtras(HiPosPaymentActivityContext context)
    {
        var line = string.Join(" | ", _extras.Select(pair => $"{pair.Key}={pair.Value}"));

        var isSandbox = context.Session.Environment.IsSandbox;

        return isSandbox ? line : TrafficBodyRedactor.Redact(line);
    }

    private void CopyExtras(Intent intent)
    {
        var bundle = intent.Extras;
        if (bundle is null)
        {
            return;
        }

        var keys = bundle.KeySet();
        if (keys is null)
        {
            return;
        }

        foreach (var key in keys)
        {
            _extras[key] = ReadExtraAsString(bundle, key);
        }
    }

    // GetStringExtra devuelve null (silenciosamente) para extras que HiPOS manda como
    // Boolean/Integer/Parcelable (ej. IsAdvancedPayment, ReceiptPrinterColumns, TransactionId) —
    // se veía como ClassCastException en logcat y el valor se perdía. Java.Lang.Object.ToString()
    // cubre todos los tipos primitivos que Bundle admite sin necesitar leer cada tipo aparte.
    private static string ReadExtraAsString(Bundle bundle, string key)
    {
        var value = bundle.Get(key);
        return value?.ToString() ?? string.Empty;
    }

    private void FinishWithFailure(string message)
    {
        var sink = _context?.Sink;
        sink?.SetResultFailed(message);
        sink?.FinishActivity();
    }

    /// <summary>
    /// Adapta la Activity a <see cref="IHiPosCardCapture"/>: levanta la app en la pantalla que
    /// corresponde y espera a que el cajero termine la operación completa — incluida la llamada
    /// a Ogloba y la confirmación del resultado.
    /// </summary>
    private sealed class SaleCapture : IHiPosCardCapture
    {
        private readonly HiPosPaymentActivity _activity;

        public SaleCapture(HiPosPaymentActivity activity) => _activity = activity;

        public Task<HiPosOperationOutcome?> RunAsync(
            HiPosSaleIntent intent,
            long amountPesos,
            string currency,
            CancellationToken cancellationToken)
        {
            // El cajero opera en las pantallas de siempre —Activar bono o Redimir saldo—, no en
            // un formulario aparte. Se registra el cobro en HiPosFlow y se levanta la app; la
            // pantalla deposita ahí el resultado y esta Activity, que quedó esperando debajo en
            // la misma tarea, retoma para responderle a HiPOS.
            var pending = HiPosFlow.Begin(intent, amountPesos, currency);

            try
            {
                var launch = new Intent(_activity, typeof(MainActivity));

                // NewTask: el módulo vive en SU PROPIA tarea, NO apilado dentro de la de HiPOS.
                //
                // Se probó compartir tarea el 2026-09-02 para que el cobro se viera integrado al
                // POS. Se revirtió el mismo día por dos razones medidas en terminal:
                //
                //  1. El ícono del launcher dejaba de abrir el flujo manual: traía al frente la
                //     tarea de HiPOS con la pantalla del cobro encima. Activar y consultar son
                //     manuales y tienen que convivir con la caja abierta.
                //  2. Peor: nuestra pantalla y la del MÓDULO FISCAL quedaban en la misma pila y
                //     se pisaban. El fiscal arrancaba y moría en 238 ms sin llegar a llamar a su
                //     backend, y la factura fallaba con ResolutionRank / ResolutionNumber
                //     (10:31:45, log de terminal). Con tarea propia eso no puede pasar: nunca
                //     tocamos la pila del POS.
                //
                // El costo de la tarea propia es que el módulo aparece como app independiente en
                // recientes. Es cosmético; el otro camino rompe la facturación.
                //
                // ReorderToFront reutiliza la instancia que ya existe en vez de crear otra.
                launch.AddFlags(
                    ActivityFlags.NewTask
                    | ActivityFlags.SingleTop
                    | ActivityFlags.ReorderToFront);

                _activity.StartActivity(launch);
            }
            catch (System.Exception exception)
            {
                // Sin app no hay cobro. Se responde cancelado en vez de dejar a HiPOS esperando:
                // lo único inaceptable del contrato de ICG es no responder.
                _activity._context?.Logger?.LogError(
                    exception, "No se pudo levantar la app para el cobro de HiPOS.");

                HiPosFlow.Complete(null);
                return Task.FromResult<HiPosOperationOutcome?>(null);
            }

            return pending;
        }
    }

    protected override void OnDestroy()
    {
        // Si la Activity muere con un cobro en curso (el cajero salió, Android la mató),
        // desbloqueamos el flujo para que responda "cancelado" en vez de dejar a HiPOS
        // esperando indefinidamente.
        HiPosFlow.Complete(null);
        base.OnDestroy();
    }

    private sealed class HiPosResponseSink : IHiPosResponseSink
    {
        private readonly HiPosPaymentActivity _activity;
        private readonly Intent _resultIntent;

        public HiPosResponseSink(HiPosPaymentActivity activity)
        {
            _activity = activity;
            _resultIntent = new Intent();
        }

        // TODA respuesta a HiPOS pasa por acá, y por eso acá se registra — con Android.Util.Log
        // y no con ILogger, que no sale en logcat. Sin esto se podía ver lo que HiPOS nos manda
        // pero no lo que le contestamos, que es justo la mitad que hace falta cuando el POS
        // relanza el mismo TRANSACTION una y otra vez.
        public void SetResultOk(string transactionResult, IDictionary<string, string?> extras)
        {
            _resultIntent.PutExtra("Result", transactionResult);
            foreach (var (key, value) in extras)
            {
                if (value is null)
                {
                    continue;
                }

                _resultIntent.PutExtra(key, value);
            }

            _activity.SetResult(Result.Ok, _resultIntent);

            global::Android.Util.Log.Info(
                "TefOgloba.HiPos",
                "RESPUESTA a HiPOS → RESULT_OK / {0} · extras: {1} · flags: {2}",
                transactionResult,
                Describe(extras),
                DescribeBoolExtras());

            BroadcastAudit(transactionResult, extras);

            // Finish() colapsa la pila: la MainActivity (encima) + esta Activity se destruyen,
            // HiPOS recibe el foco con el resultado. Antes HiPOS recuperaba el foco por su
            // cuenta; sin esto, con la MainActivity todavía viva encima, el cajero quedaba
            // mirando MAUI en vez del resultado de la venta.
            _activity.Finish();
        }

        public void SetResultFailed(string errorMessage)
        {
            _resultIntent.PutExtra("ErrorMessage", errorMessage);
            _resultIntent.PutExtra("Result", "FAILED");
            _activity.SetResult(Result.Ok, _resultIntent);

            global::Android.Util.Log.Warn(
                "TefOgloba.HiPos", $"RESPUESTA a HiPOS → FAILED · {errorMessage}");

            BroadcastAudit("FAILED", new Dictionary<string, string?> { ["ErrorMessage"] = errorMessage });

            _activity.Finish();
        }

        // Los extras van ANTES del SetResult: el intent se entrega por referencia, pero llenarlo
        // después de entregarlo es la clase de detalle que funciona hasta que deja de hacerlo.
        public void SetResultCanceled(IDictionary<string, string?> extras)
        {
            foreach (var (key, value) in extras)
            {
                if (value is null)
                {
                    continue;
                }

                _resultIntent.PutExtra(key, value);
            }

            _activity.SetResult(Result.Canceled, _resultIntent);

            global::Android.Util.Log.Warn(
                "TefOgloba.HiPos",
                "RESPUESTA a HiPOS → RESULT_CANCELED · extras: {0}",
                Describe(extras));

            BroadcastAudit("CANCELED", extras);

            _activity.Finish();
        }

        /// <summary>Extras en una línea, recortando los recibos XML que son enormes.</summary>
        private static string Describe(IDictionary<string, string?> extras) =>
            string.Join(" | ", extras
                .Where(pair => pair.Value is not null)
                .Select(pair => pair.Key.Contains("Receipt", StringComparison.OrdinalIgnoreCase)
                    ? $"{pair.Key}=<{pair.Value!.Length} chars>"
                    : $"{pair.Key}={pair.Value}"));

        // El extra entero va con la sobrecarga int de PutExtra: si se mandara como String,
        // getIntExtra en HioPos devuelve el default (-1) sin avisar. Es exactamente lo que hacía
        // que HioPos pidiera reinstalar el módulo en cada arranque.
        public void PutIntExtra(string key, int value) => _resultIntent.PutExtra(key, value);

        // Las flags boolean de GET_BEHAVIOR (SupportsCredit, HasCustomParams, etc.)
        // tienen que viajar como booleanos nativos del Intent. HioPos las lee con
        // getBooleanExtra; si las recibimos como String "true"/"false", Bundle tira
        // ClassCastException y HioPos descarta el módulo silenciosamente.
        //
        // Se anotan aparte para poder IMPRIMIRLAS: no viven en el diccionario de extras que
        // recibe SetResultOk, así que el log de la respuesta salía como "extras:" a secas. Sin
        // esto no hay forma de demostrar qué banderas mandamos, y una prueba en terminal que
        // falla no distingue entre "no la mandamos" y "HiPOS no la usó" — se perdieron dos
        // ciclos completos de prueba por esa ambigüedad.
        public void PutBoolExtra(string key, bool value)
        {
            _resultIntent.PutExtra(key, value);
            _boolExtras[key] = value;
        }

        private readonly Dictionary<string, bool> _boolExtras = new(StringComparer.Ordinal);

        /// <summary>Banderas booleanas enviadas, en una línea.</summary>
        private string DescribeBoolExtras() =>
            _boolExtras.Count == 0
                ? string.Empty
                : string.Join(" | ", _boolExtras.Select(pair => $"{pair.Key}={pair.Value}"));

        // El PNG del logo va como byte[] crudo: es el único extra binario del contrato.
        public void PutByteArrayExtra(string key, byte[] value) => _resultIntent.PutExtra(key, value);

        // docs/SEGURIDAD.md §7 ("Broadcast
        // icg.actions.externalApi.AUDIT sale en cada operación"). El doc NO especifica el
        // esquema exacto de extras — es un ítem de checklist HiOSTORE, no una sección con
        // contrato detallado como los intents de pago. Mejor esfuerzo: reenviamos el mismo
        // resultado/extras que ya le devolvemos a HiPOS más un timestamp. Confirmar el esquema
        // real con ICG antes de validar en hardware KOAJ — un listener inexistente no rompe nada
        // (SendBroadcast es fire-and-forget), pero el payload podría no calzar con lo esperado.
        private void BroadcastAudit(string result, IDictionary<string, string?> extras)
        {
            try
            {
                var auditIntent = new Intent("icg.actions.externalApi.AUDIT");
                auditIntent.SetPackage(TrustedCallerIcgStart);
                auditIntent.PutExtra("Result", result);
                auditIntent.PutExtra("TimestampUtc", DateTimeOffset.UtcNow.ToString("O"));

                foreach (var (key, value) in extras)
                {
                    if (value is not null)
                    {
                        auditIntent.PutExtra(key, value);
                    }
                }

                _activity.SendBroadcast(auditIntent);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[HiPosResponseSink] BroadcastAudit failed: {exception}");
            }
        }

        public void FinishActivity() => _activity.Finish();
    }

    /// <summary>
    /// Adapta <see cref="PosSession"/> a <see cref="IHiPosStoreConfigurationSink"/> para que
    /// HiPosPaymentOrchestrator (proyecto sin referencia a Permoda.Pay.Maui) pueda persistir la
    /// configuración recibida en el intent INITIALIZE.
    /// </summary>
    private sealed class PosSessionConfigurationSink : IHiPosStoreConfigurationSink
    {
        private readonly PosSession _session;

        public PosSessionConfigurationSink(PosSession session) => _session = session;

        public async Task<bool> ApplyAsync(
            string storeId,
            string baseUrl,
            string apiVersion,
            string? oglobaPassword,
            CancellationToken cancellationToken)
        {
            await _session.EnsureLoadedAsync(cancellationToken);

            var configuration = new Permoda.Pay.Maui.Configuration.StoreConfiguration(
                storeId,
                _session.Configuration?.TerminalId ?? string.Empty,
                baseUrl,
                apiVersion,
                _session.Configuration?.StoreName ?? string.Empty);

            var result = await _session.SaveConfigurationAsync(configuration, oglobaPassword, cancellationToken);
            return result.IsSuccess;
        }
    }

    private sealed record HiPosPaymentActivityContext(
        HiPosPaymentOrchestrator Orchestrator,
        IHiPosResponseSink Sink,
        IServiceProvider Services,
        PosSession Session)
    {
        public ILogger? Logger => Services.GetService<ILoggerFactory>()
            ?.CreateLogger("Permoda.Pay.Maui.HiPosPaymentActivity");
    }
}
