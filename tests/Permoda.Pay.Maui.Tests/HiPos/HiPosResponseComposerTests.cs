using Permoda.Pay.Application.Errors;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Maui.HiPos.Results;

namespace Permoda.Pay.Maui.Tests.HiPos;

public sealed class HiPosResponseComposerTests
{
    [Fact]
    public void Compose_ApprovedSale_ProducesAcceptedResultWithMaskedCard()
    {
        var composer = new HiPosResponseComposer();
        var result = ProcessSaleResult.Approved(
            "1756113296",
            "00136544716V",
            "113817******5937",
            remainingBalanceMinorUnits: 0,
            "COP",
            requiresRecovery: false,
            storeId: "K00036",
            terminalId: "caja-5",
            amountMinorUnits: 50_000);

        var response = composer.Compose(result, transactionNumber: "1756113296", configurationCardType: "GIFTCARD");

        Assert.Equal(Permoda.Pay.Maui.HiPos.HiPosTransactionResult.Accepted, response.Result);
        Assert.Equal("00136544716V", response.AuthorizationId);
        Assert.Equal("113817******5937", response.CardNum);
        Assert.Equal("GIFTCARD", response.CardType);
        Assert.Contains("COPIA COMERCIO", response.MerchantReceiptXml);
        Assert.Contains("COPIA CLIENTE", response.CustomerReceiptXml);
        Assert.Equal(0, response.RemainingBalanceMinorUnits);
        Assert.Contains("Tarjeta: 113817******5937", response.MerchantReceiptXml!);
        // La tienda y el terminal reales van al recibo (no referenceNumber ni "TRANSACTION").
        Assert.Contains("Tienda: K00036", response.MerchantReceiptXml!);
        Assert.Contains("Terminal: caja-5", response.MerchantReceiptXml!);
        Assert.DoesNotContain("Terminal: TRANSACTION", response.MerchantReceiptXml!);
    }

    [Fact]
    public void Compose_DeclinedSale_ReturnsFailedResultWithErrorMessage()
    {
        var composer = new HiPosResponseComposer();
        var result = ProcessSaleResult.Declined(
            "1756113296",
            ApplicationError.Declined("53", "Insufficient balance"),
            requiresRecovery: false);

        var response = composer.Compose(result, "1756113296", "GIFTCARD");

        Assert.Equal(Permoda.Pay.Maui.HiPos.HiPosTransactionResult.Failed, response.Result);
        Assert.Equal("Insufficient balance", response.ErrorMessage);
        Assert.Null(response.CardNum);
        Assert.Null(response.MerchantReceiptXml);
    }

    [Fact]
    public void Compose_UnknownResult_PropagatesRecoveryHintToHiPOS()
    {
        var composer = new HiPosResponseComposer();
        var result = ProcessSaleResult.Unknown(
            "1756113296",
            "00136544716V",
            ApplicationError.Unknown("ogloba.confirm_timeout", "Confirmation timed out"));

        var response = composer.Compose(result, "1756113296", "GIFTCARD");

        Assert.Equal(Permoda.Pay.Maui.HiPos.HiPosTransactionResult.UnknownResult, response.Result);
        Assert.Equal("Confirmation timed out", response.ErrorMessage);
        Assert.Equal("00136544716V", response.AuthorizationId);
    }
}
