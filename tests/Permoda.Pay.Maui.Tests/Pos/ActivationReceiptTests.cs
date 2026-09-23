using Permoda.Pay.Maui.HiPos.Results;

namespace Permoda.Pay.Maui.Tests.Pos;

/// <summary>
/// El comprobante que se guarda en CADA activación.
/// <para>
/// La activación no pasa por HiPOS —es un flujo manual con su propia autenticación de cajero— y
/// HiPOS solo imprime como parte de una <c>TRANSACTION</c>. Sin este comprobante, una activación
/// no dejaba rastro imprimible en ninguna parte.
/// </para>
/// <para>
/// Se prueba el contenido, que es lo que el cliente se lleva. El guardado en disco vive en
/// <c>ActivationReceiptStore</c>, que depende de <c>FileSystem.AppDataDirectory</c> de MAUI y no
/// se puede montar fuera del dispositivo.
/// </para>
/// </summary>
public sealed class ActivationReceiptTests
{
    private const string Serial = "1138170025515937";

    private static string BuildReceipt(string? url = null) =>
        ReceiptBuilder.BuildCustomerReceipt(
            storeId: "K00036",
            terminalId: "caja-5",
            referenceNumber: "00136544716V",
            cardDisplayValue: Serial,
            amountMinorUnits: 50_000,
            currency: "COP",
            remainingBalanceMinorUnits: 50_000,
            eGiftCardUrl: url);

    /// <summary>
    /// El serial va COMPLETO, sin enmascarar. Es la regla que no se puede relajar: en un bono
    /// virtual el comprobante es la única prueba física que se lleva el cliente para redimirlo
    /// —no hay plástico de respaldo— y enmascararlo lo dejaría inservible.
    /// </summary>
    [Fact]
    public void El_serial_del_bono_va_completo_sin_enmascarar()
    {
        var receipt = BuildReceipt();

        Assert.Contains(Serial, receipt, StringComparison.Ordinal);
        Assert.DoesNotContain("******", receipt, StringComparison.Ordinal);
    }

    /// <summary>La referencia es con lo que se rastrea la operación si el cliente reclama.</summary>
    [Fact]
    public void Lleva_la_referencia_de_la_operacion()
    {
        Assert.Contains("00136544716V", BuildReceipt(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Es el comprobante del CLIENTE, no el del comercio. En una activación el cliente se lleva
    /// el serial; el comercio no lo necesita completo.
    /// </summary>
    [Fact]
    public void Es_la_copia_del_cliente()
    {
        Assert.Contains("CLIENTE", BuildReceipt(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Se guarda como XML del contrato de HiPOS y no como texto plano: el día que exista el
    /// camino a la impresora, lo que está en disco ya es lo que se manda — sin volver a armarlo
    /// ni arriesgar que salga distinto de lo que el cajero vio.
    /// </summary>
    [Fact]
    public void Se_guarda_en_el_formato_que_la_impresora_espera()
    {
        var receipt = BuildReceipt();

        Assert.Contains("<Receipt", receipt, StringComparison.Ordinal);
        Assert.Contains("ReceiptLine", receipt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cuando Ogloba devuelve el enlace del bono virtual, viaja en el comprobante: es lo que
    /// resuelve un "no me llegó el correo" sin entrar al back office.
    /// </summary>
    [Fact]
    public void Incluye_el_enlace_del_bono_virtual_cuando_Ogloba_lo_devuelve()
    {
        const string url = "https://co-ts.ogloba.com/eGiftCard/KOAJ/tEDEbYGBtEkZRhF";

        Assert.Contains(url, BuildReceipt(url), StringComparison.Ordinal);
    }

    /// <summary>Sin enlace el comprobante sigue siendo válido: los bonos físicos no lo tienen.</summary>
    [Fact]
    public void Sin_enlace_el_comprobante_se_arma_igual()
    {
        var receipt = BuildReceipt(url: null);

        Assert.Contains(Serial, receipt, StringComparison.Ordinal);
        Assert.Contains("<Receipt", receipt, StringComparison.Ordinal);
    }
}
