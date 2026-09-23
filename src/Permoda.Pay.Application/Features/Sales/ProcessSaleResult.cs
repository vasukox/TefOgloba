using Permoda.Pay.Application.Errors;

namespace Permoda.Pay.Application.Features.Sales;

public sealed record ProcessSaleResult
{
    private ProcessSaleResult(
        ProcessSaleOutcome outcome,
        string? transactionNumber,
        string? referenceNumber,
        string? maskedCardNumber,
        long? remainingBalanceMinorUnits,
        string? currency,
        bool requiresRecovery,
        ApplicationError error,
        string? storeId = null,
        string? terminalId = null,
        long? amountMinorUnits = null,
        string? cardNumber = null,
        string? eGiftCardUrl = null)
    {
        Outcome = outcome;
        TransactionNumber = transactionNumber;
        ReferenceNumber = referenceNumber;
        MaskedCardNumber = maskedCardNumber;
        RemainingBalanceMinorUnits = remainingBalanceMinorUnits;
        Currency = currency;
        RequiresRecovery = requiresRecovery;
        Error = error;
        StoreId = storeId;
        TerminalId = terminalId;
        AmountMinorUnits = amountMinorUnits;
        CardNumber = cardNumber;
        EGiftCardUrl = eGiftCardUrl;
    }

    public ProcessSaleOutcome Outcome { get; }

    public string? TransactionNumber { get; }

    public string? ReferenceNumber { get; }

    public string? MaskedCardNumber { get; }

    public long? RemainingBalanceMinorUnits { get; }

    public string? Currency { get; }

    public bool RequiresRecovery { get; }

    public ApplicationError Error { get; }

    // La tienda, el terminal y el monto cobrado reales viajan en el
    // resultado para que el recibo no reutilice el referenceNumber ni el saldo restante.
    public string? StoreId { get; }

    public string? TerminalId { get; }

    public long? AmountMinorUnits { get; }

    // Serial real y link del e-gift-card que Ogloba genera en /activation (solo disponibles en
    // activación, nulos en redención). Ver RedemptionAuthorization.CardNumber/EGiftCardUrl.
    public string? CardNumber { get; }

    public string? EGiftCardUrl { get; }

    public static ProcessSaleResult Approved(
        string transactionNumber,
        string referenceNumber,
        string maskedCardNumber,
        long remainingBalanceMinorUnits,
        string currency,
        bool requiresRecovery,
        string storeId,
        string terminalId,
        long amountMinorUnits,
        string? cardNumber = null,
        string? eGiftCardUrl = null) =>
        new(
            ProcessSaleOutcome.Approved,
            transactionNumber,
            referenceNumber,
            maskedCardNumber,
            remainingBalanceMinorUnits,
            currency,
            requiresRecovery,
            ApplicationError.None,
            storeId,
            terminalId,
            amountMinorUnits,
            cardNumber,
            eGiftCardUrl);

    public static ProcessSaleResult Declined(
        string? transactionNumber,
        ApplicationError error,
        bool requiresRecovery = false) =>
        new(
            ProcessSaleOutcome.Declined,
            transactionNumber,
            null,
            null,
            null,
            null,
            requiresRecovery,
            error);

    public static ProcessSaleResult Unknown(
        string transactionNumber,
        string? referenceNumber,
        ApplicationError error) =>
        new(
            ProcessSaleOutcome.Unknown,
            transactionNumber,
            referenceNumber,
            null,
            null,
            null,
            true,
            error);
}
