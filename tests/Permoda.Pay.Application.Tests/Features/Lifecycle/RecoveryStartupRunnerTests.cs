using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Features.Lifecycle;
using Permoda.Pay.Application.Features.Recovery;
using Permoda.Pay.Application.Tests.TestDoubles;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using CardIdentifier = Permoda.Pay.Domain.GiftCards.CardIdentifier;

namespace Permoda.Pay.Application.Tests.Features.Lifecycle;

public sealed class RecoveryStartupRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_InvokesRecoveryHandlerOnce()
    {
        var calls = new List<string>();
        var provider = new FakePendingPaymentRepository(calls);
        provider.PendingPayments.Add(CreateRequestedTransaction());
        var audit = new FakeTransactionAudit();
        var recoveryHandler = new RecoverPendingPaymentsHandler(
            new FakeGiftCardProvider(calls),
            provider,
            audit,
            new StubClock());
        var runner = new RecoveryStartupRunner(recoveryHandler, audit, new StubClock());

        var first = await runner.RunOnceAsync(CancellationToken.None);
        var second = await runner.RunOnceAsync(CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Total);
        Assert.False(second.IsSuccess);
        Assert.Equal("recovery.already_executed", second.Error.Code);
        Assert.Contains(
            audit.Entries,
            entry => entry.Event == TransactionAuditEvent.RecoveryRequired);
    }

    private static PaymentTransaction CreateRequestedTransaction()
    {
        var transaction = PaymentTransaction.Create(
            TransactionNumber.Create("1756113296").Value,
            StoreId.Create("K00036").Value,
            TerminalId.Create("caja-5").Value,
            CashierId.Create("operador-123").Value,
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value,
            GiftCardOperation.Redemption).Value;
        transaction.RegisterRequest(
            ReferenceNumber.Create("00136544716V").Value);
        transaction.PrepareConfirmation();
        return transaction;
    }
}
