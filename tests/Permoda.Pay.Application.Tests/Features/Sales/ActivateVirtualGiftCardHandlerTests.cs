using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Application.Tests.TestDoubles;

namespace Permoda.Pay.Application.Tests.Features.Sales;

public sealed class ActivateVirtualGiftCardHandlerTests
{
    private static ActivateVirtualGiftCardCommand CreateCommand(string email = "cliente@correo.com") =>
        new("K00036", "CAJA-01", "cajero-1", "113815", 50_000, "COP", email);

    private static (ActivateVirtualGiftCardHandler Handler, FakeGiftCardProvider Provider) CreateSut()
    {
        var calls = new List<string>();
        var provider = new FakeGiftCardProvider(calls);
        var handler = new ActivateVirtualGiftCardHandler(provider, new StubClock());
        return (handler, provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba.com")]
    public async Task HandleAsync_WithoutValidEmail_FailsWithoutCallingProvider(string email)
    {
        var (handler, provider) = CreateSut();

        var result = await handler.HandleAsync(CreateCommand(email), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.False(result.IsPending);
        Assert.Equal("gift_card_order.email_required", result.Error.Code);
        Assert.Empty(provider.OrderCreationRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenOrderCompletes_ReturnsApprovedWithIssuedCard()
    {
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Success(
            new GiftCardOrderCreated("MA2503000197", Domain.Payments.Money.Create(50_000, "COP").Value)));
        provider.OrderConfirmationResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Success(
            new GiftCardOrderConfirmation(
                "MA2503000197",
                "042",
                [new IssuedGiftCard("9959852906258922", "113815", "1334", "20270728", 50_000)])));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.False(result.IsPending);
        Assert.Equal("MA2503000197", result.OrderNumber);
        Assert.Equal("9959852906258922", result.CardNumber);
        Assert.Equal("1334", result.PinCode);
        Assert.Equal(50_000, result.BalanceMinorUnits);
        Assert.Equal("cliente@correo.com", result.ReceiverEmail);
        Assert.Single(provider.OrderCreationRequests);
        Assert.Equal("cliente@correo.com", provider.OrderCreationRequests[0].ReceiverEmail);
        Assert.Single(provider.OrderConfirmationRequests);
    }

    [Theory]
    [InlineData("041")]
    [InlineData("043")]
    public async Task HandleAsync_WhenOrderStaysInProgress_ReturnsPendingWithoutCancelling(string orderStatus)
    {
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Success(
            new GiftCardOrderCreated("MA2503000198", Domain.Payments.Money.Create(50_000, "COP").Value)));
        provider.OrderConfirmationResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Success(
            new GiftCardOrderConfirmation("MA2503000198", orderStatus, [])));
        // El reintento de estado tampoco resuelve: sigue emitiendo.
        provider.OrderStatusResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Success(
            new GiftCardOrderConfirmation("MA2503000198", orderStatus, [])));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.True(result.IsPending);
        Assert.Equal("MA2503000198", result.OrderNumber);
        Assert.Equal("cliente@correo.com", result.ReceiverEmail);

        // Crítico: un pedido en proceso NO se cancela. El bono es válido y puede ir camino al
        // correo del cliente; cancelarlo destruiría un bono ya emitido.
        Assert.Empty(provider.OrderCancellationRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenStatusRetryResolvesTheOrder_ReturnsApproved()
    {
        // orderStatus 043 suele resolverse en segundos: consultamos una vez más para que el
        // cajero se lleve el número de tarjeta en el momento en vez de un "en proceso".
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Success(
            new GiftCardOrderCreated("MA2503000200", Domain.Payments.Money.Create(50_000, "COP").Value)));
        provider.OrderConfirmationResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Success(
            new GiftCardOrderConfirmation("MA2503000200", "043", [])));
        provider.OrderStatusResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Success(
            new GiftCardOrderConfirmation(
                "MA2503000200",
                "042",
                [new IssuedGiftCard("9959852906258999", "113815", "4455", "20270728", 50_000)])));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.Equal("9959852906258999", result.CardNumber);
        Assert.Single(provider.OrderStatusRequests);
        Assert.Empty(provider.OrderCancellationRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenStoreHasNoOrderManagement_ExplainsWhatToDo()
    {
        // Error 23 aquí no es "tienda inexistente": /activation y /balance funcionan con la misma
        // tienda. Es que le falta Order management habilitado en el back office de Ogloba.
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Failed(
            new PortFailure("23", "Unknown store", PortFailureType.Rejected)));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.False(result.IsPending);
        Assert.Equal("gift_card_order.store_without_order_management", result.Error.Code);
        Assert.Contains("Order management", result.Error.Description);
        Assert.Empty(provider.OrderConfirmationRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenConfirmationFails_CancelsTheOrphanOrder()
    {
        // El pedido ya existe en Ogloba: si no se cancela queda colgado consumiendo cupo.
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Success(
            new GiftCardOrderCreated("MA2503000199", Domain.Payments.Money.Create(50_000, "COP").Value)));
        provider.OrderConfirmationResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Failed(
            new PortFailure("998", "Unexpected error", PortFailureType.Rejected)));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal("998", result.Error.Code);
        Assert.Single(provider.OrderCancellationRequests);
        Assert.Equal("MA2503000199", provider.OrderCancellationRequests[0].OrderNumber);
    }

    [Fact]
    public async Task HandleAsync_WhenCancellationAlsoFails_TellsTheCashierWhichOrderIsOpen()
    {
        var (handler, provider) = CreateSut();
        provider.OrderCreationResults.Enqueue(PortResult<GiftCardOrderCreated>.Success(
            new GiftCardOrderCreated("MA2503000201", Domain.Payments.Money.Create(50_000, "COP").Value)));
        provider.OrderConfirmationResults.Enqueue(PortResult<GiftCardOrderConfirmation>.Failed(
            new PortFailure("998", "Unexpected error", PortFailureType.Rejected)));
        provider.OrderCancellationResults.Enqueue(PortResult<Unit>.Failed(
            new PortFailure("23010", "Unknown order no.", PortFailureType.Rejected)));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Contains("MA2503000201", result.Error.Description);
        Assert.Contains("back office", result.Error.Description);
    }
}
