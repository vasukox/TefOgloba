using Permoda.Pay.Application.Errors;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Maui.HiPos.Results;

public sealed class HiPosResponseComposer
{
    public const string MerchantReceiptExtra = "MerchantReceipt";
    public const string CustomerReceiptExtra = "CustomerReceipt";
    public const string AuthorizationIdExtra = "AuthorizationId";
    public const string CardNumExtra = "CardNum";
    public const string CardHolderExtra = "CardHolder";
    public const string CardTypeExtra = "CardType";
    public const string ErrorMessageExtra = "ErrorMessage";

    /// <summary>
    /// Título del error. API TEF 4.0 §Transaction lo lista entre los campos de salida junto a
    /// <c>ErrorMessage</c>: es el encabezado del aviso que HiPOS le muestra al cajero.
    /// </summary>
    public const string ErrorMessageTitleExtra = "ErrorMessageTitle";
    public const string TransactionResultExtra = "TransactionResult";

    public HiPosTransactionResponse Compose(
        ProcessSaleResult result,
        string transactionNumber,
        string configurationCardType) =>
        result.Outcome switch
        {
            ProcessSaleOutcome.Approved => ComposeApproved(result, configurationCardType),
            ProcessSaleOutcome.Declined => ComposeFailed(result),
            ProcessSaleOutcome.Unknown => ComposeUnknown(result, transactionNumber),
            _ => ComposeFailed(result)
        };

    private static HiPosTransactionResponse ComposeApproved(
        ProcessSaleResult result,
        string cardType)
    {
        if (result.MaskedCardNumber is null)
        {
            return new HiPosTransactionResponse(
                HiPosTransactionResult.Accepted,
                AuthorizationId: result.ReferenceNumber,
                CardNum: null,
                CardHolder: null,
                CardType: cardType,
                MerchantReceiptXml: null,
                CustomerReceiptXml: null,
                ErrorMessage: null,
                RemainingBalanceMinorUnits: result.RemainingBalanceMinorUnits,
                Currency: result.Currency);
        }

        // Usar la tienda, el terminal y el monto cobrado reales.
        // Antes se pasaba el referenceNumber como tienda, el literal "TRANSACTION" como
        // terminal y el saldo restante como monto, produciendo recibos incorrectos.
        var referenceNumber = result.ReferenceNumber ?? string.Empty;
        var currency = result.Currency ?? Money.DefaultCurrency;
        var storeId = result.StoreId ?? string.Empty;
        var terminalId = result.TerminalId ?? string.Empty;
        var amount = result.AmountMinorUnits ?? 0;
        var remainingBalance = result.RemainingBalanceMinorUnits ?? 0;

        // result.CardNumber solo viene poblado cuando Ogloba generó/devolvió un serial real en
        // /activation (nulo en /redemption). Para el comercio se enmascara igual que siempre,
        // pero usando el serial REAL en vez del identificador que se envió (el gencode genérico
        // 113815 es el mismo para cualquier bono virtual, así que enmascararlo no distingue nada).
        var merchantCardDisplay = MaskCardNumber(result.CardNumber) ?? result.MaskedCardNumber;

        return new HiPosTransactionResponse(
            HiPosTransactionResult.Accepted,
            AuthorizationId: result.ReferenceNumber,
            CardNum: merchantCardDisplay,
            CardHolder: null,
            CardType: cardType,
            MerchantReceiptXml: ReceiptBuilder.BuildMerchantReceipt(
                storeId,
                terminalId,
                referenceNumber,
                maskedCardNumber: merchantCardDisplay ?? string.Empty,
                amountMinorUnits: amount,
                currency,
                remainingBalanceMinorUnits: remainingBalance),
            CustomerReceiptXml: ReceiptBuilder.BuildCustomerReceipt(
                storeId,
                terminalId,
                referenceNumber,
                // Al cliente SIN enmascarar cuando hay un serial real: en un bono virtual, este
                // recibo es la única prueba que se lleva para poder redimirlo después.
                cardDisplayValue: result.CardNumber ?? result.MaskedCardNumber,
                amountMinorUnits: amount,
                currency,
                remainingBalanceMinorUnits: remainingBalance,
                eGiftCardUrl: result.EGiftCardUrl),
            ErrorMessage: null,
            RemainingBalanceMinorUnits: remainingBalance,
            Currency: currency);
    }

    private static string? MaskCardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return null;
        }

        var result = CardIdentifier.CreatePhysicalCard(cardNumber);
        return result.IsSuccess ? result.Value.MaskedValue : cardNumber;
    }

    private static HiPosTransactionResponse ComposeFailed(ProcessSaleResult result) =>
        new(
            HiPosTransactionResult.Failed,
            AuthorizationId: result.ReferenceNumber,
            CardNum: null,
            CardHolder: null,
            CardType: null,
            MerchantReceiptXml: null,
            CustomerReceiptXml: null,
            ErrorMessage: ToUserMessage(result.Error),
            RemainingBalanceMinorUnits: null,
            Currency: null);

    private static HiPosTransactionResponse ComposeUnknown(
        ProcessSaleResult result,
        string transactionNumber) =>
        new(
            HiPosTransactionResult.UnknownResult,
            AuthorizationId: result.ReferenceNumber ?? transactionNumber,
            CardNum: null,
            CardHolder: null,
            CardType: null,
            MerchantReceiptXml: null,
            CustomerReceiptXml: null,
            ErrorMessage: ToUserMessage(result.Error),
            RemainingBalanceMinorUnits: null,
            Currency: null);

    private static string? ToUserMessage(ApplicationError? error) =>
        error is null || error == ApplicationError.None
            ? null
            : error.Description;
}
