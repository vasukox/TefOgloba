using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Payments;

public interface IGiftCardProvider
{
    /// <summary>Comprueba la conectividad con Ogloba para la tienda (GET /test).</summary>
    Task<PortResult<Unit>> CheckConnectivityAsync(
        StoreId storeId,
        CancellationToken cancellationToken);

    /// <summary>Obtiene el nombre y saldo del comercio (GET /getBuInfo).</summary>
    Task<PortResult<BusinessUnitInfo>> GetBusinessUnitInfoAsync(
        StoreId storeId,
        CancellationToken cancellationToken);

    /// <summary>Consulta el saldo y estado de una tarjeta (POST /balance).</summary>
    Task<PortResult<CardBalance>> GetBalanceAsync(
        BalanceQuery request,
        CancellationToken cancellationToken);

    Task<PortResult<RedemptionAuthorization>> RedeemAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken);

    Task<PortResult<RedemptionAuthorization>> ActivateAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Recarga saldo a un bono recargable (POST /reload, doc §3.3).</summary>
    Task<PortResult<RedemptionAuthorization>> ReloadAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken);

    Task<PortResult<Unit>> ConfirmAsync(
        TransactionActionRequest request,
        CancellationToken cancellationToken);

    Task<PortResult<Unit>> ReverseAsync(
        ReversalRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Anula una operación ya confirmada y devuelve el saldo al bono (POST /voidTransaction,
    /// doc §3.8). Es lo que sostiene la papelera de HiPOS sobre una línea de pago con bono:
    /// borrar esa línea sin llamar acá dejaría al cliente sin factura y sin saldo.
    /// </summary>
    Task<PortResult<Unit>> VoidAsync(
        VoidTransactionRequest request,
        CancellationToken cancellationToken);

    Task<PortResult<Unit>> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Crea un pedido de bono digital con entrega por correo (POST /orderCreation).
    /// A diferencia de <see cref="ActivateAsync"/>, este es el único flujo con el que Ogloba
    /// envía el bono automáticamente al cliente.
    /// </summary>
    Task<PortResult<GiftCardOrderCreated>> CreateOrderAsync(
        CreateGiftCardOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>Confirma el pedido y obtiene la tarjeta emitida (POST /orderConfirm).</summary>
    Task<PortResult<GiftCardOrderConfirmation>> ConfirmOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>Cancela un pedido aún no confirmado (POST /orderCancel, doc §3.11).</summary>
    Task<PortResult<Unit>> CancelOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consulta el estado de un pedido (POST /orderStatus, doc §3.12) — permite hacer seguimiento
    /// a un pedido que quedó "en proceso" (orderStatus 041/043) tras /orderConfirm.
    /// </summary>
    Task<PortResult<GiftCardOrderConfirmation>> GetOrderStatusAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve un pedido confirmado (total o parcial) — POST /orderReturn, doc §3.13. Algunos
    /// productos no son retornables (`isRefundable=0` en /getProducts), por lo que el handler
    /// debe verificar antes de llamar.
    /// </summary>
    Task<PortResult<OrderReturnResult>> ReturnOrderAsync(
        ReturnGiftCardOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lista el catálogo de productos disponibles para la tienda (POST /getProducts, doc §3.16).
    /// Sirve para validar que la tienda tiene productos digitales habilitados antes de ofrecer
    /// "Activar Virtual" (bug #3 del audit: si K00036 no tiene salesType=MA, el flujo
    /// de Order Management devuelve error 23 contra Ogloba).
    /// </summary>
    Task<PortResult<IReadOnlyList<GiftCardProduct>>> GetProductsAsync(
        StoreId storeId,
        string? itemCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Historial paginado de transacciones (POST /queryTransactionsHistory, doc §3.15).
    /// Filtros opcionales; sin rango de fechas devuelve las últimas N transacciones.
    /// </summary>
    Task<PortResult<IReadOnlyList<OglobaTransactionRecord>>> QueryTransactionsHistoryAsync(
        StoreId storeId,
        string? transDateFrom,
        string? transDateTo,
        int pageNo,
        int numberOfPage,
        CancellationToken cancellationToken);
}

/// <summary>Una transacción devuelta por /queryTransactionsHistory (doc §3.15).</summary>
public sealed record OglobaTransactionRecord(
    string MerchantId,
    string TerminalId,
    string CashierId,
    string TransactionTime,
    string TransactionType,
    string TransactionNumber,
    string ReferenceNumber,
    long? PreviousBalanceMinorUnits,
    string Currency,
    long? TransactionAmountMinorUnits,
    long? BalanceMinorUnits,
    string CardNumber,
    string? Gencode,
    string TransactionStatus,
    string ReconciliationResult,
    string? ExpireDate);

/// <summary>Resultado de /orderReturn (doc §3.13). Status `04` = aprobación de primer nivel, `06` = aprobación de AR.</summary>
public sealed record OrderReturnResult(string OrderNumber, string ReturnNumber, string Status);

/// <summary>Producto del catálogo de Ogloba (POST /getProducts, doc §3.16).</summary>
public sealed record GiftCardProduct(
    string ItemCode,
    string ProductName,
    string Currency,
    bool IsFixValue,
    long? FaceValueMinorUnits,
    long? ActivationMinAmtMinorUnits,
    long? ActivationMaxAmtMinorUnits,
    bool AllowedActivate,
    bool AllowedReload,
    bool AllowedRedeem,
    bool IsRefundable,
    string ProductType,
    long CardValidityMonths);
