using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Features;

internal static class GiftCardRequestFactory
{
    /// <param name="email">
    /// Correo del beneficiario. Solo se manda al ACTIVAR una tarjeta digital: es la dirección a
    /// la que Ogloba envía el bono al confirmarse la activación. En redención y en tarjetas
    /// físicas va nulo.
    /// </param>
    public static RedemptionRequest CreateRedemption(
        PaymentTransaction transaction,
        string? note = null,
        string? email = null) =>
        new(
            transaction.TransactionNumber,
            transaction.StoreId,
            transaction.TerminalId,
            transaction.CashierId,
            transaction.CardIdentifier,
            transaction.Amount,
            PinCode: null,
            Note: note,
            Track2Data: null,
            Email: email);

    public static TransactionActionRequest CreateTransactionAction(PaymentTransaction transaction) =>
        new(
            transaction.TransactionNumber,
            transaction.ReferenceNumber ?? throw new InvalidOperationException("Reference number is required."),
            transaction.StoreId,
            transaction.TerminalId,
            transaction.CashierId,
            transaction.Amount,
            // docs/OGLOBA_API_REFERENCE.md §3.7-3.8: original* identifican la transacción
            // ORIGINAL. En nuestro modelo el POS que emite void/reversal es el mismo que
            // emitió la transacción original (misma tienda, misma caja, mismo cajero), así
            // que copiamos los mismos identificadores del agregado PaymentTransaction.
            transaction.StoreId.Value,
            transaction.TerminalId.Value,
            transaction.CashierId.Value);

    public static ReversalRequest CreateReversal(PaymentTransaction transaction) =>
        new(
            transaction.TransactionNumber,
            transaction.ReferenceNumber,
            transaction.StoreId,
            transaction.TerminalId,
            transaction.CashierId,
            transaction.StoreId.Value,
            transaction.TerminalId.Value,
            transaction.CashierId.Value,
            // originalTransNumber = el transactionNumber que se envió en el Step 1 de la
            // transacción original. Lo conocemos porque vive en el agregado.
            transaction.TransactionNumber.Value);

    public static ReconciliationRequest CreateReconciliation(
        PaymentTransaction transaction,
        ReconciliationFinalStatus finalStatus) =>
        new(
            transaction.TransactionNumber,
            transaction.ReferenceNumber ?? throw new InvalidOperationException("Reference number is required."),
            transaction.StoreId,
            transaction.TerminalId,
            transaction.CashierId,
            transaction.CardIdentifier,
            transaction.Amount,
            transaction.Operation,
            finalStatus);
}
