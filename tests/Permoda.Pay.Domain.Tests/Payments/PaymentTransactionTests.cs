using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Domain.Tests.Payments;

public sealed class PaymentTransactionTests
{
    [Fact]
    public void ConfirmationFlow_CompletesThreeRequiredSteps()
    {
        var transaction = CreateTransaction();
        var referenceNumber = CreateReferenceNumber();

        var requestResult = transaction.RegisterRequest(referenceNumber);
        var prepareResult = transaction.PrepareConfirmation();
        var confirmationResult = transaction.RegisterConfirmation();

        Assert.True(requestResult.IsSuccess);
        Assert.True(prepareResult.IsSuccess);
        Assert.True(confirmationResult.IsSuccess);
        Assert.Equal(PaymentStatus.Confirmed, transaction.Status);
        Assert.Equal(ReconciliationStatus.Pending, transaction.ReconciliationStatus);
        Assert.Equal(
            ReconciliationFinalStatus.Successful,
            transaction.GetPendingReconciliationFinalStatus().Value);

        var reconciliationResult = transaction.RegisterReconciliation();

        Assert.True(reconciliationResult.IsSuccess);
        Assert.Equal(ReconciliationStatus.Completed, transaction.ReconciliationStatus);
    }

    [Fact]
    public void Step1Timeout_AllowsOnlyTechnicalReversalBeforeRequestIsRegistered()
    {
        var transaction = CreateTransaction();

        var prepareResult = transaction.PrepareReversal();
        var reversalResult = transaction.RegisterReversal();

        Assert.True(prepareResult.IsSuccess);
        Assert.True(reversalResult.IsSuccess);
        Assert.Equal(PaymentStatus.Reversed, transaction.Status);
        Assert.Null(transaction.ReferenceNumber);
        Assert.Equal(ReconciliationStatus.NotRequired, transaction.ReconciliationStatus);
    }

    [Fact]
    public void ConfirmedTransaction_CannotBeReversed()
    {
        var transaction = CreateConfirmedTransaction();

        var result = transaction.PrepareReversal();

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorType.Conflict, result.Error.Type);
        Assert.Equal(PaymentStatus.Confirmed, transaction.Status);
    }

    [Fact]
    public void DefinitiveStep2Failure_RemainsReconcilableWithFailedFinalStatus()
    {
        var transaction = CreateTransaction();
        transaction.RegisterRequest(CreateReferenceNumber());
        transaction.PrepareConfirmation();

        var failureResult = transaction.RegisterStep2Failure();
        var finalStatusResult = transaction.GetPendingReconciliationFinalStatus();
        var reconciliationResult = transaction.RegisterReconciliation();

        Assert.True(failureResult.IsSuccess);
        Assert.True(finalStatusResult.IsSuccess);
        Assert.Equal(ReconciliationFinalStatus.Failed, finalStatusResult.Value);
        Assert.True(reconciliationResult.IsSuccess);
        Assert.Equal(PaymentStatus.Step2Failed, transaction.Status);
        Assert.Equal(ReconciliationStatus.Completed, transaction.ReconciliationStatus);
    }

    [Fact]
    public void RegisterRequest_IsIdempotentOnlyForSameReferenceNumber()
    {
        var transaction = CreateTransaction();
        var referenceNumber = CreateReferenceNumber();

        var firstResult = transaction.RegisterRequest(referenceNumber);
        var repeatedResult = transaction.RegisterRequest(referenceNumber);
        var conflictingResult = transaction.RegisterRequest(
            ReferenceNumber.Create("00136544717X").Value);

        Assert.True(firstResult.IsSuccess);
        Assert.True(repeatedResult.IsSuccess);
        Assert.True(conflictingResult.IsFailure);
        Assert.Equal("payment_transaction.reference_conflict", conflictingResult.Error.Code);
        Assert.Equal(referenceNumber, transaction.ReferenceNumber);
    }

    [Fact]
    public void Confirmation_MustBePreparedBeforeItIsRegistered()
    {
        var transaction = CreateTransaction();
        transaction.RegisterRequest(CreateReferenceNumber());

        var result = transaction.RegisterConfirmation();

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentStatus.Requested, transaction.Status);
    }

    [Theory]
    [InlineData(GiftCardOperation.Activation)]
    [InlineData(GiftCardOperation.Reload)]
    public void Create_RejectsNegativeAmountForActivationAndReload(GiftCardOperation operation)
    {
        var result = CreateTransactionResult(operation, -50_000);

        Assert.True(result.IsFailure);
        Assert.Equal("payment_transaction.invalid_amount", result.Error.Code);
    }

    [Fact]
    public void Create_AllowsNegativeRedemptionForNegativeSale()
    {
        var result = CreateTransactionResult(GiftCardOperation.Redemption, -50_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(-50_000, result.Value.Amount.MinorUnits);
    }

    [Fact]
    public void Create_RejectsZeroTransactionAmount()
    {
        var result = CreateTransactionResult(GiftCardOperation.Redemption, 0);

        Assert.True(result.IsFailure);
        Assert.Equal("payment_transaction.invalid_amount", result.Error.Code);
    }

    [Fact]
    public void StateRoundTrip_PreservesRecoverableConfirmationState()
    {
        var transaction = CreateTransaction();
        transaction.RegisterRequest(CreateReferenceNumber());
        transaction.PrepareConfirmation();

        var result = PaymentTransaction.Restore(transaction.ToState());

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.ConfirmationPending, result.Value.Status);
        Assert.Equal(ReconciliationStatus.NotRequired, result.Value.ReconciliationStatus);
        Assert.Equal(transaction.TransactionNumber, result.Value.TransactionNumber);
        Assert.Equal(transaction.ReferenceNumber, result.Value.ReferenceNumber);
    }

    [Fact]
    public void Restore_RejectsConfirmedStateWithoutReferenceNumber()
    {
        var transaction = CreateTransaction();
        var state = transaction.ToState() with
        {
            Status = PaymentStatus.Confirmed,
            ReconciliationStatus = ReconciliationStatus.Pending
        };

        var result = PaymentTransaction.Restore(state);

        Assert.True(result.IsFailure);
        Assert.Equal("payment_transaction.invalid_persisted_state", result.Error.Code);
    }

    [Fact]
    public void RejectedStep1_CannotAdvanceToStep2()
    {
        var transaction = CreateTransaction();

        var rejectionResult = transaction.RegisterRequestRejection();
        var confirmationResult = transaction.PrepareConfirmation();

        Assert.True(rejectionResult.IsSuccess);
        Assert.True(confirmationResult.IsFailure);
        Assert.Equal(PaymentStatus.RequestRejected, transaction.Status);
    }

    private static PaymentTransaction CreateConfirmedTransaction()
    {
        var transaction = CreateTransaction();
        transaction.RegisterRequest(CreateReferenceNumber());
        transaction.PrepareConfirmation();
        transaction.RegisterConfirmation();
        return transaction;
    }

    private static PaymentTransaction CreateTransaction() =>
        CreateTransactionResult(GiftCardOperation.Redemption, 50_000).Value;

    private static Result<PaymentTransaction> CreateTransactionResult(
        GiftCardOperation operation,
        long minorUnits)
    {
        return PaymentTransaction.Create(
            TransactionNumber.Create("1756113296").Value,
            StoreId.Create("K00036").Value,
            TerminalId.Create("caja-5").Value,
            CashierId.Create("operador-123").Value,
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(minorUnits).Value,
            operation);
    }

    private static ReferenceNumber CreateReferenceNumber() =>
        ReferenceNumber.Create("00136544716V").Value;
}
