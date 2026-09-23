using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Logging;

public enum TransactionAuditEvent
{
    Created = 1,
    RequestAccepted = 2,
    RequestRejected = 3,
    ReversalPending = 4,
    Reversed = 5,
    ConfirmationPending = 6,
    Confirmed = 7,
    ReconciliationPending = 8,
    Reconciled = 9,
    RecoveryRequired = 10
}

public sealed record TransactionAuditEntry(
    DateTimeOffset TimestampUtc,
    string TransactionNumber,
    string? ReferenceNumber,
    PaymentStatus Status,
    TransactionAuditEvent Event,
    string? ErrorCode);
