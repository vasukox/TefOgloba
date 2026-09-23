using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Features.Recovery;
using Permoda.Pay.Application.Tests.TestDoubles;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Tests.Features.Recovery;

public sealed class RecoverPendingPaymentsHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithRequestedTransaction_ConfirmsReconcilesAndDeletes()
    {
        var scenario = new RecoveryScenario();
        scenario.Repository.PendingPayments.Add(CreateRequestedTransaction());

        var result = await scenario.Handler.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Recovered);
        Assert.Equal(0, result.Remaining);
        Assert.Equal(
            [
                "repository.load",
                "repository.save.ConfirmationPending",
                "provider.confirm",
                "repository.save.Confirmed",
                "provider.reconcile",
                "repository.save.Confirmed",
                "repository.delete"
            ],
            scenario.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithConfirmationTimeout_LeavesTransactionPending()
    {
        var scenario = new RecoveryScenario();
        var transaction = CreateRequestedTransaction();
        transaction.PrepareConfirmation();
        scenario.Repository.PendingPayments.Add(transaction);
        scenario.Provider.ConfirmationResults.Enqueue(PortResult<Unit>.Failed(new PortFailure(
            "provider.confirm_timeout",
            "Confirmation timed out.",
            PortFailureType.Timeout)));

        var result = await scenario.Handler.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Recovered);
        Assert.Equal(1, result.Remaining);
        Assert.Equal("provider.confirm_timeout", Assert.Single(result.Failures).Code);
        Assert.Contains(PaymentStatus.ConfirmationPending, scenario.Repository.SavedStatuses);
        Assert.Empty(scenario.Repository.DeletedTransactionNumbers);
    }

    [Fact]
    public async Task HandleAsync_WithReversalPending_ReversesUsingOriginalNumberWithoutReconciliation()
    {
        var scenario = new RecoveryScenario();
        var transaction = CreateTransaction();
        transaction.PrepareReversal();
        scenario.Repository.PendingPayments.Add(transaction);

        var result = await scenario.Handler.HandleAsync();

        Assert.Equal(1, result.Recovered);
        Assert.Equal(
            transaction.TransactionNumber,
            scenario.Provider.ReversalRequests.Single().TransactionNumber);
        Assert.Empty(scenario.Provider.ReconciliationRequests);
        Assert.Contains(transaction.TransactionNumber.Value, scenario.Repository.DeletedTransactionNumbers);
    }

    [Fact]
    public async Task HandleAsync_WithConfirmedTransaction_RetriesOnlyReconciliation()
    {
        var scenario = new RecoveryScenario();
        var transaction = CreateConfirmedTransaction();
        scenario.Repository.PendingPayments.Add(transaction);

        var result = await scenario.Handler.HandleAsync();

        Assert.Equal(1, result.Recovered);
        Assert.Empty(scenario.Provider.ConfirmationRequests);
        Assert.Single(scenario.Provider.ReconciliationRequests);
        Assert.Contains(transaction.TransactionNumber.Value, scenario.Repository.DeletedTransactionNumbers);
    }

    [Fact]
    public async Task HandleAsync_WhenRepositoryCannotLoad_ReturnsTechnicalFailure()
    {
        var scenario = new RecoveryScenario();
        scenario.Repository.LoadFailure = new PortFailure(
            "persistence.database_unavailable",
            "Database unavailable.",
            PortFailureType.Technical);

        var result = await scenario.Handler.HandleAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("persistence.database_unavailable", result.Error.Code);
        Assert.Equal(0, result.Total);
        Assert.Empty(scenario.Provider.ConfirmationRequests);
    }

    private static PaymentTransaction CreateConfirmedTransaction()
    {
        var transaction = CreateRequestedTransaction();
        transaction.PrepareConfirmation();
        transaction.RegisterConfirmation();
        return transaction;
    }

    private static PaymentTransaction CreateRequestedTransaction()
    {
        var transaction = CreateTransaction();
        transaction.RegisterRequest(ReferenceNumber.Create("00136544716V").Value);
        return transaction;
    }

    private static PaymentTransaction CreateTransaction() =>
        PaymentTransaction.Create(
            TransactionNumber.Create("1756113296").Value,
            StoreId.Create("K00036").Value,
            TerminalId.Create("caja-5").Value,
            CashierId.Create("operador-123").Value,
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value,
            GiftCardOperation.Redemption).Value;

    private sealed class RecoveryScenario
    {
        public RecoveryScenario()
        {
            Provider = new FakeGiftCardProvider(Calls);
            Repository = new FakePendingPaymentRepository(Calls);
            Handler = new RecoverPendingPaymentsHandler(
                Provider,
                Repository,
                new FakeTransactionAudit(),
                new StubClock());
        }

        public List<string> Calls { get; } = [];

        public FakeGiftCardProvider Provider { get; }

        public FakePendingPaymentRepository Repository { get; }

        public RecoverPendingPaymentsHandler Handler { get; }
    }
}
