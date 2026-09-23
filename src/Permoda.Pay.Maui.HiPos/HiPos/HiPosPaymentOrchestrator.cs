using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Maui.HiPos.Requests;
using Permoda.Pay.Maui.HiPos.Results;

namespace Permoda.Pay.Maui.HiPos;

public sealed class HiPosPaymentOrchestrator
{
    private const string AppVersion = "1.0.0";
    private const string AppBuild = "1";
    private const string LibraryName = "Permoda.Pay.Maui";

    /// <summary>
    /// Versión ENTERA que HioPos compara contra la registrada en HioPosCloud. Si no coinciden
    /// exactamente, HioPos pide reinstalar el módulo en cada arranque. Debe moverse a la par de
    /// <c>ApplicationVersion</c> en el csproj y del alta en CloudLicense.
    /// </summary>
    public const int ModuleVersionCode = 1;

    public const string VersionExtra = "Version";
    public const string NameExtra = "Name";
    public const string LogoExtra = "Logo";

    // Las flags de GET_BEHAVIOR viajan como texto, no como booleanos.
    private const string True = "true";
    private const string False = "false";

    /// <summary>Nombre del botón que ve el cajero en la pantalla de medios de pago.</summary>
    public const string ModuleDisplayName = "Ogloba";

    /// <summary>
    /// PNG del logo del botón, empaquetado como MauiAsset en <c>Resources/Raw/</c>. Lo carga
    /// la capa Android y se entrega a <see cref="HandleCustomParams"/> como bytes.
    /// </summary>
    public const string LogoAssetFileName = "tef_logo.png";

    /// <summary>Línea del medio de pago cuando HiPOS no manda <c>PaymentMeanLineNumber</c>.</summary>
    private const string DefaultPaymentMeanLineNumber = "1";

    /// <summary>Tipo de medio de pago del contrato de ICG. "0" = el estándar del documento.</summary>
    private const string PaymentMeanType = "0";

    private readonly HiPosRequestMapper _mapper = new();
    private readonly HiPosResponseComposer _composer = new();
    private readonly ModifyDocumentResultBuilder _modifyDocument = new();
    private readonly ProcessSaleHandler _sales;
    private readonly IGiftCardProvider _provider;
    private readonly ILogger<HiPosPaymentOrchestrator> _logger;

    /// <summary>
    /// Identificador del medio de pago OGLOBA en el CloudLicense de KOAJ.
    /// <para>
    /// No está adivinado: se leyó del documento que HiPOS mandó a la terminal el 2026-09-01, en
    /// la línea que el propio POS rotula "OGLOBA" (ver
    /// <see cref="Requests.TotalizationDocumentReader"/>). Es el alta real de Ogloba, no el medio
    /// de otro módulo — que es lo que prohíbe D-05.
    /// </para>
    /// <para>
    /// Se usa SOLO cuando HiPOS no manda el suyo, que es lo habitual en el intent de venta. Si
    /// una tienda tuviera un id distinto, la venta se imprimiría con otro medio y el log lo dice
    /// en claro: por eso la línea de INFO al aplicarlo. Lo correcto a futuro es que viaje en el
    /// XML de <c>Parameters</c> del INITIALIZE, y eso hay que pedírselo a ICG.
    /// </para>
    /// </summary>
    public const string OglobaPaymentMeanId = "1000037";

    // La activación virtual ya no pasa por aquí: la hacen las pantallas del módulo, que son las
    // que muestran el resultado al cajero antes de responderle a HiPOS.
    public HiPosPaymentOrchestrator(
        ProcessSaleHandler sales,
        IGiftCardProvider provider,
        ILogger<HiPosPaymentOrchestrator> logger)
    {
        _sales = sales;
        _provider = provider;
        _logger = logger;
    }

    public async Task<HiPosInitializationOutcome> HandleInitializationAsync(
        string? intentExtraXml,
        IHiPosStoreConfigurationSink configurationSink,
        IHiPosResponseSink sink,
        CancellationToken cancellationToken)
    {
        System.Xml.Linq.XDocument? configuration = null;

        if (!string.IsNullOrWhiteSpace(intentExtraXml))
        {
            try
            {
                configuration = System.Xml.Linq.XDocument.Parse(intentExtraXml);
            }
            catch (System.Xml.XmlException exception)
            {
                _logger.LogWarning(exception, "HiPOS provided an invalid initialization XML.");
                sink.SetResultFailed("The initialization XML is malformed.");
                sink.FinishActivity();
                return new HiPosInitializationOutcome(string.Empty, AppVersion, AppBuild);
            }
        }

        var mapping = _mapper.MapInitialization(configuration);

        if (!mapping.IsSuccess)
        {
            sink.SetResultFailed(mapping.ErrorMessage ?? "Initialization failed.");
            sink.FinishActivity();
            return new HiPosInitializationOutcome(string.Empty, AppVersion, AppBuild);
        }

        var settings = mapping.Configuration!;

        // docs/INTEGRACION_HIPOS.md §9 (INITIALIZE): "Leer el XML
        // Parameters y guardar config (Store ID, passphrase, URL, API version)". Antes solo se
        // parseaba y se devolvía al intent, sin persistir — cada una de las 512 tiendas KOAJ
        // requería configuración manual. TerminalId (la caja física) NO viaja en este intent,
        // así que IHiPosStoreConfigurationSink conserva el que ya hubiera configurado, si lo hay.
        var saved = await configurationSink.ApplyAsync(
            settings.StoreId,
            settings.BaseUrl,
            settings.ApiVersion,
            settings.Passphrase,
            cancellationToken);

        if (!saved)
        {
            _logger.LogWarning(
                "HiPOS INITIALIZE parsed correctly but persisting the configuration failed.");
        }

        var extras = new Dictionary<string, string?>
        {
            [HiPosResponseComposer.AuthorizationIdExtra] = settings.StoreId,
            [HiPosResponseComposer.CardTypeExtra] = "GIFTCARD",
            [HiPosResponseComposer.CardHolderExtra] = settings.Passphrase
        };

        sink.SetResultOk("INITIALIZED", extras);
        sink.FinishActivity();

        return new HiPosInitializationOutcome(
            settings.ApiVersion,
            LibraryVersionName(),
            AppBuild);
    }

    public void HandleFinalization(IHiPosResponseSink sink)
    {
        var extras = new Dictionary<string, string?>
        {
            [HiPosResponseComposer.CardHolderExtra] = "Finalized"
        };

        sink.SetResultOk("FINALIZED", extras);
        sink.FinishActivity();
    }

    /// <summary>
    /// GET_VERSION. Devuelve el extra <c>Version</c> como ENTERO y con el mismo valor registrado
    /// en HioPosCloud (<see cref="ModuleVersionCode"/>). Dos reglas duras aprendidas en terminal:
    /// mandarlo como String hace que HioPos lea -1 y pida reinstalar el módulo en cada arranque;
    /// y no cerrar la Activity deja a HioPos esperando el resultado para siempre.
    /// </summary>
    public string HandleVersion(IHiPosResponseSink sink)
    {
        // Version se manda como int nativo ANTES de SetResultOk, así el bundle no se contamina
        // con un SetResultOk posterior que itere extras como string.
        sink.PutIntExtra(VersionExtra, ModuleVersionCode);
        sink.SetResultOk("VERSION", new Dictionary<string, string?>
        {
            [HiPosResponseComposer.CardHolderExtra] = $"{LibraryName} {AppVersion}+{AppBuild}"
        });
        sink.FinishActivity();
        return AppVersion;
    }

    /// <summary>
    /// GET_BEHAVIOR. Declara las capacidades del módulo.
    /// <para>
    /// docs/INTEGRACION_HIPOS.md §3: HiPOS lee las flags con
    /// <c>Intent.getBooleanExtra(...)</c>. Mandarlas como String "true"/"false" provocaba
    /// <c>ClassCastException: java.lang.String cannot be cast to java.lang.Boolean</c> en
    /// logcat (visto en KOAJ-2026-08-25) y HiPOS descartaba el módulo silenciosamente:
    /// la forma de pago no aparecía en la lista y cualquier TRANSACTION posterior nunca
    /// llegaba. Las flags deben salir como booleanos nativos del Intent. <c>canAudit</c>
    /// arranca en minúscula mientras el resto es PascalCase — literal del contrato.
    /// </para>
    /// <para>
    /// <c>SupportsCredit</c> es la que habilita la forma de pago: un TEF que no declara ni
    /// crédito ni débito no tiene nada que ofrecer y no aparece. <c>HasCustomParams</c> es la
    /// que hace que HioPos llame a GET_CUSTOM_PARAMS por el nombre y el logo del botón.
    /// </para>
    /// </summary>
    public void HandleBehavior(IHiPosResponseSink sink)
    {
        sink.PutBoolExtra("SupportsCredit", true);
        sink.PutBoolExtra("HasCustomParams", true);
        sink.PutBoolExtra("canAudit", true);          // minúscula inicial: es literal del contrato

        // OJO: QUERY_TRANSACTION no es "consultar saldo". El contrato de ICG lo define como
        // recuperar una venta que terminó en UNKNOWN_RESULT, y si respondemos ACCEPTED
        // HioPos da la venta por cobrada. Nuestro handler devuelve el saldo del bono, que
        // NO es eso: declararlo en true haría que HiPOS cerrara ventas sin cobrar tras un
        // resultado incierto. Queda en false hasta implementar la semántica real.
        sink.PutBoolExtra("SupportsTransactionQuery", false);

        // El cierre de caja se acepta sin trabajo extra porque cada venta ya se concilió
        // individualmente contra Ogloba.
        sink.PutBoolExtra("SupportsBatchClose", true);

        // API TEF 4.0 §GetBehavior, literal: "el módulo puede intentar anular transacciones que
        // hayan finalizado con un código de UNKNOWN_RESULT y se desconozca el estado de éstas".
        //
        // OJO — NO es lo que habilita la papelera sobre la línea de pago. Eso se creyó acá y es
        // falso: la papelera de una línea ya cobrada dispara REFUND ("abono de una transacción
        // existente"), no VOID_TRANSACTION. Ver DispatchUndoAsync, que atiende los dos.
        //
        // Va en true porque el caso que cubre es real: si una redención queda en UNKNOWN_RESULT,
        // HiPOS necesita poder pedirnos que la dejemos anulada antes de reintentar el cobro. En
        // false, el POS reintentaba sobre una transacción de estado desconocido.
        //
        // Excluyente con SupportsTransactionQuery (el contrato dice que si van los dos, el query
        // se ignora). Query queda en false, así que no hay conflicto.
        sink.PutBoolExtra("SupportsTransactionVoid", true);

        // Abonos PARCIALES siguen fuera: deshacer suelta la línea completa. Reintegrar una parte
        // de una venta ya cerrada es otra operación y KOAJ no la maneja.
        sink.PutBoolExtra("SupportsPartialRefund", false);
        // "Entrada de caja" en HiPOS manda
        // NEGATIVE_SALE — el cajero redime con el saldo del bono en lugar de dar efectivo.
        // Lo habilitamos para que HiPOS muestre ese botón como medio de pago disponible.
        sink.PutBoolExtra("SupportsNegativeSales", true);
        // VA EN FALSE. Se probó en true el 2026-09-01 y hubo que revertirlo al día siguiente.
        //
        // API TEF 4.0 §GetBehavior: en true, cuando HiPOS pide un abono del total de una venta
        // del terminal actual y de la Z actual, manda VOID_TRANSACTION EN LUGAR DE REFUND.
        //
        // Se probó como hipótesis para desbloquear la papelera de la línea de pago. No funcionó
        // —la papelera siguió sin aparecer— y además rompió el rechazo de notas de crédito: las
        // notas dejaron de llegar como REFUND, se colaron por el camino de VOID_TRANSACTION y se
        // respondieron ACCEPTED en silencio. Cuatro abonos dados por hechos sin mover un peso,
        // medido en terminal el 2026-09-02 (08:45-08:46).
        //
        // En false cada intent vuelve a significar lo que dice el contrato: REFUND es una nota
        // de crédito y se rechaza con motivo; VOID_TRANSACTION solo llega tras un UNKNOWN_RESULT.
        sink.PutBoolExtra("ExecuteVoidWhenAvailable", false);

        // No aplican al medio de pago (bonos regalo).
        sink.PutBoolExtra("SupportsDebit", false);
        sink.PutBoolExtra("SupportsEBTFoodstamp", false);
        sink.PutBoolExtra("SupportsTipAdjustment", false);
        sink.PutBoolExtra("SaveLoyaltyCardNum", false);
        sink.PutBoolExtra("ReadCardFromApi", false);

        // ESTA es la que desbloquea la papelera sobre la línea de pago del bono.
        //
        // Confirmado por ICG el 2026-09-01 (no está en el PDF 4.0 rev 3.8 que tenemos; es
        // posterior). Con la bandera en true, HiPOS avisa al módulo cuando el cajero cancela la
        // totalización —intent TOTALIZATION_CANCELED, ver TotalizationCanceledActivity— y a
        // partir de ahí permite soltar la línea.
        //
        // El camino largo por GET_BEHAVIOR/REFUND que se probó antes no servía: en 8 MB de log
        // de terminal, HiPOS jamás nos mandó REFUND ni VOID_TRANSACTION. Sencillamente no nos
        // preguntaba, y ninguna bandera de las documentadas cambiaba eso.
        sink.PutBoolExtra("CallOnTotalizationCanceled", true);

        // Imprime HiPOS con el XML <Receipt> que devolvemos en TRANSACTION, no el módulo.
        sink.PutBoolExtra("CanPrint", false);

        // En true, HioPos escribe el XML de la venta a disco y pasa la ruta; el scoped
        // storage de Android 13+ impide leerla y el documento llega nulo. Va inline.
        sink.PutBoolExtra("OnlyUseDocumentPath", false);

        sink.SetResultOk("OK", new Dictionary<string, string?>());
        sink.FinishActivity();
    }

    /// <summary>
    /// GET_CUSTOM_PARAMS. El nombre y el logo que ve el cajero en la pantalla de medios de pago.
    /// No salen de la configuración de HioPos: los responde el módulo.
    /// </summary>
    /// <param name="logoPng">
    /// PNG crudo. Es el único extra binario del contrato: va como <c>byte[]</c>, no como ruta ni
    /// Base64. Si es <c>null</c> se manda solo el nombre y HioPos usa su logo genérico.
    /// </param>
    public void HandleCustomParams(IHiPosResponseSink sink, byte[]? logoPng = null)
    {
        if (logoPng is { Length: > 0 })
        {
            sink.PutByteArrayExtra(LogoExtra, logoPng);
        }
        else
        {
            _logger.LogWarning(
                "HiPOS GET_CUSTOM_PARAMS sin logo: no hay {Asset} empaquetado, el botón queda con el ícono genérico.",
                LogoAssetFileName);
        }

        sink.SetResultOk("OK", new Dictionary<string, string?>
        {
            [NameExtra] = ModuleDisplayName
        });

        sink.FinishActivity();
    }

    /// <summary>
    /// GET_PRINT_INFO. El módulo ya devuelve los comprobantes dentro de la respuesta de
    /// TRANSACTION (<c>MerchantReceipt</c> / <c>CustomerReceipt</c>), así que aquí no hay
    /// información adicional que aportar. PENDIENTE de confirmar con ICG si esperan extras
    /// concretos; mientras tanto se responde OK para no bloquear el arranque.
    /// </summary>
    public void HandlePrintInfo(IHiPosResponseSink sink)
    {
        sink.SetResultOk("OK", new Dictionary<string, string?>());
        sink.FinishActivity();
    }

    /// <summary>
    /// SHOW_SETUP_SCREEN. La configuración de tienda/terminal vive en la UI autónoma del módulo
    /// y, en producción, llega por el XML <c>Parameters</c> del INITIALIZE, así que no hay
    /// pantalla que abrir desde HioPos. Se responde OK.
    /// </summary>
    public void HandleSetupScreen(IHiPosResponseSink sink)
    {
        sink.SetResultOk("OK", new Dictionary<string, string?>());
        sink.FinishActivity();
    }

    /// <summary>
    /// READ_CARD / CHARGE_CARD / GET_CARD_DATA. El módulo no opera tarjetas bancarias: redime
    /// bonos. ICG espera <c>Canceled</c> para las operaciones no soportadas.
    /// </summary>
    public void HandleUnsupportedCardOperation(string action, IHiPosResponseSink sink)
    {
        _logger.LogInformation("HiPOS {Action} no soportado por el módulo: se responde Canceled.", action);
        sink.SetResultCanceled();
        sink.FinishActivity();
    }

    public async Task HandleTransactionAsync(
        IReadOnlyDictionary<string, string?> extras,
        string? configurationStoreId,
        // HiPOS NO envía TerminalId/CashierId en el intent TRANSACTION.
        // El llamador (HiPosPaymentActivity) resuelve estos valores desde PosSession
        // persistida (Configuration.TerminalId + ActiveCashierId) y los pasa como fallback.
        string? sessionTerminalId,
        string? sessionCashierId,
        // El llamador valida que el cashierId que HiPOS manda esté
        // registrado en la tienda. Si retorna false, fallamos rápido sin gastar una
        // llamada a Ogloba (que devolvería error 59/230040 con mensaje técnico).
        bool isCashierRegistered,
        // HiPOS no manda el serial: el módulo abre su propia pantalla y captura el número de
        // bono (o el correo para activación virtual). El implementador vive en la capa Android
        // y se inyecta desde DI para que el orquestrador sea testeable sin la Activity.
        IHiPosCardCapture? capture,
        // Datos que solo conoce la capa MAUI (AppEnvironment + PosSession) y que hacen falta
        // para activar un bono virtual: el código de producto digital de Ogloba y el nombre
        // que el cliente ve como remitente del correo.
        string digitalProductCode,
        string senderName,
        IHiPosResponseSink sink,
        CancellationToken cancellationToken)
    {
        // OJO: aquí NO se rechaza por cajero. Medido en terminal (2026-08-26, logcat
        // TefOgloba.HiPos): HiPOS no manda `CashierId` en el intent TRANSACTION — el volcado
        // completo de extras no lo trae. Si además la sesión está vacía (caja recién prendida,
        // turno cerrado), rechazar acá tumbaba el cobro ANTES de levantar el módulo, que es
        // justo donde vive la pantalla "Seleccione su cajero" que resuelve el dato. También
        // tumbaba el BATCH_CLOSE del cierre de caja, que ni cajero necesita.
        //
        // Quién opera se resuelve al abrir el módulo, y la validación contra la lista de
        // cajeros de la tienda queda para el único camino que opera sin abrirlo (ver
        // DispatchSaleAsync cuando el intent ya trae CardNumber).
        var mapping = _mapper.MapTransaction(
            extras,
            configurationStoreId,
            sessionTerminalId,
            sessionCashierId);

        if (!mapping.IsSuccess)
        {
            sink.SetResultFailed(mapping.ErrorMessage ?? "The transaction request is invalid.");
            sink.FinishActivity();
            return;
        }

        var request = mapping.Transaction!;

        // HiPOS invoca al módulo SOLO como medio de pago: el cliente paga su factura con el
        // saldo de un bono. La activación salió de este camino a propósito — ahora es un flujo
        // manual que exige medios de pago desglosados y firma del cliente, y nada de eso cabe
        // dentro de un intent del POS.
        switch (request.TransactionType)
        {
            case HiPosTransactionType.Sale:
            case HiPosTransactionType.NegativeSale:
                // Lo que separa una venta de una entrada de caja es el TENDER, no el
                // TransactionType. Medido en terminal (2026-08-26, logcat TefOgloba.HiPos),
                // dos corridas de cada caso:
                //
                //   venta facturando   → type=SALE  tender=CREDIT  amount=8990000
                //   entrada de caja    → type=SALE  tender=(vacío) amount=6000000
                //
                // Las dos llegan como SALE y con IsAdvancedPayment=false, así que enrutar por
                // el tipo mandaba siempre al mismo lado. El tender sí las distingue:
                //
                //   · Con tender declarado → el cliente PAGA la factura → Redimir saldo.
                //   · Sin tender           → entrada de caja: NO se atiende desde aquí.
                //
                // Activar bonos dejó de entrar por HiPOS. Es un flujo manual con su propia
                // sesión de cajero (usuario y contraseña), y meterlo dentro de un intent del POS
                // significaría activar sin esa autenticación. El cajero cobra en el POS por
                // donde sea y después entra al módulo a activar.
                if (request.TenderType == HiPosTenderType.Unknown)
                {
                    _logger.LogInformation(
                        "HiPOS {Tx} sin tender (entrada de caja): la activación es manual.",
                        request.TransactionType);

                    SendOutcome(
                        request,
                        new HiPosOperationOutcome(
                            Accepted: false,
                            Pending: false,
                            AmountAppliedPesos: 0,
                            ErrorMessage: "Los bonos se activan desde el módulo Ogloba. "
                                + "Cobra por el medio que corresponda y luego entra a Ogloba → Activar bono."),
                        sink);
                    break;
                }

                await DispatchSaleAsync(
                    request,
                    HiPosSaleIntent.Redemption,
                    capture,
                    isCashierRegistered,
                    digitalProductCode,
                    senderName,
                    sink,
                    cancellationToken);
                break;
            // NOTA CRÉDITO. En HiPOS una nota crédito contra un medio de pago llega como REFUND,
            // y hay que rechazarla ANTES de abrir nada: Ogloba no admite devolver dinero a un
            // bono desde este flujo, y dejarla pasar es peor que no soportarla — el módulo
            // respondería ACCEPTED, HiPOS daría la nota por pagada y no se habría movido un peso.
            //
            // Se responde FAILED con el motivo, así que el cajero ve el mensaje en el POS y el
            // módulo ni se muestra (la Activity es invisible y cierra de inmediato).
            // REFUND llega por DOS motivos distintos, y hay que separarlos:
            //
            //  · Quitar la línea de pago del bono en la pantalla de cobro (documento de venta:
            //    tiquet o factura). Se ACEPTA — es lo que HiPOS necesita para desmarcarla.
            //  · Una NOTA DE CRÉDITO (documento de abono). Se RECHAZA.
            //
            // Lo único que las distingue es el DocumentTypeId de la cabecera. Ver DispatchRefund.
            case HiPosTransactionType.Refund:
                DispatchRefund(request, extras, sink);
                break;

            // VOID_TRANSACTION solo llega tras un UNKNOWN_RESULT (API TEF 4.0). Se sigue
            // rechazando: aceptarlo en silencio sin verificar contra Ogloba es lo que dejó pasar
            // cuatro abonos por hechos el 2026-09-02.
            case HiPosTransactionType.VoidTransaction:
                RejectUndo(request, sink);
                break;
            case HiPosTransactionType.BatchClose:
                // docs/OGLOBA_API_REFERENCE.md §3.3 + §1: "Ogloba expect to receive the
                // reconciliation in daily basis, for all the transactions initiated with
                // success". El flujo per-tx (ProcessSaleHandler.CompleteAuthorizedSaleAsync)
                // YA llama /reconciliation inmediatamente después de /confirmTransaction.
                // BATCH_CLOSE de HiPOS es el agregado al cierre de caja — no requiere
                // trabajo extra mientras cada venta se reconcilió individualmente. Devolvemos
                // ACCEPTED para que HiPOS no bloquee el cierre.
                _logger.LogInformation("HiPOS BATCH_CLOSE accepted: per-tx reconciliation already done.");
                sink.SetResultOk("ACCEPTED", new Dictionary<string, string?>());
                sink.FinishActivity();
                return;
            case HiPosTransactionType.QueryTransaction:
                await DispatchQueryAsync(request, sink, cancellationToken);
                break;
            default:
                sink.SetResultFailed($"Unsupported transaction type: {request.TransactionType}.");
                sink.FinishActivity();
                break;
        }
    }

    /// <summary>
    /// Despacha una venta o un abono de HiPOS. El <paramref name="intent"/> lo fija el
    /// orquestrador a partir del <c>TransactionType</c> (SALE → Activar, NEGATIVE_SALE →
    /// Redimir); la pantalla nativa del módulo NO da al cajero la opción de cambiar entre uno
    /// y otro, porque HiPOS ya eligió cuál invocar.
    /// </summary>
    private async Task DispatchSaleAsync(
        HiPosTransactionRequest request,
        HiPosSaleIntent intent,
        IHiPosCardCapture? capture,
        bool isCashierRegistered,
        string digitalProductCode,
        string senderName,
        IHiPosResponseSink sink,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "HiPOS {Tx} (tender={Tender}) → se abre {Intent} por {Amount} {Currency}.",
            request.TransactionType,
            request.TenderType,
            intent,
            request.AmountMinorUnits,
            request.Currency);

        var operation = intent == HiPosSaleIntent.Activation
            ? GiftCardOperation.Activation
            : GiftCardOperation.Redemption;

        // Si HiPOS mandó el serial (no lo hace hoy, pero el contrato no lo prohíbe) se respeta y
        // se opera directo: no tiene sentido levantar una pantalla para pedir algo que ya llegó.
        if (!string.IsNullOrWhiteSpace(request.CardNumber))
        {
            // Único camino que cobra sin abrir el módulo, así que es el único donde hay que
            // exigir un cajero válido: no hay pantalla que lo pregunte.
            if (!isCashierRegistered || string.IsNullOrWhiteSpace(request.CashierId))
            {
                _logger.LogWarning(
                    "HiPOS {Tx} rechazado: cobro directo sin cajero válido (cashierId='{Cashier}').",
                    request.TransactionType,
                    request.CashierId);
                sink.SetResultFailed(
                    "No hay cajero en turno en esta caja. Abre el módulo Ogloba y selecciona tu cajero.");
                sink.FinishActivity();
                return;
            }

            var direct = await _sales.HandleAsync(
                new ProcessSaleCommand(
                    request.StoreId,
                    request.TerminalId,
                    request.CashierId,
                    request.CardNumber!.Trim(),
                    request.AmountMinorUnits,
                    request.Currency,
                    operation),
                cancellationToken);

            // Va por el MISMO camino de respuesta que el resto: eco del TransactionType,
            // comprobantes y AuthorizationId saneado. Tener dos formas de contestarle al fiscal
            // es tener dos formas de que rechace el documento.
            SendOutcome(request, ToOutcome(direct), sink);
            return;
        }

        if (capture is null)
        {
            sink.SetResultFailed("No hay forma de abrir el módulo en esta terminal.");
            sink.FinishActivity();
            return;
        }

        // El módulo hace la operación completa —llama a Ogloba, muestra el resultado y espera a
        // que el cajero lo acepte— y recién entonces devuelve. Así el motivo de un rechazo se ve
        // en la pantalla del bono, que es donde el cajero puede hacer algo al respecto, y no
        // como una alerta genérica de HiPOS después de que el APK se cerró.
        var outcome = await capture.RunAsync(
            intent,
            request.AmountMinorUnits,
            request.Currency,
            cancellationToken);

        if (outcome is null)
        {
            // El cajero salió sin operar: HiPOS recupera el control para elegir otro medio de
            // pago. No se intentó nada contra Ogloba.
            //
            // Se devuelve RESULT_CANCELED (no RESULT_OK + FAILED): HiPOS inspecciona primero el
            // código de resultado entero —RESULT_CANCELED = "el cajero salió, vuelve a tu pantalla
            // de medios de pago"— y RESULT_OK con extras FAILED como "el módulo falló, abortar".
            // Mandar RESULT_OK aquí hacía que HiPOS cerrara el flujo completo y mandara al cajero
            // al launcher en vez de devolverlo a la pantalla de selección de medio de pago.
            //
            // El código se queda en RESULT_CANCELED, pero el intent ya NO va vacío: sin el eco de
            // TransactionType, HiPOS no puede casar la respuesta con la operación que lanzó y
            // levanta su alerta genérica de "error en módulo externo". Salir sin cobrar no es un
            // error —el cajero apretó "Volver a HiPOS"— así que se le manda el motivo redactado
            // para que el POS tenga qué mostrar en vez de esa alerta.
            _logger.LogInformation(
                "HiPOS {Tx} cancelado por el cajero en el módulo.", request.TransactionType);
            sink.SetResultCanceled(new Dictionary<string, string?>
            {
                [HiPosResponseComposer.TransactionResultExtra] = "CANCELED",
                ["TransactionType"] = request.RawTransactionType,
                [HiPosResponseComposer.ErrorMessageTitleExtra] = "No se cobró con bono",
                [HiPosResponseComposer.ErrorMessageExtra] =
                    "Volviste al POS sin usar un bono. La venta sigue abierta: elige otro medio "
                    + "de pago o entra otra vez a Ogloba."
            });
            sink.FinishActivity();
            return;
        }

        SendOutcome(request, outcome, sink);
    }

    /// <summary>
    /// Traduce el resultado del módulo a los extras que HiPOS espera.
    /// <para>
    /// Tres cosas que hacen que HiPOS cierre el documento e imprima, y que costaron errores en
    /// terminal:
    /// </para>
    /// <list type="number">
    /// <item><b>Eco del <c>TransactionType</c> que mandó HiPOS.</b> Si se le contesta un tipo
    /// distinto al que pidió, el POS no da la operación por cerrada y relanza el TRANSACTION —
    /// que es lo que se veía como "error de módulo externo" en la entrada de caja.</item>
    /// <item><b><c>MerchantReceipt</c> y <c>CustomerReceipt</c> siempre.</b> Es lo que HiPOS
    /// imprime; sin ellos no sale nada por la impresora.</item>
    /// <item><b>Nada de numeración fiscal.</b> El consecutivo, el rango y la resolución DIAN
    /// son de HiPOS y de <c>icg.hioposapifiscal</c>. El único campo que viaja al fiscal es
    /// <c>AuthorizationId</c>, y va saneado según el manual de ICG
    /// (ver <see cref="DianFieldSanitizer"/>).</item>
    /// <item><b><c>ModifyDocumentResult</c> como XML, siempre, y con <c>PaymentMeanId</c> no
    /// vacío.</b> Es lo que enriquece el medio de pago del documento. Antes se mandaba el número
    /// de línea pelado y solo cuando HiPOS enviaba <c>PaymentMeanLineNumber</c> —que no lo
    /// envía—, o sea nunca: el medio quedaba sin identificar y el módulo fiscal terminaba en
    /// "no cuenta con folios asociados" (ver <see cref="ModifyDocumentResultBuilder"/>).</item>
    /// </list>
    /// <para>
    /// El extra <c>Amount</c> se devuelve con lo que REALMENTE se aplicó, que puede ser menos
    /// que lo pedido cuando el bono no alcanzaba: así HiPOS sabe cuánto queda pendiente y le
    /// pide el resto al cajero por otro medio de pago, en vez de rechazar el cobro entero.
    /// </para>
    /// </summary>
    private void SendOutcome(
        HiPosTransactionRequest request,
        HiPosOperationOutcome outcome,
        IHiPosResponseSink sink)
    {
        var result = outcome.Accepted
            ? "ACCEPTED"
            : outcome.Pending
                ? "UNKNOWN_RESULT"
                : "FAILED";

        var applied = outcome.Accepted || outcome.Pending
            ? outcome.AmountAppliedPesos > 0 ? outcome.AmountAppliedPesos : request.AmountMinorUnits
            : 0;

        // De vuelta en la MISMA escala en que llegó (contrato de ICG §Transaction: los dos
        // últimos dígitos son decimales).
        var appliedCents = applied * 100;

        var authorizationId = DianFieldSanitizer.AuthorizationId(
            outcome.Reference, request.TransactionId);

        // UN solo comprobante, el del cliente.
        //
        // Antes se armaba el de comercio y se mandaba en los DOS extras (MerchantReceipt y
        // CustomerReceipt), así que la térmica sacaba dos tirillas y ambas decían "COPIA
        // COMERCIO". El cliente se llevaba una copia que no era la suya y el comercio una de
        // sobra: la venta ya queda respaldada por la factura que imprime HiPOS.
        //
        // Se arma también cuando falla: el cliente se lleva constancia de que el cobro se
        // intentó y de por qué no pasó.
        var receipt = ReceiptBuilder.BuildCustomerReceipt(
            request.StoreId,
            request.TerminalId,
            referenceNumber: authorizationId,
            cardDisplayValue: outcome.CardNumber ?? string.Empty,
            amountMinorUnits: applied,
            request.Currency,
            remainingBalanceMinorUnits: outcome.RemainingBalancePesos ?? 0,
            eGiftCardUrl: null);

        var extras = new Dictionary<string, string?>
        {
            [HiPosResponseComposer.TransactionResultExtra] = result,

            // Eco literal del tipo que pidió HiPOS. Sin esto el POS relanza el intent.
            ["TransactionType"] = request.RawTransactionType,

            [HiPosResponseComposer.CardNumExtra] = outcome.CardNumber,
            [HiPosResponseComposer.CardHolderExtra] = outcome.Customer,
            [HiPosResponseComposer.CardTypeExtra] = "GIFTCARD",

            // Solo el del cliente. Mandar además MerchantReceipt hacía que salieran dos tirillas.
            [HiPosResponseComposer.CustomerReceiptExtra] = receipt,

            ["Amount"] = appliedCents.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (outcome.Accepted || outcome.Pending)
        {
            extras[HiPosResponseComposer.AuthorizationIdExtra] = authorizationId;

            // La referencia de Ogloba, para poder rastrear la operación desde el documento.
            extras["TransactionData"] = authorizationId;

            // El medio de pago se nombra SIEMPRE, y con el de Ogloba.
            //
            // Historia, porque este campo ya se equivocó dos veces en direcciones opuestas:
            //
            //  1. Se puso "2" —el medio "Tarjeta" sobre el que consolida Sistecrédito en estas
            //     mismas terminales— copiando su solución. Salió mal: HiPOS aplicó el pago sobre
            //     ESE medio y el POS respondió "la forma de pago tarjeta de crédito no tiene
            //     equivalencia". Medido en terminal el 2026-08-28.
            //  2. De ahí se concluyó dejarlo VACÍO, asumiendo que vacío significaba "deja la
            //     línea que HiPOS ya eligió". No significa eso: con el id vacío HiPOS aplica el
            //     pago sobre su medio por defecto, y la venta se imprime como EFECTIVO. Reportado
            //     desde caja el 2026-09-08, con el bono ya cobrado.
            //
            // La lección de (1) sigue en pie —no se nombra un medio AJENO— pero la conclusión de
            // (2) era falsa. Lo que faltaba era el id real del alta de Ogloba en CloudLicense, y
            // ya lo tenemos medido en la terminal (ver OglobaPaymentMeanId).
            //
            // Precedencia: lo que mande HiPOS manda. El valor medido es el respaldo para cuando
            // no manda nada, que es lo habitual (la lista PaymentMeans del documento todavía está
            // vacía cuando llega el TRANSACTION).
            var paymentMeanId = (request.PaymentMeanId ?? string.Empty).Trim();

            if (paymentMeanId.Length == 0)
            {
                paymentMeanId = OglobaPaymentMeanId;

                _logger.LogInformation(
                    "HiPOS no envió PaymentMeanId: se usa el de Ogloba ({Mean}). Si la venta se "
                    + "imprime con otro medio, es que esta tienda tiene un id distinto.",
                    paymentMeanId);
            }

            var lineNumber = string.IsNullOrWhiteSpace(request.PaymentMeanLineNumber)
                ? DefaultPaymentMeanLineNumber
                : request.PaymentMeanLineNumber!.Trim();

            // Enriquecimiento del MEDIO DE PAGO del documento, no numeración fiscal. Va SIEMPRE
            // que la operación se acepte: antes salía solo si HiPOS mandaba PaymentMeanLineNumber
            // —que no lo manda—, así que no salía nunca y el medio quedaba sin AuthorizationId.
            //
            // Y va como XML <ModifyDocumentResult> completo. Antes se mandaba el número de línea
            // pelado, que no es lo que HiPOS parsea. Ver ModifyDocumentResultBuilder para por qué
            // el XML contiene únicamente PaymentMeans.
            extras["ModifyDocumentResult"] = _modifyDocument.Build(
                paymentMeanId: paymentMeanId,
                type: PaymentMeanType,
                lineNumber: lineNumber,
                amount: appliedCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                authorizationId: authorizationId,

                // TransactionId VA. Se probó quitarlo el 2026-09-01, con dos resultados:
                //
                //  1. NO era lo que bloquea la papelera de la línea de pago. Se midió en
                //     terminal con el XML ya sin el campo y la línea siguió igual — resaltada y
                //     sin papelera. La marca viene de otro lado.
                //  2. Al facturar apareció el error de folios. Se atribuyó a este cambio, pero
                //     el 2026-09-02 volvió a salir CON el campo puesto, y el log mostró que es
                //     una respuesta del backend fiscal (hioposreports-co.saas.com.co):
                //     {"error":true,"Mensaje":"No cuenta con folios asociados","Codigo":109}.
                //     O sea: la tienda se quedó sin rango de folios DIAN. No lo causa este campo.
                //
                // Se mantiene porque el contrato lo define como parte de la línea de pago y no
                // hay razón medida para quitarlo — pero el punto 2 NO es un argumento a favor.
                transactionId: request.TransactionId?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                customFields:
                [
                    ("GiftCardNumber", outcome.CardNumber ?? string.Empty),
                    ("RemainingBalance", (outcome.RemainingBalancePesos ?? 0)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture))
                ]);

            // Pago parcial: el bono no alcanzó para toda la factura.
            //
            // NO se manda FixedPaymentMeanId / FixedPaymentMeanAmount. HiPOS ya tiene el importe
            // del bono por dos vías —el extra `Amount` y el `Amount` de la línea dentro de
            // ModifyDocumentResult—, y agregar el par Fixed* hace que lo cuente OTRA VEZ.
            //
            // Medido en terminal el 2026-09-08, factura de $67.800 cubierta con un bono de
            // $50.000: HiPOS registró $100.000 entregados y pintó la línea de EFECTIVO en
            // -$32.200 (67.800 - 100.000). El cajero veía una devolución que no existe.
            //
            // El par estuvo escrito desde el principio pero NUNCA se ejecutó: exigía
            // paymentMeanId no vacío, y hasta el 2026-09-08 ese id siempre iba vacío. Al poner el
            // id real de Ogloba —el arreglo del recibo que salía como EFECTIVO— este camino se
            // despertó por primera vez y se llevó por delante el pago parcial, que llevaba
            // semanas funcionando.
            //
            // Si algún día hiciera falta corregir el importe del medio, hay que quitar primero
            // una de las otras dos vías. Las tres a la vez no.
            if (applied > 0 && applied < request.AmountMinorUnits)
            {
                _logger.LogInformation(
                    "HiPOS {Tx}: pago parcial, el bono cubrió {Applied} de {Requested}. "
                    + "HiPOS pide el resto por otro medio.",
                    request.TransactionType,
                    applied,
                    request.AmountMinorUnits);
            }
        }
        else
        {
            extras[HiPosResponseComposer.ErrorMessageExtra] =
                outcome.ErrorMessage ?? "La operación no se pudo completar.";
        }

        sink.SetResultOk(result, extras);

        _logger.LogInformation(
            "HiPOS {Tx} {Result}: authorizationId={Auth}, aplicado {Applied} de {Requested}.",
            request.TransactionType,
            result,
            authorizationId,
            applied,
            request.AmountMinorUnits);

        sink.FinishActivity();
    }


    /// <summary>
    /// Adapta el resultado de una operación hecha por el orquestrador al mismo tipo que
    /// devuelven las pantallas del módulo, para que ambos caminos respondan igual.
    /// </summary>
    private static HiPosOperationOutcome ToOutcome(ProcessSaleResult result) =>
        new(
            Accepted: result.Outcome == ProcessSaleOutcome.Approved,
            Pending: result.Outcome == ProcessSaleOutcome.Unknown,
            AmountAppliedPesos: 0,
            Reference: result.ReferenceNumber,
            CardNumber: result.MaskedCardNumber,
            RemainingBalancePesos: result.RemainingBalanceMinorUnits,
            ErrorMessage: result.Outcome == ProcessSaleOutcome.Approved
                ? null
                : $"{result.Error.Code}: {result.Error.Description}");

    private static ProcessSaleCommand BuildSaleCommand(
        HiPosTransactionRequest request,
        string capturedValue,
        GiftCardOperation operation)
    {
        // El serial del bono nunca llega vacío aquí: la pantalla nativa lo exige antes de
        // confirmar. El correo para activación virtual es una @ — se valida también en la
        // pantalla. Llegar vacío sería un bug del módulo, no del contrato.
        return new ProcessSaleCommand(
            request.StoreId,
            request.TerminalId,
            request.CashierId,
            capturedValue.Trim(),
            request.AmountMinorUnits,
            request.Currency,
            operation);
    }

    private void SendSaleResult(
        IHiPosResponseSink sink,
        ProcessSaleResult saleResult,
        string transactionNumber,
        string? customer = null)
    {
        var response = _composer.Compose(
            saleResult,
            transactionNumber: transactionNumber,
            configurationCardType: "GIFTCARD");

        // En un recaudo, el titular que HiPOS imprime y guarda en el documento es el cliente al
        // que se le recaudó, no lo que devolvió Ogloba de la tarjeta.
        if (!string.IsNullOrWhiteSpace(customer))
        {
            response = response with { CardHolder = customer };
        }

        SendComposed(sink, response);
    }

    /// <summary>
    /// TOTALIZATION_CANCELED — el cajero canceló la totalización con un cobro de bono ya hecho.
    /// <para>
    /// Es el caso que motivó todo esto: se facturó con bono, la DIAN no integró, y el cajero
    /// necesita soltar la línea de pago. HiPOS solo avisa de esto si el módulo declara
    /// <c>CallOnTotalizationCanceled=true</c> en GET_BEHAVIOR.
    /// </para>
    /// <para>
    /// <b>Se llama a <c>/voidTransaction</c>, NO a <c>/reversal</c></b>, aunque el ejemplo de ICG
    /// use reversal. ICG no conoce el modelo de dos pasos de Ogloba: <c>/reversal</c> solo
    /// deshace un Step 1 que quedó en <c>Requested</c> porque no supimos su resultado, y nuestra
    /// redención se confirma de inmediato (<c>/redemption</c> → <c>/confirmTransaction</c> →
    /// <c>/reconciliation</c>). Sobre una transacción ya confirmada, <c>/reversal</c> devuelve
    /// error y el saldo NO vuelve: el cliente quedaría sin factura y sin plata.
    /// </para>
    /// <para>
    /// Nunca se responde error al POS por no haber encontrado nada que deshacer: este intent
    /// llega en TODA totalización cancelada, se haya pagado con bono o no.
    /// </para>
    /// </summary>
    /// <param name="documentXml">Documento de venta que manda HiPOS. Puede venir vacío.</param>
    /// <param name="storeId">Tienda configurada. HiPOS NO la manda en este intent.</param>
    /// <param name="terminalId">Caja configurada. Tampoco viaja en el intent.</param>
    /// <param name="cashierId">Cajero en turno. Tampoco viaja en el intent.</param>
    public async Task HandleTotalizationCanceledAsync(
        string? documentXml,
        string? storeId,
        string? terminalId,
        string? cashierId,
        IHiPosResponseSink sink,
        CancellationToken cancellationToken)
    {
        var line = TotalizationDocumentReader.FindOglobaPayment(documentXml);

        if (line is null)
        {
            _logger.LogInformation(
                "HiPOS TOTALIZATION_CANCELED: el documento no trae pago con bono.");

            sink.SetResultOk("OK", new Dictionary<string, string?>());
            sink.FinishActivity();
            return;
        }

        // Igual que en DispatchUndo: NO se llama a Ogloba. Acá solo se le confirma al POS que
        // puede seguir. El dato queda en el log con la referencia y el importe, que es el rastro
        // con el que se cuadra después si nadie deshizo la redención.
        _logger.LogWarning(
            "HiPOS TOTALIZATION_CANCELED: se acepta soltar el pago con bono {Card} SIN devolver "
            + "el saldo en Ogloba (referencia {Reference}, importe {Amount}).",
            line.CardNumber ?? "?",
            line.ReferenceNumber,
            line.AmountText ?? "?");

        sink.SetResultOk("OK", new Dictionary<string, string?>
        {
            [HiPosResponseComposer.AuthorizationIdExtra] = line.ReferenceNumber
        });

        sink.FinishActivity();
    }


    /// <summary>
    /// Rechaza cualquier intento de DESHACER un cobro con bono, venga por donde venga.
    /// <para>
    /// Atiende los dos intents con que HiPOS puede pedirlo:
    /// </para>
    /// <list type="bullet">
    /// <item><b>REFUND</b> — nota de crédito ("abono de una transacción existente", API TEF 4.0
    /// §Transaction).</item>
    /// <item><b>VOID_TRANSACTION</b> — anulación de un cobro que terminó en
    /// <c>UNKNOWN_RESULT</c>.</item>
    /// </list>
    /// <para>
    /// Los DOS se rechazan por decisión de KOAJ (2026-09-02): un cobro con bono no se deshace
    /// desde el POS. Lo que haya que hacer con el saldo se resuelve por el flujo de anulación de
    /// venta, que es otro camino.
    /// </para>
    /// <para>
    /// Cerrar los dos —y no solo REFUND— es lo que hace que la prohibición sea real. El
    /// 2026-09-01 solo se cerraba REFUND, y al prender <c>ExecuteVoidWhenAvailable</c> HiPOS
    /// empezó a mandar los abonos como VOID_TRANSACTION: se colaron por el otro camino y se
    /// respondieron ACCEPTED en silencio. Cuatro abonos dados por hechos sin mover un peso.
    /// Mientras quede una sola puerta abierta, basta un cambio de configuración del POS para
    /// que todo vuelva a colarse sin que nadie se entere.
    /// </para>
    /// <para>
    /// Responder ACCEPTED sin llamar a Ogloba era lo PEOR de los dos mundos: el POS daba la
    /// operación por hecha y el saldo no volvía. Entre mentirle al POS y decirle que no, se
    /// elige decirle que no.
    /// </para>
    /// </summary>
    /// <summary>
    /// Atiende un <c>REFUND</c>, que HiPOS manda por dos motivos que hay que separar.
    /// <list type="bullet">
    /// <item><b>Quitar la línea de pago del bono</b> en la pantalla de cobro. Es el caso que
    /// importa: el cajero cobró con bono, algo salió mal —la DIAN no integró, por ejemplo— y
    /// necesita soltar ese medio de pago para seguir. Se responde ACCEPTED y HiPOS desmarca la
    /// línea. <b>NO se llama a Ogloba</b>: el saldo del bono se resuelve por el flujo de
    /// anulación de venta, que es otro camino. Es una decisión de KOAJ, confirmada con ICG el
    /// 2026-09-03.</item>
    /// <item><b>Una nota de crédito.</b> Se rechaza con motivo: los bonos Ogloba no admiten
    /// devoluciones desde el POS.</item>
    /// </list>
    /// <para>
    /// Lo único del intent que las distingue es <c>Header.DocumentTypeId</c> (API TEF 4.0
    /// §DocumentData): 3 y 4 son abonos —tiquet y factura—, o sea notas de crédito; 1 y 2 son
    /// tiquet y factura de venta normal.
    /// </para>
    /// <para>
    /// Si el documento no llega o no se puede leer, se RECHAZA. Ante la duda no se acepta: un
    /// abono con bono Ogloba no se hace nunca, y aceptar "por si acaso" era exactamente la
    /// rendija por la que uno podía colarse. Cerrarla no cuesta la papelera — cuando el cajero
    /// la aprieta, el documento viaja entero (medido: 15.236 caracteres).
    /// </para>
    /// </summary>
    private void DispatchRefund(
        HiPosTransactionRequest request,
        IReadOnlyDictionary<string, string?> extras,
        IHiPosResponseSink sink)
    {
        var document = ReadIncomingDocument(extras);
        var documentTypeId = TotalizationDocumentReader.ReadDocumentTypeId(document);
        var isCreditNote = TotalizationDocumentReader.IsCreditNote(document);

        if (isCreditNote == true)
        {
            RejectUndo(request, sink);
            return;
        }

        // Sin DocumentTypeId legible NO se acepta. Antes sí, para no arriesgar que la caja
        // quedara trabada sin poder soltar la línea — pero eso dejaba una rendija por la que
        // podía colarse un abono, y la prohibición de KOAJ es que no se cuele NINGUNO.
        //
        // La rendija además era teórica: cuando el cajero aprieta la papelera, el documento que
        // viaja es la venta en curso y llega entero (medido en terminal: DocumentData con 15.236
        // caracteres, ver OnlyUseDocumentPath=false en GET_BEHAVIOR). El caso "no se pudo leer"
        // nunca se observó en ese camino. Así que cerrarla no cuesta la papelera; solo cierra la
        // puerta que quedaba entreabierta.
        //
        // Si esto empieza a aparecer en el log con la papelera funcionando, es señal de que
        // HiPOS cambió lo que manda — y entonces hay que mirar el documento otra vez, no
        // reabrir la rendija.
        if (isCreditNote is null)
        {
            _logger.LogWarning(
                "HiPOS REFUND sin DocumentTypeId legible: se RECHAZA. No se puede distinguir "
                + "quitar la línea de un abono, y un abono con bono Ogloba no se hace nunca.");
            RejectUndo(request, sink);
            return;
        }

        _logger.LogInformation(
            "HiPOS REFUND aceptado (DocumentTypeId={Type}): se suelta la línea de pago del bono. "
            + "NO se devuelve el saldo en Ogloba — referencia {Reference}, importe {Amount}.",
            documentTypeId ?? "<sin dato>",
            request.ReferenceNumber ?? "<sin referencia>",
            request.AmountMinorUnits);

        // Se responde como si el abono se hubiera pagado. Es lo que HiPOS necesita para
        // habilitar el desmarcado de la línea; el importe y el tipo van con los nombres del
        // contrato, igual que en una venta.
        sink.SetResultOk("ACCEPTED", new Dictionary<string, string?>
        {
            [HiPosResponseComposer.TransactionResultExtra] = "ACCEPTED",

            // Eco literal del tipo: sin él el POS no da la operación por cerrada y relanza.
            ["TransactionType"] = request.RawTransactionType,

            // De vuelta en la escala del contrato (los dos últimos dígitos son decimales).
            ["Amount"] = (request.AmountMinorUnits * 100)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),

            // La MISMA referencia de la línea que se suelta, no una nueva: es con la que HiPOS
            // cruza las dos operaciones al conciliar.
            [HiPosResponseComposer.AuthorizationIdExtra] = request.ReferenceNumber,
            ["TransactionData"] = request.ReferenceNumber
        });

        sink.FinishActivity();
    }

    /// <summary>
    /// Documento de venta que manda HiPOS, por los nombres con que puede venir. Llega inline
    /// —confirmado en terminal: <c>DocumentData</c> con 15.236 caracteres— porque GET_BEHAVIOR
    /// declara <c>OnlyUseDocumentPath=false</c>.
    /// </summary>
    private static string? ReadIncomingDocument(IReadOnlyDictionary<string, string?> extras)
    {
        foreach (var key in new[] { "DocumentData", "Document" })
        {
            if (extras.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private void RejectUndo(HiPosTransactionRequest request, IHiPosResponseSink sink)
    {
        var isCreditNote = request.TransactionType == HiPosTransactionType.Refund;

        _logger.LogWarning(
            "HiPOS {Tx} rechazado: los bonos Ogloba no se deshacen desde el POS "
            + "(importe {Amount}, referencia {Reference}).",
            request.TransactionType,
            request.AmountMinorUnits,
            request.ReferenceNumber ?? "<sin referencia>");

        // Se responde con los campos DEL CONTRATO, no con SetResultFailed.
        //
        // SetResultFailed manda `Result=FAILED` + `ErrorMessage`, y `Result` no es un campo de
        // salida del contrato: la API TEF 4.0 §Transaction define `TransactionResult`,
        // `TransactionType`, `ErrorMessage` y `ErrorMessageTitle`. Con el nombre equivocado
        // HiPOS no lo lee como transacción fallida y el cajero no ve ningún aviso — la operación
        // no pasaba y nadie decía por qué (medido en terminal el 2026-09-01).
        //
        // El eco de TransactionType va por la misma razón que en una venta: sin él el POS no da
        // la operación por cerrada y relanza el intent.
        sink.SetResultOk("FAILED", new Dictionary<string, string?>
        {
            [HiPosResponseComposer.TransactionResultExtra] = "FAILED",
            ["TransactionType"] = request.RawTransactionType,
            [HiPosResponseComposer.ErrorMessageTitleExtra] = "Forma de pago no válida",

            // El motivo se redacta según lo que el cajero está intentando: en una nota de
            // crédito puede cambiar de medio y seguir; en una anulación no hay alternativa que
            // dependa de él, así que se le dice a dónde escalar y con qué dato.
            [HiPosResponseComposer.ErrorMessageExtra] = isCreditNote
                ? "Los bonos Ogloba no admiten notas de crédito. Usa otro medio de pago."
                : "Los bonos Ogloba no admiten anulaciones desde el POS. "
                  + $"Escala a soporte con la referencia {request.ReferenceNumber ?? "de la venta"}."
        });

        sink.FinishActivity();
    }


    private async Task DispatchQueryAsync(
        HiPosTransactionRequest request,
        IHiPosResponseSink sink,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CardNumber))
        {
            sink.SetResultFailed("QUERY_TRANSACTION requiere el CardNumber del bono.");
            sink.FinishActivity();
            return;
        }

        var cardIdentifierResult = CardIdentifier.CreatePhysicalCard(request.CardNumber);

        if (cardIdentifierResult.IsFailure)
        {
            sink.SetResultFailed(
                $"{cardIdentifierResult.Error.Code}: {cardIdentifierResult.Error.Description}");
            sink.FinishActivity();
            return;
        }

        var storeIdResult = StoreId.Create(request.StoreId);
        var terminalIdResult = TerminalId.Create(request.TerminalId);
        var cashierIdResult = CashierId.Create(request.CashierId);

        if (storeIdResult.IsFailure || terminalIdResult.IsFailure || cashierIdResult.IsFailure)
        {
            sink.SetResultFailed("Los identificadores de la transacción son inválidos.");
            sink.FinishActivity();
            return;
        }

        var balanceResult = await _provider.GetBalanceAsync(
            new BalanceQuery(
                storeIdResult.Value,
                terminalIdResult.Value,
                cashierIdResult.Value,
                TransactionNumber.Create(BuildLocalTransactionNumber()).Value,
                cardIdentifierResult.Value),
            cancellationToken);

        if (balanceResult.IsFailure)
        {
            sink.SetResultFailed(
                $"{balanceResult.Failure.Code}: {balanceResult.Failure.Description}");
            sink.FinishActivity();
            return;
        }

        // QUERY_TRANSACTION no es una venta — no hay referenceNumber Ogloba. Devolvemos el
        // saldo en AuthorizationId (que HiPOS reenvía a DocGateway como referencia) más un
        // recibo mínimo de una línea con el saldo formateado.
        var balance = balanceResult.Value;
        var masked = CardIdentifier.CreatePhysicalCard(request.CardNumber).Value.MaskedValue;
        var balanceText = $"Saldo {balance.BalanceMinorUnits:N0} {balance.Currency} · estado {balance.Status}";

        var merchantReceipt = ReceiptBuilder.BuildMerchantReceipt(
            request.StoreId,
            request.TerminalId,
            referenceNumber: "QUERY",
            maskedCardNumber: masked,
            amountMinorUnits: 0,
            balance.Currency,
            remainingBalanceMinorUnits: balance.BalanceMinorUnits);

        var resultExtras = new Dictionary<string, string?>
        {
            [HiPosResponseComposer.TransactionResultExtra] = "ACCEPTED",
            [HiPosResponseComposer.AuthorizationIdExtra] = balanceText,
            [HiPosResponseComposer.CardNumExtra] = masked,
            [HiPosResponseComposer.CardTypeExtra] = "GIFTCARD",
            [HiPosResponseComposer.MerchantReceiptExtra] = merchantReceipt,
            [HiPosResponseComposer.CustomerReceiptExtra] = merchantReceipt
        };

        sink.SetResultOk("ACCEPTED", resultExtras);
        sink.FinishActivity();
    }

    private static void SendComposed(IHiPosResponseSink sink, HiPosTransactionResponse response)
    {
        var resultExtras = new Dictionary<string, string?>
        {
            [HiPosResponseComposer.TransactionResultExtra] = MapResult(response.Result),
            [HiPosResponseComposer.AuthorizationIdExtra] = response.AuthorizationId,
            [HiPosResponseComposer.CardNumExtra] = response.CardNum,
            [HiPosResponseComposer.CardHolderExtra] = response.CardHolder,
            [HiPosResponseComposer.CardTypeExtra] = response.CardType,
            [HiPosResponseComposer.ErrorMessageExtra] = response.ErrorMessage,
            [HiPosResponseComposer.MerchantReceiptExtra] = response.MerchantReceiptXml,
            [HiPosResponseComposer.CustomerReceiptExtra] = response.CustomerReceiptXml
        };

        sink.SetResultOk(MapResult(response.Result), resultExtras);
        sink.FinishActivity();
    }

    private static string MapResult(HiPosTransactionResult result) =>
        result switch
        {
            HiPosTransactionResult.Accepted => "ACCEPTED",
            HiPosTransactionResult.Failed => "FAILED",
            HiPosTransactionResult.UnknownResult => "UNKNOWN_RESULT",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };

    private static string LibraryVersionName() => AppVersion;

    // QUERY_TRANSACTION no tiene transactionNumber de HiPOS (no se hace cargo al cliente).
    // docs/OGLOBA_API_REFERENCE.md §3.14 exige transactionNumber (max 20 chars) solo para
    // idempotencia de Ogloba — usamos los últimos 16 dígitos del tick UTC, suficiente
    // para evitar colisiones dentro del mismo turno de cajero.
    private static string BuildLocalTransactionNumber()
    {
        var ticks = DateTimeOffset.UtcNow.Ticks.ToString();
        return ticks.Length <= 20 ? ticks : ticks[^20..];
    }
}

/// <summary>
/// Abstrae la persistencia de configuración de tienda para que <see cref="HiPosPaymentOrchestrator"/>
/// (proyecto sin referencia a Permoda.Pay.Maui, para evitar dependencia circular) pueda guardar
/// lo que llega en el intent INITIALIZE sin conocer <c>PosSession</c> directamente. El
/// implementador conserva el TerminalId ya configurado, si lo hay — este intent no lo trae.
/// </summary>
public interface IHiPosStoreConfigurationSink
{
    Task<bool> ApplyAsync(
        string storeId,
        string baseUrl,
        string apiVersion,
        string? oglobaPassword,
        CancellationToken cancellationToken);
}

public sealed record HiPosInitializationOutcome(
    string ApiVersion,
    string VersionName,
    string VersionCode);

/// <summary>
/// Puerto que el orquestrador usa para devolver el resultado al POS. Vive en Maui.HiPos para
/// que el orquestrador (que no ve tipos de Android) sea testeable sin la Activity. La capa MAUI
/// implementa este contrato con <c>SetResult</c> + <c>Broadcast</c> + <c>Finish</c>.
/// </summary>
public interface IHiPosResponseSink
{
    void SetResultOk(string transactionResult, IDictionary<string, string?> extras);

    void SetResultFailed(string errorMessage);

    /// <summary>
    /// Responde <c>RESULT_CANCELED</c> con los campos del contrato que expliquen la salida.
    /// <para>
    /// El código entero sigue siendo <c>RESULT_CANCELED</c> —eso es lo que le dice a HiPOS
    /// "vuelve a tu pantalla de medios de pago" en vez de "aborta el flujo"— pero el intent ya
    /// no viaja vacío. Un intent sin el eco de <c>TransactionType</c> deja a HiPOS sin poder
    /// casar la respuesta con la operación que lanzó, y lo que el cajero ve es la alerta
    /// genérica de "error en módulo externo" sobre una salida que no fue ningún error.
    /// </para>
    /// </summary>
    void SetResultCanceled(IDictionary<string, string?> extras);

    /// <summary>
    /// <c>RESULT_CANCELED</c> pelado, para las operaciones que el módulo no soporta
    /// (READ_CARD / CHARGE_CARD / GET_CARD_DATA). Ahí no hay nada que explicarle al cajero:
    /// HiPOS pregunta si sabemos hacer algo, decimos que no y sigue. Responder algo siempre es
    /// obligatorio — si un handler termina sin devolver resultado, HioPos se queda esperando
    /// indefinidamente y el cajero pierde la venta.
    /// </summary>
    void SetResultCanceled() => SetResultCanceled(new Dictionary<string, string?>());

    /// <summary>
    /// Añade un extra ENTERO al intent de respuesta. Necesario para <c>Version</c>: HioPos lo lee
    /// con <c>getIntExtra</c> y, si va como String, el Bundle devuelve -1
    /// ("Key Version expected Integer but value was a java.lang.String"), HioPos concluye que el
    /// módulo está en versión -1 y pide reinstalarlo en cada arranque.
    /// </summary>
    void PutIntExtra(string key, int value);

    /// <summary>
    /// Añade un extra BOOLEANO al intent de respuesta. Necesario para las flags de
    /// <c>GET_BEHAVIOR</c>: HiPOS las lee con <c>getBooleanExtra</c>. Mandarlas como String
    /// "true"/"false" provocaba <c>ClassCastException: java.lang.String cannot be cast to
    /// java.lang.Boolean</c> en logcat y HiPOS descartaba el módulo silenciosamente.
    /// </summary>
    void PutBoolExtra(string key, bool value);

    /// <summary>
    /// Añade un extra BINARIO. Solo lo usa <c>Logo</c> en GET_CUSTOM_PARAMS, que viaja como el
    /// PNG crudo: ni ruta ni Base64. Es el único extra binario de todo el contrato.
    /// </summary>
    void PutByteArrayExtra(string key, byte[] value);

    void FinishActivity();
}
