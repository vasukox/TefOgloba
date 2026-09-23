using Android.App;
using Android.Content;
using Android.OS;
using Permoda.Pay.Maui.HiPos;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.Platforms.Android.HiPos;

/// <summary>
/// Recibe el aviso de HiPOS de que el cajero canceló la totalización, y le devuelve al bono el
/// saldo que se le había descontado.
/// <para>
/// Es lo que desbloquea la papelera sobre la línea de pago del bono. Antes esa línea quedaba
/// congelada: si la DIAN no integraba la factura, la caja se trababa con la plata ya descontada
/// y sin forma de soltar el cobro. HiPOS solo manda este intent si el módulo declara
/// <c>CallOnTotalizationCanceled=true</c> en GET_BEHAVIOR.
/// </para>
/// <para>
/// El namespace de la acción es <c>icg.actions.document.</c>, NO
/// <c>icg.actions.electronicpayment.</c> como el resto del contrato TEF. Por eso esta Activity
/// va aparte de <see cref="HiPosPaymentActivity"/>, que despacha por el último segmento de las
/// acciones de electronicpayment y no reconocería ésta.
/// </para>
/// </summary>
[Activity(
    Name = "permoda.pay.maui.android.hipos.TotalizationCanceledActivity",
    Theme = "@style/Ogloba.Invisible",
    Exported = true)]
public sealed class TotalizationCanceledActivity : Activity
{
    private const string LogTag = "TefOgloba.HiPos";

    /// <summary>Acción completa, con el apk_name que ICG dio de alta en CloudLicense.</summary>
    public const string Action =
        "icg.actions.document." + HiPosPaymentActivity.PrimaryApkName + ".TOTALIZATION_CANCELED";

    /// <summary>Ruta del XML del documento. Es la forma en que ICG lo manda por defecto.</summary>
    private const string DocumentPathExtra = "DocumentPath";

    /// <summary>
    /// Alternativa de Android 10+. Con almacenamiento por ámbito, la ruta plana del extra
    /// anterior puede no ser legible para nosotros aunque exista — ya nos pasó con el documento
    /// de venta, y es la razón de que GET_BEHAVIOR declare <c>OnlyUseDocumentPath=false</c>.
    /// </summary>
    private const string DocumentUriExtra = "DocumentUri";

    /// <summary>Documento embebido en el propio intent, cuando HiPOS lo manda así.</summary>
    private static readonly string[] InlineDocumentExtras = ["DocumentData", "Document"];

    private Intent? _pendingIntent;
    private bool _resumed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Mismo motivo que en HiPosPaymentActivity: en arranque en frío AppServices.Current
        // todavía es null acá. El intent se guarda y se procesa en OnResume.
        _pendingIntent = Intent;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
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

        var intent = _pendingIntent;
        _pendingIntent = null;

        if (intent is null)
        {
            Respond(ok: true, error: null);
            return;
        }

        Handle(intent);
    }

    /// <summary>
    /// Regla dura del contrato: este método no puede terminar sin responder. Es <c>async
    /// void</c>, así que el try/catch tiene que vivir adentro — uno en OnResume no atraparía
    /// nada de lo que falle después del primer await, y HiPOS se quedaría esperando.
    /// </summary>
    private async void Handle(Intent intent)
    {
        try
        {
            global::Android.Util.Log.Info(
                LogTag,
                "TOTALIZATION_CANCELED recibido. action={0} caller={1}",
                intent.Action ?? "<null>",
                CallingPackage ?? "<null>");

            var services = AppServices.Current;
            var orchestrator = services?.GetService<HiPosPaymentOrchestrator>();
            var session = services?.GetService<PosSession>();

            if (orchestrator is null || session is null)
            {
                // Sin servicios no podemos anular. Se responde ERROR y no OK: decir que todo
                // salió bien dejaría a HiPOS soltando una venta con el bono aún descontado.
                Respond(ok: false, error: "El módulo Ogloba no pudo iniciarse para devolver el saldo.");
                return;
            }

            await session.EnsureLoadedAsync(CancellationToken.None);

            var document = ReadDocument(intent);

            var sink = new TotalizationSink(this);

            await orchestrator.HandleTotalizationCanceledAsync(
                document,
                session.Configuration?.StoreId,
                session.Configuration?.TerminalId,
                // Un cobro desde HiPOS no abre turno en el módulo, así que se usa el cajero que
                // esté operando (el del último turno si no hay uno activo) — el mismo criterio
                // con el que se hizo la redención que ahora se deshace.
                session.OperatingCashierId,
                sink,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                LogTag, $"TOTALIZATION_CANCELED falló: {exception}");

            Respond(ok: false, error: exception.Message);
        }
    }

    /// <summary>
    /// Trae el XML del documento por los tres caminos por los que HiPOS puede mandarlo, en orden
    /// de preferencia. Devolver <c>null</c> es un resultado válido: significa que no hay
    /// documento y por lo tanto no hay pago con bono que deshacer.
    /// </summary>
    private string? ReadDocument(Intent intent)
    {
        foreach (var key in InlineDocumentExtras)
        {
            var inline = intent.GetStringExtra(key);

            if (!string.IsNullOrWhiteSpace(inline))
            {
                return inline;
            }
        }

        var path = intent.GetStringExtra(DocumentPathExtra);

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }

                global::Android.Util.Log.Warn(
                    LogTag, $"DocumentPath no existe o no es legible: {path}");
            }
            catch (Exception exception)
            {
                // Almacenamiento por ámbito: la ruta llega pero no la podemos abrir. Se intenta
                // con el Uri, que sí atraviesa el ContentResolver.
                global::Android.Util.Log.Warn(
                    LogTag, $"No se pudo leer DocumentPath: {exception.Message}");
            }
        }

        return ReadFromUri(intent);
    }

    private string? ReadFromUri(Intent intent)
    {
        try
        {
            var uri = intent.GetParcelableExtra(DocumentUriExtra) as global::Android.Net.Uri
                ?? (intent.GetStringExtra(DocumentUriExtra) is { Length: > 0 } text
                    ? global::Android.Net.Uri.Parse(text)
                    : null);

            if (uri is null)
            {
                return null;
            }

            using var stream = ContentResolver?.OpenInputStream(uri);

            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(
                LogTag, $"No se pudo leer DocumentUri: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Responde y cierra. El Intent de respuesta lleva la MISMA acción que el de entrada: así lo
    /// hace el ejemplo de ICG y es como el POS reconoce de qué operación es la respuesta.
    /// </summary>
    private void Respond(bool ok, string? error)
    {
        var result = new Intent(Action);

        if (!ok && !string.IsNullOrWhiteSpace(error))
        {
            result.PutExtra("ErrorMessage", error);
        }

        SetResult(ok ? Result.Ok : Result.Canceled, result);

        global::Android.Util.Log.Info(
            LogTag,
            "RESPUESTA a HiPOS (TOTALIZATION_CANCELED) → {0}{1}",
            ok ? "RESULT_OK" : "RESULT_CANCELED",
            error is null ? string.Empty : $" · {error}");

        Finish();
    }

    /// <summary>
    /// Traduce lo que responde el orquestador al formato de este intent, que no es el de una
    /// transacción: acá HiPOS solo espera RESULT_OK o RESULT_CANCELED, sin extras de venta.
    /// </summary>
    private sealed class TotalizationSink : IHiPosResponseSink
    {
        private readonly TotalizationCanceledActivity _activity;

        public TotalizationSink(TotalizationCanceledActivity activity) => _activity = activity;

        public void SetResultOk(string transactionResult, IDictionary<string, string?> extras) =>
            _activity.Respond(ok: true, error: null);

        public void SetResultFailed(string errorMessage) =>
            _activity.Respond(ok: false, error: errorMessage);

        // Los extras se ignoran a propósito: este intent no lleva transacción, así que HiPOS no
        // tiene dónde mostrar un motivo. La firma existe por el otro sink, el de los cobros.
        public void SetResultCanceled(IDictionary<string, string?> extras) =>
            _activity.Respond(ok: true, error: null);

        // El resto del contrato no aplica a este intent: no hay recibos, ni banderas de
        // comportamiento, ni logo que devolver.
        public void PutIntExtra(string key, int value) { }

        public void PutBoolExtra(string key, bool value) { }

        public void PutByteArrayExtra(string key, byte[] value) { }

        // El cierre lo hace Respond. Dejarlo vacío evita un Finish() doble.
        public void FinishActivity() { }
    }
}
