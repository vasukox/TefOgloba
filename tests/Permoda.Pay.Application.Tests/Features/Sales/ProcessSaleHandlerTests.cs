using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Application.Tests.TestDoubles;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Tests.Features.Sales;

public sealed class ProcessSaleHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidSale_ExecutesStrictThreeStepFlow()
    {
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Approved, result.Outcome);
        Assert.False(result.RequiresRecovery);
        Assert.Equal("1756113296", result.TransactionNumber);
        Assert.Equal("00136544716V", result.ReferenceNumber);
        Assert.Equal("113817******5937", result.MaskedCardNumber);
        Assert.Equal(0, result.RemainingBalanceMinorUnits);
        Assert.Equal(
            [
                "provider.balance",
                "provider.redeem",
                "repository.save.Requested",
                "provider.confirm",
                "repository.save.Confirmed",
                "provider.reconcile",
                "repository.save.Confirmed",
                "repository.delete"
            ],
            scenario.Calls);
        Assert.Equal(
            ReconciliationFinalStatus.Successful,
            scenario.Provider.ReconciliationRequests.Single().FinalStatus);
        Assert.Contains(
            scenario.Audit.Entries,
            entry => entry.Event == TransactionAuditEvent.Reconciled);
    }

    [Fact]
    public async Task HandleAsync_WithConfirmationTimeout_KeepsPendingStateForRecovery()
    {
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());
        scenario.Provider.ConfirmationResults.Enqueue(FailedUnit(
            "provider.confirm_timeout",
            PortFailureType.Timeout));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Unknown, result.Outcome);
        Assert.True(result.RequiresRecovery);
        Assert.Equal("00136544716V", result.ReferenceNumber);
        Assert.Equal(
            [PaymentStatus.Requested, PaymentStatus.ConfirmationPending],
            scenario.Repository.SavedStatuses);
        Assert.Empty(scenario.Provider.ReconciliationRequests);
        Assert.Contains(
            scenario.Audit.Entries,
            entry => entry.Event == TransactionAuditEvent.RecoveryRequired);
    }

    [Fact]
    public async Task HandleAsync_WithStep1Timeout_ReversesAndRetriesWithNewTransactionNumber()
    {
        var scenario = new SaleScenario("1756113296", "1756113297");
        scenario.Provider.RedemptionResults.Enqueue(FailedRedemption(
            "provider.redemption_timeout",
            PortFailureType.Timeout));
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Approved, result.Outcome);
        Assert.Equal("1756113297", result.TransactionNumber);
        Assert.Equal(2, scenario.Provider.RedemptionRequests.Count);
        Assert.Equal(
            "1756113296",
            scenario.Provider.ReversalRequests.Single().TransactionNumber.Value);
        Assert.Equal(
            [
                "provider.balance",
                "provider.redeem",
                "repository.save.ReversalPending",
                "provider.reverse",
                "repository.save.Reversed",
                "repository.delete",
                "provider.balance",
                "provider.redeem",
                "repository.save.Requested",
                "provider.confirm",
                "repository.save.Confirmed",
                "provider.reconcile",
                "repository.save.Confirmed",
                "repository.delete"
            ],
            scenario.Calls);
    }

    [Fact]
    public async Task HandleAsync_WhenReversalFails_ReturnsUnknownWithoutRetryingStep1()
    {
        var scenario = new SaleScenario("1756113296", "1756113297");
        scenario.Provider.RedemptionResults.Enqueue(FailedRedemption(
            "provider.redemption_timeout",
            PortFailureType.Timeout));
        scenario.Provider.ReversalResults.Enqueue(FailedUnit(
            "provider.reversal_unknown",
            PortFailureType.Indeterminate));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Unknown, result.Outcome);
        Assert.True(result.RequiresRecovery);
        Assert.Single(scenario.Provider.RedemptionRequests);
        Assert.Equal(
            [PaymentStatus.ReversalPending, PaymentStatus.ReversalPending],
            scenario.Repository.SavedStatuses);
        Assert.DoesNotContain("provider.confirm", scenario.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithDefinitiveStep2Rejection_ReconcilesWithFailedStatus()
    {
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());
        scenario.Provider.ConfirmationResults.Enqueue(FailedUnit(
            "provider.confirm_rejected",
            PortFailureType.Rejected));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Declined, result.Outcome);
        Assert.False(result.RequiresRecovery);
        Assert.Equal(ApplicationErrorType.Declined, result.Error.Type);
        Assert.Equal(
            ReconciliationFinalStatus.Failed,
            scenario.Provider.ReconciliationRequests.Single().FinalStatus);
        Assert.Contains(PaymentStatus.Step2Failed, scenario.Repository.SavedStatuses);
    }

    [Fact]
    public async Task HandleAsync_WhenReconciliationFails_ApprovesPaymentAndKeepsRecoveryPending()
    {
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());
        scenario.Provider.ReconciliationResults.Enqueue(FailedUnit(
            "provider.reconciliation_timeout",
            PortFailureType.Timeout));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Approved, result.Outcome);
        Assert.True(result.RequiresRecovery);
        Assert.Empty(scenario.Repository.DeletedTransactionNumbers);
        Assert.Equal(
            [PaymentStatus.Requested, PaymentStatus.Confirmed, PaymentStatus.Confirmed],
            scenario.Repository.SavedStatuses);
    }

    [Fact]
    public async Task HandleAsync_WhenCriticalPersistenceFails_ReversesInsteadOfConfirming()
    {
        // Decisión de producto (KOAJ no maneja cancelaciones de bonos): si la persistencia
        // local falla después de que Step 1 autorizó, llamamos /reversal — NUNCA
        // /cancelTransaction. El saldo pre-autorizado se libera y la transacción queda
        // borrada de la BD local. Si el reversal también falla, queda como
        // `Unknown` con `RequiresRecovery=true` para que RecoveryStartupRunner reintente.
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(ApprovedRedemption());
        scenario.Repository.SaveResults.Enqueue(PortResult<Unit>.Failed(new PortFailure(
            "persistence.write_failed",
            "The pending transaction could not be saved.",
            PortFailureType.Technical)));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Unknown, result.Outcome);
        Assert.True(result.RequiresRecovery);
        Assert.Equal(ApplicationErrorType.Technical, result.Error.Type);
        Assert.Equal("persistence.write_failed", result.Error.Code);
        Assert.Empty(scenario.Provider.ConfirmationRequests);
        Assert.Single(scenario.Provider.ReversalRequests);
        Assert.Contains(PaymentStatus.Reversed, scenario.Repository.SavedStatuses);
    }

    [Fact]
    public async Task HandleAsync_WithBusinessRejection_DeclinesWithoutStep2()
    {
        var scenario = new SaleScenario("1756113296");
        scenario.Provider.RedemptionResults.Enqueue(FailedRedemption(
            "53",
            PortFailureType.Rejected));

        var result = await scenario.Handler.HandleAsync(ValidCommand());

        Assert.Equal(ProcessSaleOutcome.Declined, result.Outcome);
        Assert.Equal("53", result.Error.Code);
        Assert.Equal(ApplicationErrorType.Declined, result.Error.Type);
        Assert.Empty(scenario.Repository.SavedStatuses);
        Assert.Empty(scenario.Provider.ConfirmationRequests);
        Assert.Empty(scenario.Provider.ReversalRequests);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidAmount_StopsBeforeGeneratingExternalCalls()
    {
        var scenario = new SaleScenario("1756113296");
        var command = ValidCommand() with { AmountUnits = 0 };

        var result = await scenario.Handler.HandleAsync(command);

        Assert.Equal(ProcessSaleOutcome.Declined, result.Outcome);
        Assert.Equal(ApplicationErrorType.Validation, result.Error.Type);
        Assert.Equal("payment_transaction.invalid_amount", result.Error.Code);
        Assert.Empty(scenario.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithCancelledToken_StopsBeforeProviderCall()
    {
        var scenario = new SaleScenario("1756113296");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scenario.Handler.HandleAsync(ValidCommand(), cancellation.Token));

        Assert.Empty(scenario.Calls);
    }

    private static ProcessSaleCommand ValidCommand() =>
        new(
            "K00036",
            "caja-5",
            "operador-123",
            "1138170025515937",
            50_000,
            "COP");

    private static PortResult<RedemptionAuthorization> ApprovedRedemption() =>
        PortResult<RedemptionAuthorization>.Success(new RedemptionAuthorization(
            ReferenceNumber.Create("00136544716V").Value,
            Money.Create(0).Value));

    private static PortResult<RedemptionAuthorization> FailedRedemption(
        string code,
        PortFailureType type) =>
        PortResult<RedemptionAuthorization>.Failed(new PortFailure(
            code,
            "Provider operation failed.",
            type));

    private static PortResult<Unit> FailedUnit(string code, PortFailureType type) =>
        PortResult<Unit>.Failed(new PortFailure(
            code,
            "Provider operation failed.",
            type));

    private sealed class SaleScenario
    {
        public SaleScenario(params string[] transactionNumbers)
        {
            Provider = new FakeGiftCardProvider(Calls);
            Repository = new FakePendingPaymentRepository(Calls);
            Audit = new FakeTransactionAudit();
            Handler = new ProcessSaleHandler(
                Provider,
                Repository,
                new StubTransactionNumberGenerator(transactionNumbers),
                Audit,
                new StubClock());
        }

        public List<string> Calls { get; } = [];

        public FakeGiftCardProvider Provider { get; }

        public FakePendingPaymentRepository Repository { get; }

        public FakeTransactionAudit Audit { get; }

        public ProcessSaleHandler Handler { get; }
    }
}
