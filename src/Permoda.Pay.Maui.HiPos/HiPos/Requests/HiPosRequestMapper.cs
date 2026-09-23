using System.Globalization;
using System.Xml.Linq;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Maui.HiPos.Requests;

public sealed class HiPosRequestMapper
{
    public const string StoreIdProperty = "StoreId";
    public const string PassphraseProperty = "Passphrase";
    public const string BaseUrlProperty = "BaseUrl";
    public const string ApiVersionProperty = "ApiVersion";
    public const string TransactionTypeProperty = "TransactionType";
    public const string StoreIdIntentProperty = "MerchantId";
    public const string TerminalIdProperty = "TerminalId";
    public const string CashierIdProperty = "CashierId";
    public const string AmountProperty = "Amount";
    public const string CurrencyProperty = "Currency";
    public const string ReferenceNumberProperty = "TransactionData";
    public const string CardNumberProperty = "CardNumber";

    /// <summary>
    /// "Adelanto de pedido" en el contrato de ICG. Distingue el abono de la venta: los dos
    /// llegan como <c>TransactionType=SALE</c>.
    /// </summary>
    public const string IsAdvancedPaymentProperty = "IsAdvancedPayment";

    /// <summary>
    /// Medio de pago declarado por HiPOS en el extra <c>TenderType</c>. Se parsea solo como
    /// diagnóstico — el routing lo define <see cref="TransactionTypeProperty"/>, no el tender.
    /// </summary>
    public const string TenderTypeProperty = "TenderType";

    public HiPosRequestMappingResult MapInitialization(XDocument? configurationDocument)
    {
        if (configurationDocument?.Root is null)
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.configuration.missing",
                "HiPOS must provide an initialization XML configuration.");
        }

        var storeId = Read(configurationDocument, StoreIdProperty);
        var passphrase = Read(configurationDocument, PassphraseProperty);
        var baseUrl = Read(configurationDocument, BaseUrlProperty);
        var apiVersion = Read(configurationDocument, ApiVersionProperty);

        if (string.IsNullOrWhiteSpace(storeId) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiVersion))
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.configuration.incomplete",
                "The initialization configuration is missing required fields.");
        }

        var baseUrlResult = ParseBaseUrl(baseUrl);

        if (baseUrlResult.IsFailure)
        {
            return HiPosRequestMappingResult.Failed(
                baseUrlResult.Error.Code,
                baseUrlResult.Error.Description);
        }

        return HiPosRequestMappingResult.Init(
            new HiPosInitializationConfiguration(
                storeId.Trim(),
                passphrase?.Trim() ?? string.Empty,
                baseUrlResult.Value,
                apiVersion.Trim()));
    }

    public HiPosRequestMappingResult MapTransaction(
        IReadOnlyDictionary<string, string?> extras,
        string? configurationStoreId,
        // HiPOS NO envía TerminalId/CashierId en el intent TRANSACTION —
        // solo los manda en INITIALIZE/SETUP. Aquí aceptamos fallback desde la sesión
        // persistida (Configuration.TerminalId + ActiveCashierId) para no rechazar la
        // transacción cuando el POS los omitió. El orquestador pasa estos valores desde
        // PosSession. Si tampoco están ahí, marcamos "missing" para que HiPOS se entere.
        string? sessionTerminalId = null,
        string? sessionCashierId = null)
    {
        var missingFields = new List<string>();

        if (!TryRead(extras, TransactionTypeProperty, out var transactionTypeText))
        {
            missingFields.Add(TransactionTypeProperty);
        }

        if (!TryRead(extras, StoreIdIntentProperty, out var storeId) ||
            string.IsNullOrWhiteSpace(storeId))
        {
            storeId = configurationStoreId;
        }

        var terminalId = TryRead(extras, TerminalIdProperty, out var terminalIdFromIntent)
            && !string.IsNullOrWhiteSpace(terminalIdFromIntent)
                ? terminalIdFromIntent
                : sessionTerminalId;

        if (string.IsNullOrWhiteSpace(terminalId))
        {
            missingFields.Add(TerminalIdProperty);
        }

        var cashierId = TryRead(extras, CashierIdProperty, out var cashierIdFromIntent)
            && !string.IsNullOrWhiteSpace(cashierIdFromIntent)
                ? cashierIdFromIntent
                : sessionCashierId;

        // El cajero NO se exige aquí. Medido en terminal (2026-08-26): HiPOS no manda
        // `CashierId` en el intent TRANSACTION, y la sesión puede estar vacía si la caja se
        // acaba de prender. Exigirlo rechazaba el cobro antes de que el módulo alcanzara a
        // abrir la pantalla que pregunta quién está operando. Quien opera sin abrir el módulo
        // valida por su cuenta (ver HiPosPaymentOrchestrator.DispatchSaleAsync).

        // El monto se lee sin exigirlo aquí: su obligatoriedad depende del tipo (más abajo).
        TryRead(extras, AmountProperty, out var amountText);
        TryRead(extras, CurrencyProperty, out var currency);
        TryRead(extras, ReferenceNumberProperty, out var referenceNumber);
        TryRead(extras, CardNumberProperty, out var cardNumber);
        // TenderType se parsea solo para diagnóstico: el routing por TransactionType ya está
        // decidido por el contrato y no se modifica con esto. Si el POS lo manda vacío o
        // con un valor desconocido, queda como Unknown y se registra en log sin romper nada.
        var tenderType = MapTenderType(TryRead(extras, TenderTypeProperty, out var tenderRaw) ? tenderRaw : null);

        if (missingFields.Count > 0)
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.transaction.incomplete",
                $"The HiPOS transaction request is missing: {string.Join(", ", missingFields)}.");
        }

        if (string.IsNullOrWhiteSpace(storeId))
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.store.unknown",
                "The merchant identifier was not provided in the configuration or intent.");
        }

        if (!IsKnownTransactionType(transactionTypeText))
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.transaction.unsupported",
                $"The HiPOS transaction type '{transactionTypeText}' is not supported.");
        }

        var transactionType = MapTransactionType(transactionTypeText!);

        // Los requisitos de monto, PAN y referencia dependen del tipo.
        // Antes se exigían monto>0 y PAN para TODOS los tipos, por lo que
        // VOID_TRANSACTION, REFUND, BATCH_CLOSE y QUERY_TRANSACTION eran imposibles de mapear.
        var requiresAmount = transactionType is not (
            HiPosTransactionType.BatchClose or HiPosTransactionType.QueryTransaction);
        // El serial del bono NO llega nunca en el intent: los campos de entrada de un SALE en el
        // contrato de ICG son TransactionType, TenderType, Amount, TipAmount, TaxAmount,
        // TaxDetail, TransactionId, TransactionData, ReceiptPrinterColumns, ShopData, SellerData
        // y DocumentPath. El módulo lo captura al levantarse (ver IHiPosCardCapture). Exigirlo
        // aquí hacía que toda venta por HiPOS fallara con "hipos.card.missing".
        const bool requiresCard = false;

        // La referencia YA NO se exige en REFUND / VOID_TRANSACTION.
        //
        // Se exigía cuando esos intents se resolvían llamando a Ogloba, que identifica la
        // transacción por ella. Hoy no llamamos a Ogloba: soltar la línea de pago es una acción
        // del POS (ver DispatchUndo), y la referencia quedó solo como dato de trazabilidad.
        //
        // Rechazar acá por falta de un dato que ya no se usa devolvía la caja al estado que este
        // camino existe para evitar: la línea trabada y el cajero sin poder seguir.
        const bool requiresReference = false;

        var amountMinorUnits = 0L;
        var hasAmount = TryParseHiPosAmount(amountText, out var parsedAmount, out var hadFractionalCents);

        if (hadFractionalCents)
        {
            // El peso colombiano no maneja céntimos: si llegan, algo está mal configurado.
            // Se trunca (nunca se cobra de más) y queda constancia para poder auditarlo.
            System.Diagnostics.Debug.WriteLine(
                $"[HiPosRequestMapper] Importe con céntimos no representables en COP: '{amountText}' -> {parsedAmount}");
        }

        if (requiresAmount)
        {
            if (!hasAmount || parsedAmount <= 0)
            {
                return HiPosRequestMappingResult.Failed(
                    "hipos.transaction.amount_invalid",
                    "The amount must be a positive integer in minor units.");
            }

            amountMinorUnits = parsedAmount;
        }
        else if (hasAmount && parsedAmount >= 0)
        {
            amountMinorUnits = parsedAmount;
        }

        if (requiresCard && string.IsNullOrWhiteSpace(cardNumber))
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.card.missing",
                "The card number is required to build a sale authorization.");
        }

        if (requiresReference && string.IsNullOrWhiteSpace(referenceNumber))
        {
            return HiPosRequestMappingResult.Failed(
                "hipos.reference.missing",
                "The original transaction reference is required for a void or refund.");
        }

        return HiPosRequestMappingResult.ForTransaction(new HiPosTransactionRequest
        {
            TransactionType = transactionType,
            StoreId = storeId.Trim(),
            TerminalId = terminalId!.Trim(),
            CashierId = cashierId?.Trim() ?? string.Empty,
            AmountMinorUnits = amountMinorUnits,
            Currency = string.IsNullOrWhiteSpace(currency)
                ? Money.DefaultCurrency
                : currency!.Trim().ToUpperInvariant(),
            ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber)
                ? null
                : referenceNumber.Trim(),
            CardNumber = string.IsNullOrWhiteSpace(cardNumber)
                ? null
                : cardNumber.Trim(),
            TenderType = tenderType,
            IsAdvancedPayment = ParseFlag(extras, IsAdvancedPaymentProperty),

            // Se conserva el texto original para poder devolverlo como eco: HiPOS espera el
            // mismo tipo que mandó, no una traducción nuestra.
            RawTransactionType = transactionTypeText!.Trim(),
            PaymentMeanLineNumber = TryRead(extras, "PaymentMeanLineNumber", out var line)
                ? line
                : null,

            // Se acepta con cualquiera de los dos nombres del contrato. Hoy HiPOS no manda
            // ninguno en el TRANSACTION (la lista de medios del documento aún está vacía), así
            // que en la práctica gana el valor por defecto del orquestrador. Se lee igual para
            // que, el día que llegue, mande el del documento real en vez del nuestro.
            PaymentMeanId = TryRead(extras, "PaymentMeanId", out var meanId)
                ? meanId
                : TryRead(extras, "FixedPaymentMeanId", out var fixedMeanId)
                    ? fixedMeanId
                    : null,
            TransactionId = TryRead(extras, "TransactionId", out var txId)
                && long.TryParse(txId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTxId)
                    ? parsedTxId
                    : null
        });
    }

    /// <summary>
    /// Convierte el importe que manda HioPos a las unidades que consume Ogloba (pesos enteros).
    /// <para>
    /// Contrato de ICG (API de Desarrollo de un Módulo de Cobro Electrónico 4.0, §Transaction):
    /// «El formato de éste campo es un entero (serializado cómo string para mantener los ceros a
    /// la izquierda) dónde los dos últimos dígitos son los decimales. Por ejemplo, el importe
    /// 0.01 se recibirá como 001». Es decir, HioPos manda el importe MULTIPLICADO POR 100: una
    /// venta de $57.415 llega como <c>5741500</c> y hay que dividir entre 100.
    /// </para>
    /// <para>
    /// Algunas versiones lo emiten con separador decimal explícito (<c>57415,0000</c>). Ese
    /// formato ya viene en unidades, no en céntimos, y las dos lecturas coinciden en el mismo
    /// importe. Por eso se distingue por la presencia del separador.
    /// </para>
    /// <para>
    /// Regla crítica: si hay separador, el último es SIEMPRE el decimal y los anteriores son de
    /// miles. Parsear <c>57415,0000</c> con <c>NumberStyles.Any</c> e <c>InvariantCulture</c>
    /// interpreta la coma como separador de miles y devuelve <c>574150000</c>: diez mil veces el
    /// importe, sin lanzar error.
    /// </para>
    /// </summary>
    /// <param name="hadFractionalCents">
    /// Queda en <c>true</c> cuando el importe traía céntimos distintos de cero. El peso
    /// colombiano no se subdivide, así que eso señala un desajuste de moneda o de configuración:
    /// el llamador lo registra en el log en vez de cobrarlo en silencio.
    /// </param>
    internal static bool TryParseHiPosAmount(string? raw, out long amount, out bool hadFractionalCents)
    {
        amount = 0;
        hadFractionalCents = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        var lastSeparator = text.LastIndexOfAny([',', '.']);

        if (lastSeparator >= 0)
        {
            // Formato con separador: el valor ya está en unidades de la moneda.
            var integerPart = text[..lastSeparator].Replace(",", string.Empty).Replace(".", string.Empty);
            var fractionPart = text[(lastSeparator + 1)..];

            var normalized = string.IsNullOrEmpty(fractionPart)
                ? integerPart
                : $"{integerPart}.{fractionPart}";

            if (!decimal.TryParse(
                    normalized,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var units))
            {
                return false;
            }

            hadFractionalCents = units != decimal.Truncate(units);
            amount = (long)decimal.Truncate(units);
            return true;
        }

        // Formato del contrato: entero con los dos últimos dígitos como decimales.
        if (!long.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var minorUnits))
        {
            return false;
        }

        hadFractionalCents = minorUnits % 100 != 0;
        amount = minorUnits / 100;
        return true;
    }

    /// <summary>
    /// Lee un extra booleano del intent. HiPOS lo mete en el Bundle como <c>Boolean</c>, y la
    /// capa Android lo aplana con <c>ToString()</c>, así que llega como "true"/"false"; algunas
    /// versiones lo mandan como 0/1. Cualquier cosa que no se reconozca se toma como
    /// <c>false</c>: ante la duda, el módulo abre en Activar, que es el caso frecuente, y el
    /// cajero puede cambiar a Redimir en la misma pantalla.
    /// </summary>
    private static bool ParseFlag(IReadOnlyDictionary<string, string?> extras, string key)
    {
        if (!TryRead(extras, key, out var raw))
        {
            return false;
        }

        var text = raw!.Trim();

        return bool.TryParse(text, out var parsed)
            ? parsed
            : text is "1";
    }

    private static Result<string> ParseBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            return Result<string>.Failure(DomainError.Validation(
                "hipos.configuration.baseurl_invalid",
                "The base URL must be an absolute HTTPS address."));
        }

        return Result<string>.Success(
            parsed.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? parsed.AbsoluteUri
                : $"{parsed.AbsoluteUri}/");
    }

    private static string? Read(XDocument document, string name) =>
        document.Root!.Descendants(name).FirstOrDefault()?.Value;

    private static bool TryRead(
        IReadOnlyDictionary<string, string?> extras,
        string key,
        out string? value)
    {
        if (extras.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static HiPosTransactionType MapTransactionType(string value) =>
        value switch
        {
            "SALE" => HiPosTransactionType.Sale,
            "NEGATIVE_SALE" => HiPosTransactionType.NegativeSale,
            "REFUND" => HiPosTransactionType.Refund,
            "VOID_TRANSACTION" => HiPosTransactionType.VoidTransaction,
            "BATCH_CLOSE" => HiPosTransactionType.BatchClose,
            "QUERY_TRANSACTION" => HiPosTransactionType.QueryTransaction,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

    private static bool IsKnownTransactionType(string? value) => value switch
    {
        "SALE" or "NEGATIVE_SALE" or "REFUND" or "VOID_TRANSACTION" or "BATCH_CLOSE" or "QUERY_TRANSACTION"
            => true,
        _ => false
    };

    /// <summary>
    /// Traduce el extra <c>TenderType</c> al enum <see cref="HiPosTenderType"/>. La lista de
    /// valores conocidos sale del contrato público de ICG ("CREDIT", "DEBIT", "EBT_FOODSTAMP");
    /// "OGLOBA" es el valor que HiPOS manda cuando el cajero eligió nuestro medio de pago
    /// (pendiente de confirmar el literal exacto en una captura de logcat real). Cualquier
    /// otro valor cae en <see cref="HiPosTenderType.Unknown"/> sin lanzar excepción.
    /// </summary>
    internal static HiPosTenderType MapTenderType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return HiPosTenderType.Unknown;
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            "CREDIT" => HiPosTenderType.Credit,
            "DEBIT" => HiPosTenderType.Debit,
            "EBT_FOODSTAMP" => HiPosTenderType.EbtFoodstamp,
            "OGLOBA" => HiPosTenderType.Ogloba,
            _ => HiPosTenderType.Unknown
        };
    }
}
