using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Tests.TestDoubles;

internal sealed class FakeGiftCardProvider : IGiftCardProvider
{
    private readonly IList<string> _calls;

    public FakeGiftCardProvider(IList<string> calls)
    {
        _calls = calls;
    }

    public Queue<PortResult<RedemptionAuthorization>> RedemptionResults { get; } = new();

    public Queue<PortResult<RedemptionAuthorization>> ActivationResults { get; } = new();

    public Queue<PortResult<RedemptionAuthorization>> ReloadResults { get; } = new();

    public Queue<PortResult<Unit>> ConfirmationResults { get; } = new();

    public Queue<PortResult<Unit>> ReversalResults { get; } = new();

    public Queue<PortResult<Unit>> VoidResults { get; } = new();

    public Queue<PortResult<Unit>> ReconciliationResults { get; } = new();

    public List<RedemptionRequest> RedemptionRequests { get; } = [];

    public List<RedemptionRequest> ActivationRequests { get; } = [];

    public List<TransactionActionRequest> ConfirmationRequests { get; } = [];

    public List<ReversalRequest> ReversalRequests { get; } = [];

    public List<VoidTransactionRequest> VoidRequests { get; } = [];

    public List<ReconciliationRequest> ReconciliationRequests { get; } = [];

    public Task<PortResult<Unit>> CheckConnectivityAsync(
        Permoda.Pay.Domain.Payments.StoreId storeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.test");
        return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
    }

    public Task<PortResult<BusinessUnitInfo>> GetBusinessUnitInfoAsync(
        Permoda.Pay.Domain.Payments.StoreId storeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.getBuInfo");
        return Task.FromResult(PortResult<BusinessUnitInfo>.Success(
            new BusinessUnitInfo("Tienda de prueba", 0)));
    }

    public Queue<PortResult<CardBalance>> BalanceResults { get; } = new();

    public Task<PortResult<CardBalance>> GetBalanceAsync(
        BalanceQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.balance");
        return Task.FromResult(BalanceResults.Count == 0
            ? PortResult<CardBalance>.Success(new CardBalance(50_000, "COP", "ACTIVE", "20270101"))
            : BalanceResults.Dequeue());
    }

    public Task<PortResult<RedemptionAuthorization>> RedeemAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.redeem");
        RedemptionRequests.Add(request);

        if (RedemptionResults.Count == 0)
        {
            throw new InvalidOperationException("A redemption result must be configured.");
        }

        return Task.FromResult(RedemptionResults.Dequeue());
    }

    public Task<PortResult<RedemptionAuthorization>> ActivateAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.activate");
        ActivationRequests.Add(request);

        if (ActivationResults.Count == 0)
        {
            throw new InvalidOperationException("An activation result must be configured.");
        }

        return Task.FromResult(ActivationResults.Dequeue());
    }

    public List<RedemptionRequest> ReloadRequests { get; } = [];

    public Task<PortResult<RedemptionAuthorization>> ReloadAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.reload");
        ReloadRequests.Add(request);

        if (ReloadResults.Count == 0)
        {
            throw new InvalidOperationException("A reload result must be configured.");
        }

        return Task.FromResult(ReloadResults.Dequeue());
    }

    public Task<PortResult<Unit>> ConfirmAsync(
        TransactionActionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.confirm");
        ConfirmationRequests.Add(request);
        return Task.FromResult(NextOrSuccess(ConfirmationResults));
    }

    public Task<PortResult<Unit>> ReverseAsync(
        ReversalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.reverse");
        ReversalRequests.Add(request);
        return Task.FromResult(NextOrSuccess(ReversalResults));
    }

    public Task<PortResult<Unit>> VoidAsync(
        VoidTransactionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.void");
        VoidRequests.Add(request);
        return Task.FromResult(NextOrSuccess(VoidResults));
    }

    public Task<PortResult<Unit>> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.reconcile");
        ReconciliationRequests.Add(request);
        return Task.FromResult(NextOrSuccess(ReconciliationResults));
    }

    private static PortResult<Unit> NextOrSuccess(Queue<PortResult<Unit>> results) =>
        results.Count == 0
            ? PortResult<Unit>.Success(Unit.Value)
            : results.Dequeue();

    public Queue<PortResult<GiftCardOrderCreated>> OrderCreationResults { get; } = new();

    public Queue<PortResult<GiftCardOrderConfirmation>> OrderConfirmationResults { get; } = new();

    public List<CreateGiftCardOrderRequest> OrderCreationRequests { get; } = [];

    public List<ConfirmGiftCardOrderRequest> OrderConfirmationRequests { get; } = [];

    public Task<PortResult<GiftCardOrderCreated>> CreateOrderAsync(
        CreateGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.orderCreation");
        OrderCreationRequests.Add(request);

        if (OrderCreationResults.Count == 0)
        {
            throw new InvalidOperationException("An order-creation result must be configured.");
        }

        return Task.FromResult(OrderCreationResults.Dequeue());
    }

    public Task<PortResult<GiftCardOrderConfirmation>> ConfirmOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.orderConfirm");
        OrderConfirmationRequests.Add(request);

        if (OrderConfirmationResults.Count == 0)
        {
            throw new InvalidOperationException("An order-confirmation result must be configured.");
        }

        return Task.FromResult(OrderConfirmationResults.Dequeue());
    }

    public Queue<PortResult<Unit>> OrderCancellationResults { get; } = new();

    public Queue<PortResult<GiftCardOrderConfirmation>> OrderStatusResults { get; } = new();

    public List<ConfirmGiftCardOrderRequest> OrderCancellationRequests { get; } = [];

    public List<ConfirmGiftCardOrderRequest> OrderStatusRequests { get; } = [];

    public Task<PortResult<Unit>> CancelOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.orderCancel");
        OrderCancellationRequests.Add(request);
        return Task.FromResult(NextOrSuccess(OrderCancellationResults));
    }

    public Task<PortResult<GiftCardOrderConfirmation>> GetOrderStatusAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.orderStatus");
        OrderStatusRequests.Add(request);

        if (OrderStatusResults.Count == 0)
        {
            throw new InvalidOperationException("An order-status result must be configured.");
        }

        return Task.FromResult(OrderStatusResults.Dequeue());
    }

    public Queue<PortResult<OrderReturnResult>> OrderReturnResults { get; } = new();
    public List<ReturnGiftCardOrderRequest> OrderReturnRequests { get; } = [];

    public Queue<PortResult<IReadOnlyList<GiftCardProduct>>> ProductsResults { get; } = new();
    public List<(StoreId, string?)> ProductsRequests { get; } = [];

    public Task<PortResult<OrderReturnResult>> ReturnOrderAsync(
        ReturnGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.orderReturn");
        OrderReturnRequests.Add(request);

        if (OrderReturnResults.Count == 0)
        {
            throw new InvalidOperationException("An order-return result must be configured.");
        }

        return Task.FromResult(OrderReturnResults.Dequeue());
    }

    public Task<PortResult<IReadOnlyList<GiftCardProduct>>> GetProductsAsync(
        StoreId storeId,
        string? itemCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.getProducts");
        ProductsRequests.Add((storeId, itemCode));

        if (ProductsResults.Count == 0)
        {
            throw new InvalidOperationException("A get-products result must be configured.");
        }

        return Task.FromResult(ProductsResults.Dequeue());
    }

    public Queue<PortResult<IReadOnlyList<OglobaTransactionRecord>>> QueryHistoryResults { get; } = new();

    public Task<PortResult<IReadOnlyList<OglobaTransactionRecord>>> QueryTransactionsHistoryAsync(
        StoreId storeId,
        string? transDateFrom,
        string? transDateTo,
        int pageNo,
        int numberOfPage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("provider.queryTransactionsHistory");

        if (QueryHistoryResults.Count == 0)
        {
            return Task.FromResult(PortResult<IReadOnlyList<OglobaTransactionRecord>>.Success(
                Array.Empty<OglobaTransactionRecord>()));
        }

        return Task.FromResult(QueryHistoryResults.Dequeue());
    }
}
