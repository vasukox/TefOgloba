using System.Text.Json;
using System.Text.Json.Serialization;

namespace Permoda.Pay.Infrastructure.Ogloba.Contracts;

internal sealed class OglobaResponse
{
    public bool IsSuccessful { get; init; }

    public JsonElement ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ReferenceNumber { get; init; }

    public long? Balance { get; init; }

    public string? Currency { get; init; }

    public string? CardStatus { get; init; }

    public string? ExpireDate { get; init; }

    // Serial real del bono que Ogloba genera en /activation (doc §5, respuesta de ejemplo).
    // Antes se descartaba silenciosamente por no estar declarado aquí: System.Text.Json
    // ignora por defecto las propiedades del JSON que no tienen contraparte en el DTO.
    public string? CardNumber { get; init; }

    // Link del e-gift-card específico de esta transacción (solo bonos virtuales). El nombre
    // JSON real es "eGiftCardUrl" — se fija explícito porque la política de camelCase de
    // System.Text.Json no es fiable para un prefijo de una sola letra mayúscula como "E".
    [JsonPropertyName("eGiftCardUrl")]
    public string? EGiftCardUrl { get; init; }

    // docs/OGLOBA_API_REFERENCE.md §3.2: presente en /redemption desde v2.11 — identifica
    // el producto del bono redimido (informativo, ya conocido vía input).
    public string? Gencode { get; init; }

    // docs/OGLOBA_API_REFERENCE.md §3.2: importe a cobrar al cliente cuando la redención
    // combina saldo + recarga (útil para split payment).
    public long? ToBeChargedAmt { get; init; }

    // docs/OGLOBA_API_REFERENCE.md §3.2: presente cuando la moneda de la tarjeta ≠ la moneda
    // de la transacción. Importante para auditoría cuando KOAJ opere con bonos en moneda
    // distinta a COP (escenario futuro según Roger).
    public OglobaRequestedCurrency? RequestedCurrency { get; init; }

    // docs/OGLOBA_API_REFERENCE.md §3.2: desglose de la redención cuando se combinan
    // saldo propio + recarga. Cada item trae el monto parcial y el origen (saldo/recarga).
    public IReadOnlyList<OglobaRedeemShareDetail>? RedeemShareDet { get; init; }

    public string? GetErrorCode() => OglobaErrorCode.Read(ErrorCode);
}

// docs/OGLOBA_API_REFERENCE.md §3.2 (RequestedCurrency): cuando la moneda de la tarjeta
// difiere de la de la transacción, Ogloba reporta el rate aplicado y los saldos efectivos.
internal sealed class OglobaRequestedCurrency
{
    public string? Currency { get; init; }

    public double? ExchangeRate { get; init; }

    public long? Balance { get; init; }

    public long? PreviousBalance { get; init; }

    public long? InitialBalance { get; init; }
}

// docs/OGLOBA_API_REFERENCE.md §3.2 (RedeemShareDet): cada item es un componente del monto
// total redimido — identifica si vino del saldo base o de una recarga.
internal sealed class OglobaRedeemShareDetail
{
    public long? SharedAmt { get; init; }

    public string? ReloadSource { get; init; }
}

// Ogloba a veces manda errorCode como string y a veces como número (ver §7 del doc de
// referencia) — todas las respuestas comparten esta lectura defensiva.
internal static class OglobaErrorCode
{
    public static string? Read(JsonElement errorCode) => errorCode.ValueKind switch
    {
        JsonValueKind.String => errorCode.GetString(),
        JsonValueKind.Number => errorCode.GetRawText(),
        _ => null
    };
}

// POST /orderCreation (docs/OGLOBA_API_REFERENCE.md §3.9).
internal sealed class OglobaOrderCreationResponse
{
    public bool IsSuccessful { get; init; }

    public JsonElement ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? OrderNo { get; init; }

    public long? OrderAmount { get; init; }

    public string? GetErrorCode() => OglobaErrorCode.Read(ErrorCode);
}

// POST /orderConfirm (docs/OGLOBA_API_REFERENCE.md §3.10).
internal sealed class OglobaOrderConfirmResponse
{
    public bool IsSuccessful { get; init; }

    public JsonElement ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? OrderNo { get; init; }

    public string? OrderStatus { get; init; }

    public List<OglobaOrderedCard>? ListOfCards { get; init; }

    public string? GetErrorCode() => OglobaErrorCode.Read(ErrorCode);
}

internal sealed class OglobaOrderedCard
{
    public string? CardNumber { get; init; }

    public string? ShortCardNumber { get; init; }

    public string? Gencode { get; init; }

    public string? PinCode1 { get; init; }

    public string? PinCode2 { get; init; }

    public string? ExpiryDate { get; init; }

    public long? CardBalance { get; init; }

    // Link al bono emitido. Verificado en vivo (2026-08-24, pedido MA2608000014): Ogloba lo
    // devuelve en /orderStatus aunque /orderConfirm traiga listOfCards vacío. La entrega es
    // siempre por correo; esto sirve de contingencia cuando el correo no llega y como
    // referencia de rastreo para soporte.
    [JsonPropertyName("eGiftCardUrl")]
    public string? EGiftCardUrl { get; init; }
}

// docs/OGLOBA_API_REFERENCE.md §3.16 (getProducts). Solo los campos que el handler de
// aplicación necesita para gatekeeping de UI (allowedActivate/Reload/Redeem + rangos).
internal sealed class OglobaGetProductsResponse
{
    public bool IsSuccessful { get; init; }

    public JsonElement ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<OglobaProductEntry>? ProductList { get; init; }
}

internal sealed class OglobaProductEntry
{
    public string? ItemCode { get; init; }
    public string? ProductName { get; init; }
    public string? Currency { get; init; }
    public int? IsFixValue { get; init; }
    public long? FaceValue { get; init; }
    public long? ActivationMinAmt { get; init; }
    public long? ActivationMaxAmt { get; init; }
    public int? AllowedActivate { get; init; }
    public int? AllowedReload { get; init; }
    public int? AllowedRedeem { get; init; }
    public int? IsRefundable { get; init; }
    public string? ProductType { get; init; }
    public int? CardValidity { get; init; }
}

// docs/OGLOBA_API_REFERENCE.md §3.13 (orderReturn). Devuelve isSuccessful, status (04|06), returnNo.
internal sealed class OglobaOrderReturnResponse
{
    public bool IsSuccessful { get; init; }
    public JsonElement ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ReturnNo { get; init; }
    public string? Status { get; init; }
}

// docs/OGLOBA_API_REFERENCE.md §3.15 (queryTransactionsHistory). El array viene bajo
// `transactionRecord` (singular) en el ejemplo del Postman, pero también aceptamos
// `transactionRecords` (plural) por si Ogloba cambia la convención.
internal sealed class OglobaQueryTransactionsHistoryResponse
{
    public bool IsSuccessful { get; init; }
    public JsonElement ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<OglobaTransactionRecordEntry>? TransactionRecord { get; init; }
    public IReadOnlyList<OglobaTransactionRecordEntry>? TransactionRecords { get; init; }
}

internal sealed class OglobaTransactionRecordEntry
{
    public string? MerchantId { get; init; }
    public string? TerminalId { get; init; }
    public string? CashierId { get; init; }
    public string? TransactionTime { get; init; }
    public string? TransactionType { get; init; }
    public string? TransactionNumber { get; init; }
    public string? ReferenceNumber { get; init; }
    public long? PreviousBalance { get; init; }
    public string? Currency { get; init; }
    public long? TransactionAmount { get; init; }
    public long? Balance { get; init; }
    public string? CardNumber { get; init; }
    public string? Gencode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? ReconciliationResult { get; init; }
    public string? ExpireDate { get; init; }
}
