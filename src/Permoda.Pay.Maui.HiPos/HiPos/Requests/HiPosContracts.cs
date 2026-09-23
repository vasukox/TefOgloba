namespace Permoda.Pay.Maui.HiPos;

public enum HiPosTransactionType
{
    Sale = 1,
    NegativeSale = 2,
    Refund = 3,
    VoidTransaction = 4,
    BatchClose = 5,
    QueryTransaction = 6
}

/// <summary>
/// Medio de pago que HiPOS declara en el extra <c>TenderType</c> del TRANSACTION. Hoy el
/// módulo solo consume el campo para diagnóstico — el routing de la operación depende del
/// <see cref="HiPosTransactionType"/>, no del tender. Los valores conocidos reflejan el
/// contrato público de ICG; cualquier cadena que no calce cae en <see cref="Unknown"/> y se
/// registra en log sin alterar el flujo.
/// </summary>
public enum HiPosTenderType
{
    Unknown = 0,
    Credit = 1,
    Debit = 2,
    EbtFoodstamp = 3,
    Ogloba = 4
}

public sealed record HiPosInitializationConfiguration(
    string StoreId,
    string Passphrase,
    string BaseUrl,
    string ApiVersion);

public sealed record HiPosTransactionRequest
{
    public required HiPosTransactionType TransactionType { get; init; }

    public required string StoreId { get; init; }

    public required string TerminalId { get; init; }

    public required string CashierId { get; init; }

    public required long AmountMinorUnits { get; init; }

    public required string Currency { get; init; }

    public string? ReferenceNumber { get; init; }

    public string? CardNumber { get; init; }

    /// <summary>
    /// Medio de pago declarado por HiPOS en el extra <c>TenderType</c>. Es solo diagnóstico:
    /// no afecta el routing, pero se loguea para confirmar en terminal que HiPOS está
    /// enviando el valor esperado (típicamente <see cref="HiPosTenderType.Ogloba"/>).
    /// </summary>
    public HiPosTenderType TenderType { get; init; } = HiPosTenderType.Unknown;

    /// <summary>
    /// Extra <c>IsAdvancedPayment</c> del contrato de ICG ("Adelanto de pedido"). Queda como
    /// información: el routing de la operación ya está fijo por <see cref="TransactionType"/>
    /// (SALE → Activar, NEGATIVE_SALE → Redimir), pero si HiPOS manda SALE con este flag en
    /// true seguimos respetando la semántica anterior para no romper integraciones que aún
    /// dependan de él. Ver el switch en <c>HandleTransactionAsync</c>.
    /// </summary>
    public bool IsAdvancedPayment { get; init; }

    /// <summary>
    /// El <c>TransactionType</c> TAL CUAL lo mandó HiPOS. Se devuelve como eco en la respuesta:
    /// si el POS recibe un tipo distinto al que pidió, no da la operación por cerrada y relanza
    /// el intent — se veía como "error de módulo externo".
    /// </summary>
    public string RawTransactionType { get; init; } = string.Empty;

    /// <summary>
    /// Línea del medio de pago dentro del documento de HiPOS. Se devuelve en
    /// <c>ModifyDocumentResult</c> para que el POS sepa sobre qué línea aplicar el resultado.
    /// No es numeración fiscal.
    /// </summary>
    public string? PaymentMeanLineNumber { get; init; }

    /// <summary>
    /// Identificador del medio de pago del CloudLicense sobre el que se consolida Ogloba.
    /// <para>
    /// Casi nunca llega: cuando HiPOS lanza el TRANSACTION, la lista <c>PaymentMeans</c> del
    /// documento todavía está vacía — el medio se agrega DESPUÉS de que respondemos. Por eso no
    /// se lee del documento y hay un valor por defecto en el orquestrador. Lo que no puede pasar
    /// es que viaje vacío: con un id vacío el módulo fiscal no encuentra sobre qué medio aplicar
    /// el pago y termina reportando "no cuenta con folios asociados".
    /// </para>
    /// </summary>
    public string? PaymentMeanId { get; init; }

    /// <summary>
    /// <c>TransactionId</c> de HiPOS. Solo se usa como respaldo del <c>AuthorizationId</c>
    /// cuando Ogloba no devolvió referencia — el campo no puede viajar vacío al fiscal.
    /// </summary>
    public long? TransactionId { get; init; }
}

/// <summary>
/// Qué operación de Ogloba corresponde a un SALE de HiPOS.
/// <para>
/// Venta normal (factura con forma de pago Ogloba) → se activa un bono con el importe de la
/// factura. Abono/entrada de caja → se redime saldo de un bono existente.
/// </para>
/// </summary>
public enum HiPosSaleIntent
{
    Activation = 1,
    Redemption = 2
}

public enum HiPosTransactionResult
{
    Accepted = 1,
    Failed = 2,
    UnknownResult = 3
}

public sealed record HiPosTransactionResponse(
    HiPosTransactionResult Result,
    string? AuthorizationId,
    string? CardNum,
    string? CardHolder,
    string? CardType,
    string? MerchantReceiptXml,
    string? CustomerReceiptXml,
    string? ErrorMessage,
    long? RemainingBalanceMinorUnits,
    string? Currency);