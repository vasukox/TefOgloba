using Permoda.Pay.Application.Errors;

namespace Permoda.Pay.Application.Features.Sales;

public sealed record ActivateVirtualGiftCardResult
{
    private ActivateVirtualGiftCardResult(
        bool isApproved,
        bool isPending,
        string? orderNumber,
        string? cardNumber,
        string? gencode,
        string? pinCode,
        long? balanceMinorUnits,
        string? currency,
        string? receiverEmail,
        string? shortCardNumber,
        string? eGiftCardUrl,
        ApplicationError error)
    {
        ShortCardNumber = shortCardNumber;
        EGiftCardUrl = eGiftCardUrl;
        IsApproved = isApproved;
        IsPending = isPending;
        OrderNumber = orderNumber;
        CardNumber = cardNumber;
        Gencode = gencode;
        PinCode = pinCode;
        BalanceMinorUnits = balanceMinorUnits;
        Currency = currency;
        ReceiverEmail = receiverEmail;
        Error = error;
    }

    public bool IsApproved { get; }

    /// <summary>Orden creada pero Ogloba aún la procesa (orderStatus 041/043). No es un error.</summary>
    public bool IsPending { get; }

    public string? OrderNumber { get; }

    public string? CardNumber { get; }

    public string? Gencode { get; }

    public string? PinCode { get; }

    public long? BalanceMinorUnits { get; }

    public string? Currency { get; }

    public string? ReceiverEmail { get; }

    /// <summary>Serial corto del bono: el que el cliente usa al redimir.</summary>
    public string? ShortCardNumber { get; }

    /// <summary>
    /// Link al bono. La entrega es siempre por correo; esto es la contingencia para cuando no
    /// llega y la referencia de rastreo para soporte.
    /// </summary>
    public string? EGiftCardUrl { get; }

    public ApplicationError Error { get; }

    public static ActivateVirtualGiftCardResult Approved(
        string orderNumber,
        string? cardNumber,
        string? gencode,
        string? pinCode,
        long? balanceMinorUnits,
        string currency,
        string receiverEmail,
        string? shortCardNumber = null,
        string? eGiftCardUrl = null) =>
        new(true, false, orderNumber, cardNumber, gencode, pinCode, balanceMinorUnits, currency, receiverEmail,
            shortCardNumber, eGiftCardUrl, ApplicationError.None);

    public static ActivateVirtualGiftCardResult Pending(string orderNumber, string receiverEmail) =>
        new(false, true, orderNumber, null, null, null, null, null, receiverEmail, null, null, ApplicationError.None);

    public static ActivateVirtualGiftCardResult Failed(ApplicationError error) =>
        new(false, false, null, null, null, null, null, null, null, null, null, error);
}
