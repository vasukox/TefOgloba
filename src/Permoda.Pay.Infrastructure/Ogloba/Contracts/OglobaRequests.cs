namespace Permoda.Pay.Infrastructure.Ogloba.Contracts;

// docs/OGLOBA_API_REFERENCE.md §3.2-3.3: pinCode, note y track2Data son opcionales pero
// mejoran la validación y la trazabilidad (especialmente pinCode, que protege contra el
// bloqueo por intentos incorrectos del bono).
/// <param name="Email">
/// Correo del beneficiario, SOLO para activar una tarjeta digital en tienda física.
/// <para>
/// Confirmado por Ogloba (Gilberto, 2026-09-09) respondiendo a nuestra consulta sobre Order
/// management: <i>"orderCreation, orderConfirm, orderStatus y orderCancel normalmente se utilizan
/// para generar lotes de tarjetas (alto volumen). Para la activación de una tarjeta digital en
/// tienda física deben usar la API activation e incluir dos parámetros: gencode y email... Una vez
/// que se confirme la activación, la tarjeta será enviada al beneficiario según la dirección de
/// correo electrónico que se haya colocado."</i>
/// </para>
/// <para>
/// Este campo NO está en la tabla de la §3.1 de nuestra referencia de la API, y esa omisión nos
/// costó una conclusión equivocada: dimos por bloqueado el bono digital en producción porque los
/// recursos de Order management no estaban publicados en el APIM. No hacían falta.
/// </para>
/// </param>
/// <param name="Reason">Texto libre que Ogloba acepta junto al correo. Hoy no se usa.</param>
internal sealed record OglobaRedemptionRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string TransactionNumber,
    long Amount,
    string Currency,
    string? CardNumber,
    string? Gencode,
    string? PinCode,
    string? Note,
    string? Track2Data,
    string? Email = null,
    string? Reason = null);

// docs/OGLOBA_API_REFERENCE.md §3.14: el endpoint exige terminalId, cashierId y
// transactionNumber además de merchantId. Sin esos 3 Ogloba responde 400 (verificado en
// UAT: la versión anterior con solo merchantId + cardNumber/gencode era el root cause
// del HTTP 400 al consultar saldo).
internal sealed record OglobaBalanceRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string TransactionNumber,
    string? CardNumber,
    string? Gencode,
    string? PinCode);

// Postman oficial "Ogloba - GiftCard integration" (Step 2): confirmTransaction y
// cancelTransaction solo piden estos 4 campos — sin amount, sin transactionNumber, sin
// original* (eso es específico de voidTransaction/reversal, que sí lo piden).
internal sealed record OglobaConfirmCancelRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string ReferenceNumber);

internal sealed record OglobaReversalRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string TransactionNumber,
    string? ReferenceNumber,
    string OriginalMerchantId,
    string OriginalTerminalId,
    string OriginalCashierId,
    string OriginalTransNumber);

// docs/OGLOBA_API_REFERENCE.md §3.8. Anula una transacción YA CONFIRMADA y devuelve el saldo
// al bono — es distinto de /reversal, que solo deshace un Step 1 que quedó en `Requested`.
//
// El path es "voidTransaction", no "void": documentación interna antigua
// lo llama /void y el Postman oficial de Ogloba desmiente ese nombre.
//
// `referenceNumber` admite 40 caracteres acá (el doble que en el resto del contrato) porque es
// la referencia que devolvió la operación original, no una que generemos nosotros.
internal sealed record OglobaVoidRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string OriginalMerchantId,
    string OriginalTerminalId,
    string OriginalCashierId,
    string ReferenceNumber,
    string? TransactionNumber,
    string? Note,
    string? Reason,
    string? TransactionTime);

// Postman oficial ("Ogloba - GiftCard integration", Step 3): el nivel superior solo lleva
// merchantId + businessDate — NO terminalId (eso va por registro, en reconciliationRecords).
internal sealed record OglobaReconciliationRequest(
    string MerchantId,
    string BusinessDate,
    IReadOnlyCollection<OglobaReconciliationDetail> ReconciliationRecords);

internal sealed record OglobaReconciliationDetail(
    string TerminalTxNo,
    long LineCount,
    string TerminalId,
    string CashierId,
    string TransactionNumber,
    string ReferenceNumber,
    string TransactionType,
    string Currency,
    long Amount,
    string FinalStatus);

// docs/OGLOBA_API_REFERENCE.md §3.9-3.10 (Order management) — único flujo con envío
// de correo automático por parte de Ogloba.
/// <summary>
/// Ítem de /orderCreation. Los campos nulos no se serializan (JsonIgnoreCondition.WhenWritingNull),
/// así que los opcionales simplemente no viajan cuando no hay dato.
/// <para>
/// <c>DeliverType</c> "3" (eMail) es lo que hace que Ogloba envíe el bono al cliente. La
/// PLANTILLA del correo —asunto, diseño, marca— la define Ogloba en su backend; desde aquí solo
/// se aportan los datos que se inyectan en ella: <c>Message</c>, <c>SenderName</c> y
/// <c>SenderEmail</c>.
/// </para>
/// <para>
/// <c>receiverName</c> se excluye a propósito: verificado en vivo (pedido MA2608000014), Ogloba
/// lo almacena en <c>mobileNo</c> —el campo del teléfono del cliente—, así que mandarlo ensucia
/// el registro del bono sin aportar nada a un envío que siempre es por correo.
/// </para>
/// </summary>
internal sealed record OglobaOrderItem(
    string ItemCode,
    long FaceAmount,
    int Quantity,
    string DeliverType,
    string? ReceiverEmail,
    string? Message = null,
    string? SenderName = null,
    string? SenderEmail = null,

    // docs/OGLOBA_API_REFERENCE.md §3.9, máx 60. Es donde viaja la identificación del cliente
    // (CC-NombreApellido) en las activaciones VIRTUALES: /orderCreation no tiene campo `note`
    // —el que sí usa la activación física— y este es el único campo libre que Ogloba muestra en
    // el reporte, bajo la columna "Phone No.".
    //
    // NO se usa `message` para eso: ese campo es el mensaje del regalo y termina a la vista del
    // cliente en el correo que le manda Ogloba.
    string? ReceiverMobileNo = null);

/// <summary>
/// Pago declarado en /orderConfirm. <c>PaymentType</c> "01" es el valor por defecto del contrato
/// y <c>PaymentId</c> debe ser único por pago.
/// </summary>
internal sealed record OglobaOrderPayment(
    string PaymentType,
    string PaymentId);

internal sealed record OglobaOrderCreationRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string ClientOrderNo,
    string SalesType,
    IReadOnlyCollection<OglobaOrderItem> OrderItems);

internal sealed record OglobaOrderConfirmRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string OrderNo,
    long ShippingFee,
    long CardFee,
    long ReturnFee,
    IReadOnlyCollection<OglobaOrderPayment>? PaymentList = null);

// docs/OGLOBA_API_REFERENCE.md §3.11 (/orderCancel) y §3.12 (/orderStatus): mismo body mínimo
// en ambos endpoints — merchantId/terminalId/cashierId/orderNo, sin fees.
internal sealed record OglobaOrderRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string OrderNo);

// docs/OGLOBA_API_REFERENCE.md §3.13 (orderReturn): returnType F|P, partialReturnDetails
// opcional (requerido si P). Los importes y quantities se mandan en pesos/raw (igual que /redemption).
internal sealed record OglobaOrderReturnRequest(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string OrderNo,
    string ReturnType,
    long ReturnFee,
    long CardFee,
    long ShippingFee,
    IReadOnlyCollection<OglobaPartialReturnDetail>? PartialReturnDetails);

internal sealed record OglobaPartialReturnDetail(
    string? CardNoB,
    string? CardNoE,
    long FaceValue,
    string ItemCode,
    int Qty);

// docs/OGLOBA_API_REFERENCE.md §3.16 (getProducts). Acepta filtro opcional por itemCode.
internal sealed record OglobaGetProductsRequest(
    string MerchantId,
    string? ItemCode);

// docs/OGLOBA_API_REFERENCE.md §3.15 (queryTransactionsHistory). pageNo y numberOfPage son
// mandatory; el resto son filtros opcionales ("at least one criteria").
internal sealed record OglobaQueryTransactionsHistoryRequest(
    string MerchantId,
    string? TerminalId,
    string? CashierId,
    string? TransDateFrom,
    string? TransDateTo,
    string? CardNumber,
    string? TransType,
    string? TransactionStatus,
    string? ReconciliationStatus,
    string? OrderNo,
    string? ReferenceNumber,
    int PageNo,
    int NumberOfPage);
