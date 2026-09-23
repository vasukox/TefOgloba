using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Payments;

/// <summary>
/// Pedido de bono digital vía POST /orderCreation. A diferencia de /activation (atómico,
/// sin datos del cliente), este es el único flujo que Ogloba documenta con envío de correo
/// automático — orderItems[].deliverType=3 (eMail) + receiverEmail
/// (docs/OGLOBA_API_REFERENCE.md §3.9).
/// </summary>
/// <param name="Message">
/// Mensaje que Ogloba inyecta en la plantilla del correo (p. ej. "¡Feliz cumpleaños!"). Opcional.
/// </param>
/// <param name="SenderName">
/// Quién aparece como remitente en el correo. La plantilla, el asunto y el diseño los define
/// Ogloba en su backend; desde aquí solo se aportan los datos que se inyectan en ella.
/// </param>
/// <param name="CustomerDocumentNumber">
/// Cédula del cliente (solo dígitos). Se concatena con <see cref="CustomerName"/> y se añade al
/// <see cref="Message"/> que se manda a Ogloba, para que el dato quede asociado a la
/// transacción / bono en el backoffice.
/// </param>
/// <param name="CustomerName">
/// Nombre y apellidos del cliente. Se concatena con <see cref="CustomerDocumentNumber"/> y se
/// añade al <see cref="Message"/> que se manda a Ogloba.
/// </param>
/// <remarks>
/// No hay campo dedicado para el nombre del destinatario en /orderCreation: el endpoint
/// <c>receiverName</c> documentado lo guarda en <c>mobileNo</c> del bono, comportamiento
/// que ensucia el registro del bono sin un destino claro. Se concatena en el <c>message</c>
/// de texto libre para mantener el dato del cliente en la auditoría sin tocar ese bug.
/// </remarks>
public sealed record CreateGiftCardOrderRequest(
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    string ClientOrderNumber,
    string ItemCode,
    Money FaceAmount,
    string ReceiverEmail,
    string? Message = null,
    string? SenderName = null,
    string? CustomerDocumentNumber = null,
    string? CustomerName = null);

public sealed record GiftCardOrderCreated(string OrderNumber, Money OrderAmount);

public sealed record ConfirmGiftCardOrderRequest(
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    string OrderNumber);

/// <summary>
/// docs/OGLOBA_API_REFERENCE.md §3.13 (orderReturn). F: full, P: partial.
/// Si <see cref="ReturnType"/> es 'P', <see cref="PartialReturnDetails"/> describe qué tarjetas devolver.
/// </summary>
public sealed record ReturnGiftCardOrderRequest(
    StoreId StoreId,
    TerminalId TerminalId,
    CashierId CashierId,
    string OrderNumber,
    string ReturnType,
    long ReturnFee,
    long CardFee,
    long ShippingFee,
    IReadOnlyList<PartialReturnDetail>? PartialReturnDetails);

public sealed record PartialReturnDetail(
    string? CardNumberBegin,
    string? CardNumberEnd,
    long FaceValueMinorUnits,
    string ItemCode,
    int Quantity);

/// <param name="OrderStatus">
/// "042" completado (tarjetas listas), "041"/"043" en proceso (doc §3.12: tratar 041 como 043),
/// cualquier otro valor se trata como fallo.
/// </param>
public sealed record GiftCardOrderConfirmation(
    string OrderNumber,
    string OrderStatus,
    IReadOnlyList<IssuedGiftCard> Cards)
{
    public bool IsCompleted => OrderStatus == "042";

    public bool IsInProgress => OrderStatus is "041" or "043";
}

/// <param name="ShortCardNumber">
/// Serial corto/legible del bono. Es el que el cliente digita o escanea al redimir.
/// </param>
/// <param name="EGiftCardUrl">
/// Link al bono emitido. La entrega al cliente es SIEMPRE por correo (deliverType 3); esto no es
/// un canal alterno sino la contingencia para cuando el correo no llega —caso que el propio
/// manual de KOAJ contempla (revisar spam)—, y la referencia con la que soporte puede rastrear
/// el bono sin entrar al back office.
/// </param>
public sealed record IssuedGiftCard(
    string? CardNumber,
    string? Gencode,
    string? PinCode1,
    string? ExpiryDate,
    long? CardBalanceMinorUnits,
    string? ShortCardNumber = null,
    string? EGiftCardUrl = null);
