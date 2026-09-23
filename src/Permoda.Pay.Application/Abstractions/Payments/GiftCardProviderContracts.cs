using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Payments;

public sealed record RedemptionRequest(
    TransactionNumber TransactionNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    CardIdentifier CardIdentifier,
    Money Amount,
    // docs/OGLOBA_API_REFERENCE.md §3.2: opcionales pero recomendados. pinCode habilita
    // validación segura del bono (3 intentos incorrectos pueden bloquear); track2Data aplica
    // solo cuando el cajero pasa la tarjeta por un lector de banda magnética.
    string? PinCode = null,
    string? Note = null,
    string? Track2Data = null,
    // Correo del beneficiario para ACTIVAR una tarjeta digital en tienda. Ogloba le envía el bono
    // a esa dirección al confirmarse la activación (confirmado por Ogloba el 2026-09-09). No
    // aplica a redención ni a tarjetas físicas.
    string? Email = null);

/// <param name="CardNumber">
/// Serial real que Ogloba genera en /activation. Nulo en /redemption (Ogloba no lo devuelve ahí).
/// </param>
/// <param name="EGiftCardUrl">Link del e-gift-card específico de esta transacción (solo bonos virtuales).</param>
/// <param name="Gencode">Producto del bono redimido (devuelto por /redemption desde v2.11).</param>
/// <param name="ToBeChargedAmt">Importe a cobrar al cliente cuando se combinan saldo + recarga.</param>
/// <param name="RequestedCurrency">Moneda y rate aplicados si difieren de la transacción.</param>
/// <param name="RedeemShareDet">Desglose por origen del monto (saldo vs recarga).</param>
public sealed record RedemptionAuthorization(
    ReferenceNumber ReferenceNumber,
    Money RemainingBalance,
    string? CardNumber = null,
    string? EGiftCardUrl = null,
    string? Gencode = null,
    long? ToBeChargedAmt = null,
    RequestedCurrencyInfo? RequestedCurrency = null,
    IReadOnlyList<RedeemShareDetail>? RedeemShareDet = null);

/// <summary>Información de moneda cruzada cuando difiere de la transacción.</summary>
public sealed record RequestedCurrencyInfo(
    string Currency,
    double ExchangeRate,
    long Balance,
    long PreviousBalance,
    long InitialBalance);

/// <summary>Componente del monto redimido — identifica si vino de saldo o recarga.</summary>
public sealed record RedeemShareDetail(long SharedAmt, string ReloadSource);

// docs/OGLOBA_API_REFERENCE.md §3.7 (Reversal) y §3.8 (VoidTransaction): ambos endpoints
// exigen los identificadores ORIGINALES de la transacción que se reversa/anula. Los campos
// Original* viajan SIEMPRE poblados; Current* son los del POS que emite la reversal/void.
public sealed record TransactionActionRequest(
    TransactionNumber TransactionNumber,
    ReferenceNumber ReferenceNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    Money Amount,
    string OriginalMerchantId,
    string OriginalTerminalId,
    string OriginalCashierId);

public sealed record ReversalRequest(
    TransactionNumber TransactionNumber,
    ReferenceNumber? ReferenceNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    string OriginalMerchantId,
    string OriginalTerminalId,
    string OriginalCashierId,
    string OriginalTransactionNumber);

/// <summary>
/// Anulación de una operación YA CONFIRMADA contra Ogloba: el saldo vuelve al bono.
/// <para>
/// No confundir con <see cref="ReversalRequest"/>. La reversa deshace un Step 1 que quedó en
/// `Requested` porque no supimos su resultado; la anulación revierte algo que sí se completó y
/// que el cajero decidió deshacer. Ogloba las expone como endpoints distintos y con reglas
/// distintas: la anulación puede ser rechazada por plazo vencido (238) o porque la transacción
/// ya se validó en el cierre (109).
/// </para>
/// <para>
/// Los campos <c>Original*</c> identifican la operación que se anula; los otros tres, la caja
/// que está anulando. Casi siempre coinciden, pero no tienen por qué: una anulación puede
/// hacerse desde otra caja de la misma tienda.
/// </para>
/// </summary>
public sealed record VoidTransactionRequest(
    ReferenceNumber ReferenceNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    string OriginalMerchantId,
    string OriginalTerminalId,
    string OriginalCashierId,
    TransactionNumber? TransactionNumber = null,
    string? Note = null,
    string? Reason = null);

public sealed record ReconciliationRequest(
    TransactionNumber TransactionNumber,
    ReferenceNumber ReferenceNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    CardIdentifier CardIdentifier,
    Money Amount,
    GiftCardOperation Operation,
    ReconciliationFinalStatus FinalStatus);

/// <summary>Datos del comercio (Business Unit) devueltos por Ogloba en GET /getBuInfo.</summary>
/// <param name="BalanceMinorUnits">Saldo en centavos (per §3.17, `availableBalance` en Decimal).</param>
public sealed record BusinessUnitInfo(string Name, long? BalanceMinorUnits)
{
    /// <summary>Saldo en pesos para mostrar al cajero.</summary>
    public long? BalancePesos => BalanceMinorUnits.HasValue ? BalanceMinorUnits.Value / 100 : null;
}

/// <summary>Consulta de saldo de una tarjeta (POST /balance).</summary>
/// <param name="TransactionNumber">Único por intento; Ogloba lo usa para idempotencia.</param>
/// <param name="PinCode">PIN del bono (opcional, 3 intentos incorrectos pueden bloquear).</param>
public sealed record BalanceQuery(
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    TransactionNumber TransactionNumber,
    CardIdentifier CardIdentifier,
    string? PinCode = null);

/// <summary>Saldo y estado de una tarjeta devueltos por Ogloba en POST /balance.</summary>
/// <param name="BalanceMinorUnits">
/// Saldo en PESOS tal cual (confirmado en vivo el 2026-07-28 contra sandbox: /balance responde
/// el mismo valor que se activó, sin ×100 — igual que /activation, /redemption y /orderCreation).
/// El nombre "MinorUnits" queda por consistencia con el resto del contrato, no porque Ogloba
/// use centavos aquí.
/// </param>
/// <param name="Status">
/// Uno de los 6 valores documentados en §3.14:
/// GENERATE (recién creada), ALLOCATE (asignada), IN USE (activa y en uso),
/// SUSPEND (suspendida), REFUND (devuelta/finalizada), EXPIRE (expirada).
/// </param>
public sealed record CardBalance(
    long BalanceMinorUnits,
    string Currency,
    string Status,
    string? ExpireDate)
{
    /// <summary>Estados en los que la tarjeta es vendible/redimible (bug C: antes solo aceptaba ACTIVE).</summary>
    public bool IsActive => IsUsable(Status);

    public static bool IsUsable(string? status) => status?.ToUpperInvariant() switch
    {
        "GENERATE" or "ALLOCATE" or "IN USE" or "ACTIVE" => true,
        _ => false
    };
}
