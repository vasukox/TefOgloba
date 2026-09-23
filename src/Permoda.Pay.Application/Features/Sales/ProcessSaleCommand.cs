using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Application.Features.Sales;

/// <summary>
/// Comando del flujo POS (activación / redención / anulación). El campo
/// <see cref="AmountUnits"/> representa la unidad entera de la moneda indicada en
/// <see cref="Currency"/>; el handler lo envía a Ogloba sin transformación, por lo que
/// el cajero captura el monto en la unidad de la moneda (por ejemplo, pesos COP).
/// </summary>
/// <param name="CustomerDocumentNumber">
/// Cédula del cliente (solo dígitos). Opcional en redención; en activación se concatena con
/// <see cref="CustomerName"/> y se manda a Ogloba en el campo <c>note</c> del request.
/// </param>
/// <param name="CustomerName">
/// Nombre y apellidos del cliente. En activación se concatena con
/// <see cref="CustomerDocumentNumber"/> y se manda a Ogloba en el campo <c>note</c> del request.
/// </param>
public sealed record ProcessSaleCommand(
    string StoreId,
    string TerminalId,
    string CashierId,
    string CardNumber,
    long AmountUnits,
    string Currency,
    GiftCardOperation Operation = GiftCardOperation.Redemption,
    CardIdentifierKind CardKind = CardIdentifierKind.PhysicalCard,
    string? CustomerDocumentNumber = null,
    string? CustomerName = null,
    string? CustomerEmail = null);
