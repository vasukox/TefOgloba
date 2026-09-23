namespace Permoda.Pay.Application.Features.Sales;

/// <summary>
/// Activación de bono virtual con envío de correo automático por parte de Ogloba
/// (POST /orderCreation + /orderConfirm — no /activation, que no acepta datos del cliente;
/// ver docs/OGLOBA_API_REFERENCE.md §3.9-3.10).
/// </summary>
/// <param name="Message">Mensaje para la plantilla del correo de Ogloba. Opcional.</param>
/// <param name="SenderName">Remitente visible del correo: normalmente el nombre de la tienda.</param>
/// <param name="CustomerDocumentNumber">
/// Cédula del cliente (solo dígitos). Se concatena con <see cref="CustomerName"/> y se manda a
/// Ogloba en el campo <c>message</c> del <c>orderItems[0]</c> del request a /orderCreation.
/// </param>
/// <param name="CustomerName">
/// Nombre y apellidos del cliente. Se concatena con <see cref="CustomerDocumentNumber"/> y se
/// manda a Ogloba en el campo <c>message</c> del <c>orderItems[0]</c> del request.
/// </param>
public sealed record ActivateVirtualGiftCardCommand(
    string StoreId,
    string TerminalId,
    string CashierId,
    string ItemCode,
    long AmountUnits,
    string Currency,
    string ReceiverEmail,
    string? Message = null,
    string? SenderName = null,
    string? CustomerDocumentNumber = null,
    string? CustomerName = null);
