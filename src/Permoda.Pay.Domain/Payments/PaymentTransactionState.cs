using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Domain.Payments;

public sealed record PaymentTransactionState(
    TransactionNumber TransactionNumber,
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    CardIdentifier CardIdentifier,
    Money Amount,
    GiftCardOperation Operation,
    ReferenceNumber? ReferenceNumber,
    PaymentStatus Status,
    ReconciliationStatus ReconciliationStatus);
