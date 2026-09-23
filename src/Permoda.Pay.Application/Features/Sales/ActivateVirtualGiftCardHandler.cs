using System.Text.RegularExpressions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Features.Sales;

/// <summary>
/// Separado de <see cref="ProcessSaleHandler"/> porque /orderCreation + /orderConfirm no
/// comparten el modelo Requested/Confirmed/Reconciled de <c>PaymentTransaction</c> (Step 1/2/3) —
/// es un flujo propio de "Order management" con su propio estado (orderStatus).
/// </summary>
public sealed partial class ActivateVirtualGiftCardHandler
{
    // Suficiente para rechazar "a@", "@b", "a@b" (sin dominio) sin pretender validar RFC 5322
    // completo — Ogloba es quien finalmente rebota un correo inválido.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private readonly IGiftCardProvider _provider;
    private readonly IClock _clock;

    public ActivateVirtualGiftCardHandler(IGiftCardProvider provider, IClock clock)
    {
        _provider = provider;
        _clock = clock;
    }

    public async Task<ActivateVirtualGiftCardResult> HandleAsync(
        ActivateVirtualGiftCardCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = command.ReceiverEmail?.Trim() ?? string.Empty;

        // Ogloba solo envía el bono automáticamente en este flujo (§3.9); sin correo válido no
        // tiene sentido crear el pedido — por eso "siempre" se exige aquí, no como validación de UI.
        if (string.IsNullOrWhiteSpace(email) || !EmailPattern().IsMatch(email))
        {
            return ActivateVirtualGiftCardResult.Failed(ApplicationError.Validation(
                "gift_card_order.email_required",
                "Se requiere un correo válido del cliente para que Ogloba envíe el bono."));
        }

        var storeIdResult = StoreId.Create(command.StoreId);
        var terminalIdResult = TerminalId.Create(command.TerminalId);
        var cashierIdResult = CashierId.Create(command.CashierId);
        var amountResult = Money.Create(command.AmountUnits, command.Currency);

        if (storeIdResult.IsFailure || terminalIdResult.IsFailure || cashierIdResult.IsFailure || amountResult.IsFailure)
        {
            var error = storeIdResult.IsFailure
                ? storeIdResult.Error
                : terminalIdResult.IsFailure
                    ? terminalIdResult.Error
                    : cashierIdResult.IsFailure
                        ? cashierIdResult.Error
                        : amountResult.Error;

            return ActivateVirtualGiftCardResult.Failed(ApplicationError.Validation(error.Code, error.Description));
        }

        var clientOrderNumber = $"OGB{_clock.UtcNow:yyyyMMddHHmmssfff}";

        var creationResult = await _provider.CreateOrderAsync(
            new CreateGiftCardOrderRequest(
                storeIdResult.Value,
                terminalIdResult.Value,
                cashierIdResult.Value,
                clientOrderNumber,
                command.ItemCode,
                amountResult.Value,
                email,
                command.Message,
                command.SenderName,
                command.CustomerDocumentNumber,
                command.CustomerName),
            cancellationToken);

        if (creationResult.IsFailure)
        {
            // Error 23 en ESTE endpoint no significa "tienda inexistente": /activation,
            // /redemption y /balance funcionan con la misma tienda. Significa que la tienda no
            // tiene habilitado Order management (salesType MA) en el back office de Ogloba, que
            // es lo único con lo que Ogloba envía el bono por correo. Sin este mensaje, el
            // cajero lee "Tienda no registrada" y escala por el camino equivocado.
            var creationError = creationResult.Failure.Code == "23"
                ? ApplicationError.Declined(
                    "gift_card_order.store_without_order_management",
                    "Esta tienda no tiene habilitada la emisión de bonos virtuales en Ogloba. " +
                    "Solicita al administrador que active Order management para la tienda; " +
                    "los bonos físicos no se ven afectados.")
                : ApplicationError.Declined(creationResult.Failure.Code, creationResult.Failure.Description);

            return ActivateVirtualGiftCardResult.Failed(creationError);
        }

        var orderNumber = creationResult.Value.OrderNumber;

        var confirmRequest = new ConfirmGiftCardOrderRequest(
            storeIdResult.Value,
            terminalIdResult.Value,
            cashierIdResult.Value,
            orderNumber);

        var confirmationResult = await _provider.ConfirmOrderAsync(confirmRequest, cancellationToken);

        if (confirmationResult.IsFailure)
        {
            // El pedido ya existe en Ogloba pero no se pudo confirmar: si lo dejamos así queda
            // colgado consumiendo el cupo de la tienda y nadie lo cierra. Lo cancelamos.
            // Es el equivalente al /reversal del flujo físico.
            var cancelled = await TryCancelAsync(confirmRequest, cancellationToken);

            var description = cancelled
                ? confirmationResult.Failure.Description
                : $"{confirmationResult.Failure.Description} El pedido {orderNumber} quedó abierto en Ogloba " +
                  "y debe cancelarse desde el back office.";

            return ActivateVirtualGiftCardResult.Failed(
                ApplicationError.Declined(confirmationResult.Failure.Code, description));
        }

        var confirmation = confirmationResult.Value;

        if (confirmation.IsInProgress || confirmation.Cards.Count == 0)
        {
            // orderStatus 041/043: Ogloba sigue emitiendo. Consultamos una vez más antes de
            // devolverle "en proceso" al cajero, porque la emisión suele resolverse en segundos
            // y así se lleva el número de tarjeta en el momento.
            // NUNCA se cancela en este caso: el bono es válido y puede estar en camino al correo.
            var statusResult = await _provider.GetOrderStatusAsync(confirmRequest, cancellationToken);

            if (statusResult.IsSuccess && statusResult.Value.Cards.Count > 0 && !statusResult.Value.IsInProgress)
            {
                confirmation = statusResult.Value;
            }
            else
            {
                return ActivateVirtualGiftCardResult.Pending(orderNumber, email);
            }
        }

        var card = confirmation.Cards[0];

        return ActivateVirtualGiftCardResult.Approved(
            confirmation.OrderNumber,
            card.CardNumber,
            card.Gencode,
            card.PinCode1,
            card.CardBalanceMinorUnits,
            amountResult.Value.Currency,
            email,
            card.ShortCardNumber,
            card.EGiftCardUrl);
    }

    /// <summary>
    /// Cancela el pedido en Ogloba tras una confirmación fallida. Best-effort: si la cancelación
    /// también falla, el llamador se lo dice al cajero con el número de pedido para que lo
    /// cierren desde el back office, en vez de dejarlo abierto en silencio consumiendo cupo.
    /// </summary>
    private async Task<bool> TryCancelAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provider.CancelOrderAsync(request, cancellationToken);
            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // La cancelación es una red de seguridad: que falle no debe cambiar el error real
            // que se le reporta al cajero, solo añadirle el aviso del pedido abierto.
            return false;
        }
    }
}
