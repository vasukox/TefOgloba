using System.Xml.Linq;
using Permoda.Pay.Maui.HiPos.Requests;

namespace Permoda.Pay.Maui.Tests.HiPos;

public sealed class HiPosRequestMapperTests
{
    [Fact]
    public void MapInitialization_ParsesHttpsBaseUrlAndExposesConfiguration()
    {
        var xml = XDocument.Parse($"""
            <Configuration>
                <StoreId>K00036</StoreId>
                <Passphrase>secret</Passphrase>
                <BaseUrl>https://ogloba.test/giftCardService</BaseUrl>
                <ApiVersion>2.18</ApiVersion>
            </Configuration>
            """);

        var mapper = new HiPosRequestMapper();
        var result = mapper.MapInitialization(xml);

        Assert.True(result.IsSuccess);
        Assert.Equal("K00036", result.Configuration!.StoreId);
        Assert.Equal("secret", result.Configuration.Passphrase);
        Assert.Equal("https://ogloba.test/giftCardService/", result.Configuration.BaseUrl);
        Assert.Equal("2.18", result.Configuration.ApiVersion);
    }

    [Fact]
    public void MapInitialization_RejectsHttpBaseUrl()
    {
        var xml = XDocument.Parse("""
            <Configuration>
                <StoreId>K00036</StoreId>
                <BaseUrl>http://insecure.test/</BaseUrl>
                <ApiVersion>2.18</ApiVersion>
            </Configuration>
            """);

        var result = new HiPosRequestMapper().MapInitialization(xml);

        Assert.False(result.IsSuccess);
        Assert.Equal("hipos.configuration.baseurl_invalid", result.ErrorCode);
    }

    [Theory]
    // Contrato de ICG: entero serializado como string donde los DOS ÚLTIMOS DÍGITOS son los
    // decimales ("el importe 0.01 se recibirá como 001"). Una venta de $57.415 llega como
    // 5741500 y hay que dividir entre 100; no dividir cobraría 100 veces de más.
    [InlineData("5741500", 57415L)]
    [InlineData("100", 1L)]
    [InlineData("0000050000", 500L)]
    // Variante con separador decimal explícito: ya viene en unidades, no en céntimos. Las dos
    // lecturas coinciden en el mismo importe para la misma venta.
    [InlineData("57415,0000", 57415L)]
    [InlineData("57415.0000", 57415L)]
    [InlineData("1.234.567,0000", 1234567L)]
    [InlineData("1,234,567.0000", 1234567L)]
    [InlineData("57415,", 57415L)]
    public void MapTransaction_ConvertsHiPosAmountToWholePesos(string raw, long expected)
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.AmountProperty] = raw;

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Transaction!.AmountMinorUnits);
    }

    [Fact]
    public void MapTransaction_RejectsNonNumericAmount()
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.AmountProperty] = "no-es-un-monto";

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.False(result.IsSuccess);
        Assert.Equal("hipos.transaction.amount_invalid", result.ErrorCode);
    }

    [Fact]
    public void MapTransaction_AcceptsSaleWithoutCardNumber()
    {
        // El contrato de ICG no incluye CardNumber entre los campos de entrada de un SALE:
        // HiPOS manda el importe y el módulo captura el bono al levantarse. Exigirlo aquí
        // hacía que toda venta por HiPOS fallara antes de llegar a Ogloba.
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.CardNumberProperty] = string.Empty;

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Transaction!.CardNumber);
        Assert.Equal(Permoda.Pay.Maui.HiPos.HiPosTransactionType.Sale, result.Transaction.TransactionType);
    }

    [Fact]
    public void MapTransaction_BuildsTransactionRequestUsingConfigurationStoreId()
    {
        var extras = CreateRequiredExtras();
        extras.Remove(HiPosRequestMapper.StoreIdIntentProperty);

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Equal("K00036", result.Transaction!.StoreId);
        Assert.Equal(50_000, result.Transaction.AmountMinorUnits);
        Assert.Equal("caja-5", result.Transaction.TerminalId);
        Assert.Equal(Permoda.Pay.Maui.HiPos.HiPosTransactionType.Sale, result.Transaction.TransactionType);
    }

    [Fact]
    public void MapTransaction_NormalizesCurrencyAndReferenceNumber()
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.CurrencyProperty] = "cop";
        extras[HiPosRequestMapper.ReferenceNumberProperty] = "00136544716V";

        var result = new HiPosRequestMapper().MapTransaction(extras, configurationStoreId: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("COP", result.Transaction!.Currency);
        Assert.Equal("00136544716V", result.Transaction.ReferenceNumber);
    }

    [Fact]
    public void MapTransaction_FallsBackToSessionTerminalIdWhenIntentOmitsIt()
    {
        // HiPOS no envía TerminalId/CashierId en el intent TRANSACTION.
        // El orquestador los resuelve desde PosSession (Configuration.TerminalId +
        // ActiveCashierId) y los pasa como fallback. Aquí verificamos que el mapper
        // usa esos fallbacks cuando el intent no los trae.
        var extras = CreateRequiredExtras();
        extras.Remove(HiPosRequestMapper.TerminalIdProperty);
        extras.Remove(HiPosRequestMapper.CashierIdProperty);

        var result = new HiPosRequestMapper().MapTransaction(
            extras,
            configurationStoreId: "K00036",
            sessionTerminalId: "caja-from-session",
            sessionCashierId: "operador-from-session");

        Assert.True(result.IsSuccess);
        Assert.Equal("caja-from-session", result.Transaction!.TerminalId);
        Assert.Equal("operador-from-session", result.Transaction.CashierId);
    }

    [Fact]
    public void MapTransaction_IntentTerminalAndCashierTakePrecedenceOverSession()
    {
        // Si HiPOS manda TerminalId/CashierId en el intent, esos ganan sobre la sesión.
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.TerminalIdProperty] = "caja-from-intent";
        extras[HiPosRequestMapper.CashierIdProperty] = "operador-from-intent";

        var result = new HiPosRequestMapper().MapTransaction(
            extras,
            configurationStoreId: "K00036",
            sessionTerminalId: "caja-from-session",
            sessionCashierId: "operador-from-session");

        Assert.True(result.IsSuccess);
        Assert.Equal("caja-from-intent", result.Transaction!.TerminalId);
        Assert.Equal("operador-from-intent", result.Transaction.CashierId);
    }

    [Fact]
    public void MapTransaction_StillReportsMissingWhenNeitherIntentNorSessionHasTerminalId()
    {
        var extras = CreateRequiredExtras();
        extras.Remove(HiPosRequestMapper.TerminalIdProperty);

        var result = new HiPosRequestMapper().MapTransaction(
            extras,
            configurationStoreId: "K00036",
            sessionTerminalId: null,
            sessionCashierId: "operador-from-session");

        Assert.False(result.IsSuccess);
        Assert.Equal("hipos.transaction.incomplete", result.ErrorCode);
        Assert.Contains("TerminalId", result.ErrorMessage);
    }

    [Fact]
    public void MapTransaction_AllowsVoidWithoutCardNumber()
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.TransactionTypeProperty] = "VOID_TRANSACTION";
        extras[HiPosRequestMapper.ReferenceNumberProperty] = "00136544716V";
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Permoda.Pay.Maui.HiPos.HiPosTransactionType.VoidTransaction,
            result.Transaction!.TransactionType);
        Assert.Null(result.Transaction.CardNumber);
        Assert.Equal("00136544716V", result.Transaction.ReferenceNumber);
    }

    /// <summary>
    /// Un REFUND sin referencia YA NO se rechaza.
    /// <para>
    /// Se rechazaba cuando ese intent se resolvía llamando a Ogloba, que identifica la
    /// transacción por la referencia. Hoy soltar la línea de pago es una acción del POS y no
    /// toca a Ogloba, así que la referencia quedó como dato de trazabilidad. Rechazar por un
    /// dato que ya no se usa devolvía la caja al estado que este camino existe para evitar: la
    /// línea trabada y el cajero sin poder seguir.
    /// </para>
    /// </summary>
    [Fact]
    public void MapTransaction_AllowsRefundWithoutReference()
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.TransactionTypeProperty] = "REFUND";
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Permoda.Pay.Maui.HiPos.HiPosTransactionType.Refund,
            result.Transaction!.TransactionType);
    }

    [Fact]
    public void MapTransaction_AllowsBatchCloseWithoutAmountOrCard()
    {
        var extras = CreateRequiredExtras();
        extras[HiPosRequestMapper.TransactionTypeProperty] = "BATCH_CLOSE";
        extras.Remove(HiPosRequestMapper.AmountProperty);
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        var result = new HiPosRequestMapper().MapTransaction(extras, "K00036");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Permoda.Pay.Maui.HiPos.HiPosTransactionType.BatchClose,
            result.Transaction!.TransactionType);
    }

    private static Dictionary<string, string?> CreateRequiredExtras() =>
        new()
        {
            [HiPosRequestMapper.TransactionTypeProperty] = "SALE",
            [HiPosRequestMapper.StoreIdIntentProperty] = "K00036",
            [HiPosRequestMapper.TerminalIdProperty] = "caja-5",
            [HiPosRequestMapper.CashierIdProperty] = "operador-123",
            // Formato del contrato: los dos últimos dígitos son decimales, así que una venta
            // de $50.000 viaja como "5000000".
            [HiPosRequestMapper.AmountProperty] = "5000000",
            [HiPosRequestMapper.CurrencyProperty] = "COP",
            [HiPosRequestMapper.CardNumberProperty] = "1138170025515937"
        };
}
