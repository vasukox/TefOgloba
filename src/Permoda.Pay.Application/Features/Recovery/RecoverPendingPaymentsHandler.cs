using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Application.Features;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Features.Recovery;

public sealed class RecoverPendingPaymentsHandler
{
    private readonly IGiftCardProvider _provider;
    private readonly IPendingPaymentRepository _pendingPayments;
    private readonly ITransactionAudit _audit;
    private readonly IClock _clock;

    public RecoverPendingPaymentsHandler(
        IGiftCardProvider provider,
        IPendingPaymentRepository pendingPayments,
        ITransactionAudit audit,
        IClock clock)
    {
        _provider = provider;
        _pendingPayments = pendingPayments;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RecoverPendingPaymentsResult> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var loadResult = await _pendingPayments.LoadPendingAsync(cancellationToken);

        if (loadResult.IsFailure)
        {
            return RecoverPendingPaymentsResult.LoadFailed(
                ApplicationError.Technical(
                    loadResult.Failure.Code,
                    loadResult.Failure.Description));
        }

        var failures = new List<PaymentRecoveryFailure>();
        var recovered = 0;

        foreach (var transaction in loadResult.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failure = await RecoverAsync(transaction, cancellationToken);

            if (failure is null)
            {
                recovered++;
            }
            else
            {
                failures.Add(failure);
            }
        }

        return RecoverPendingPaymentsResult.Completed(
            loadResult.Value.Count,
            recovered,
            failures);
    }

    private Task<PaymentRecoveryFailure?> RecoverAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.ReconciliationStatus == ReconciliationStatus.Completed)
        {
            return DeleteCompletedAsync(transaction, cancellationToken);
        }

        return transaction.Status switch
        {
            PaymentStatus.Requested or PaymentStatus.ConfirmationPending =>
                RecoverConfirmationAsync(transaction, cancellationToken),
            PaymentStatus.ReversalPending =>
                RecoverReversalAsync(transaction, cancellationToken),
            PaymentStatus.Confirmed or
            PaymentStatus.Step2Failed when
                transaction.ReconciliationStatus == ReconciliationStatus.Pending =>
                    RecoverReconciliationAsync(transaction, cancellationToken),
            PaymentStatus.Reversed or PaymentStatus.RequestRejected =>
                DeleteCompletedAsync(transaction, cancellationToken),
            _ => Task.FromResult<PaymentRecoveryFailure?>(new PaymentRecoveryFailure(
                transaction.TransactionNumber.Value,
                transaction.Status,
                "recovery.unsupported_state",
                "The pending transaction state cannot be recovered automatically."))
        };
    }

    private async Task<PaymentRecoveryFailure?> RecoverConfirmationAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.Status == PaymentStatus.Requested)
        {
            var preparation = transaction.PrepareConfirmation();

            if (preparation.IsFailure)
            {
                return FromDomainError(transaction, preparation.Error);
            }

            var saveFailure = await SaveAsync(transaction, cancellationToken);

            if (saveFailure is not null)
            {
                return saveFailure;
            }
        }

        await AuditAsync(
            transaction,
            TransactionAuditEvent.ConfirmationPending,
            null,
            cancellationToken);

        var confirmation = await _provider.ConfirmAsync(
            GiftCardRequestFactory.CreateTransactionAction(transaction),
            cancellationToken);

        if (confirmation.IsFailure)
        {
            if (confirmation.Failure.Type != PortFailureType.Rejected)
            {
                await SaveAsync(transaction, cancellationToken);
                await AuditAsync(
                    transaction,
                    TransactionAuditEvent.RecoveryRequired,
                    confirmation.Failure.Code,
                    cancellationToken);
                return FromPortFailure(transaction, confirmation.Failure);
            }

            var step2Failure = transaction.RegisterStep2Failure();

            if (step2Failure.IsFailure)
            {
                return FromDomainError(transaction, step2Failure.Error);
            }

            var saveFailure = await SaveAsync(transaction, cancellationToken);

            if (saveFailure is not null)
            {
                return saveFailure;
            }

            return await RecoverReconciliationAsync(transaction, cancellationToken);
        }

        var registration = transaction.RegisterConfirmation();

        if (registration.IsFailure)
        {
            return FromDomainError(transaction, registration.Error);
        }

        await AuditAsync(transaction, TransactionAuditEvent.Confirmed, null, cancellationToken);

        var persistenceFailure = await SaveAsync(transaction, cancellationToken);

        return persistenceFailure ??
            await RecoverReconciliationAsync(transaction, cancellationToken);
    }

    private async Task<PaymentRecoveryFailure?> RecoverReversalAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AuditAsync(
            transaction,
            TransactionAuditEvent.ReversalPending,
            null,
            cancellationToken);

        var reversal = await _provider.ReverseAsync(
            GiftCardRequestFactory.CreateReversal(transaction),
            cancellationToken);

        if (reversal.IsFailure)
        {
            await SaveAsync(transaction, cancellationToken);
            return FromPortFailure(transaction, reversal.Failure);
        }

        var registration = transaction.RegisterReversal();

        if (registration.IsFailure)
        {
            return FromDomainError(transaction, registration.Error);
        }

        await AuditAsync(transaction, TransactionAuditEvent.Reversed, null, cancellationToken);

        var persistenceFailure = await SaveAsync(transaction, cancellationToken);
        return persistenceFailure ?? await DeleteAsync(transaction, cancellationToken);
    }

    private async Task<PaymentRecoveryFailure?> RecoverReconciliationAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var finalStatus = transaction.GetPendingReconciliationFinalStatus();

        if (finalStatus.IsFailure)
        {
            return FromDomainError(transaction, finalStatus.Error);
        }

        await AuditAsync(
            transaction,
            TransactionAuditEvent.ReconciliationPending,
            null,
            cancellationToken);

        var reconciliation = await _provider.ReconcileAsync(
            GiftCardRequestFactory.CreateReconciliation(transaction, finalStatus.Value),
            cancellationToken);

        if (reconciliation.IsFailure)
        {
            await SaveAsync(transaction, cancellationToken);
            return FromPortFailure(transaction, reconciliation.Failure);
        }

        var registration = transaction.RegisterReconciliation();

        if (registration.IsFailure)
        {
            return FromDomainError(transaction, registration.Error);
        }

        await AuditAsync(transaction, TransactionAuditEvent.Reconciled, null, cancellationToken);

        var persistenceFailure = await SaveAsync(transaction, cancellationToken);
        return persistenceFailure ?? await DeleteAsync(transaction, cancellationToken);
    }

    private Task<PaymentRecoveryFailure?> DeleteCompletedAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken) =>
        DeleteAsync(transaction, cancellationToken);

    private async Task<PaymentRecoveryFailure?> SaveAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = await _pendingPayments.SaveAsync(transaction, cancellationToken);
        return result.IsSuccess ? null : FromPortFailure(transaction, result.Failure);
    }

    private async Task<PaymentRecoveryFailure?> DeleteAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = await _pendingPayments.DeleteAsync(
            transaction.TransactionNumber,
            cancellationToken);
        return result.IsSuccess ? null : FromPortFailure(transaction, result.Failure);
    }

    private async Task AuditAsync(
        PaymentTransaction transaction,
        TransactionAuditEvent auditEvent,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(
            new TransactionAuditEntry(
                _clock.UtcNow,
                transaction.TransactionNumber.Value,
                transaction.ReferenceNumber?.Value,
                transaction.Status,
                auditEvent,
                errorCode),
            cancellationToken);
    }

    private static PaymentRecoveryFailure FromPortFailure(
        PaymentTransaction transaction,
        PortFailure failure) =>
        new(
            transaction.TransactionNumber.Value,
            transaction.Status,
            failure.Code,
            failure.Description);

    private static PaymentRecoveryFailure FromDomainError(
        PaymentTransaction transaction,
        DomainError error) =>
        new(
            transaction.TransactionNumber.Value,
            transaction.Status,
            error.Code,
            error.Description);
}
