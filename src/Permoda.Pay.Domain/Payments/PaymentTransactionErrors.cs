using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public static class PaymentTransactionErrors
{
    public static DomainError Required(string fieldName) => DomainError.Validation(
        $"payment_transaction.{fieldName}_required",
        $"The {fieldName} is required.");

    public static DomainError InvalidOperation => DomainError.Validation(
        "payment_transaction.invalid_operation",
        "The gift card operation is invalid.");

    public static DomainError InvalidAmount => DomainError.Validation(
        "payment_transaction.invalid_amount",
        "The transaction amount cannot be zero, and activation or reload amounts must be positive.");

    public static DomainError InvalidTransition(PaymentStatus currentStatus, string action) => DomainError.Conflict(
        "payment_transaction.invalid_transition",
        $"Cannot {action} a transaction in status {currentStatus}.");

    public static DomainError ReferenceConflict => DomainError.Conflict(
        "payment_transaction.reference_conflict",
        "The transaction already has a different reference number.");

    public static DomainError ReconciliationNotPending => DomainError.Conflict(
        "payment_transaction.reconciliation_not_pending",
        "The transaction does not require reconciliation.");

    public static DomainError InvalidPersistedState => DomainError.Validation(
        "payment_transaction.invalid_persisted_state",
        "The persisted transaction state is inconsistent.");
}
