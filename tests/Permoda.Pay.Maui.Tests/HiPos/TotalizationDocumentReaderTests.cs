using Permoda.Pay.Maui.HiPos.Requests;

namespace Permoda.Pay.Maui.Tests.HiPos;

/// <summary>
/// El XML de estos tests es el REAL: se capturó de la terminal B5AC009H02300343 el 2026-09-01,
/// del documento que HiPOS armó tras cobrar $210.100 con un bono. Los nombres de campo salen de
/// ahí, incluido <c>PaymenMeanName</c> sin la "t", que es como lo escribe ICG.
/// </summary>
public sealed class TotalizationDocumentReaderTests
{
    private const string RealDocument = """
        <Document>
          <PaymentMeans>
            <PaymentMean>
              <PaymentMeanField Key="PaymentMeanId">1000037</PaymentMeanField>
              <PaymentMeanField Key="Type">0</PaymentMeanField>
              <PaymentMeanField Key="LineNumber">1</PaymentMeanField>
              <PaymentMeanField Key="Description">OGLOBA</PaymentMeanField>
              <PaymentMeanField Key="PaymenMeanName">OGLOBA</PaymentMeanField>
              <PaymentMeanField Key="Amount">210100,0000</PaymentMeanField>
              <PaymentMeanField Key="CurrencyISOCode">COP</PaymentMeanField>
              <PaymentMeanField Key="TransactionId">6227905065</PaymentMeanField>
              <PaymentMeanField Key="AuthorizationId">6227905065</PaymentMeanField>
              <PaymentMeanField Key="CardNum">113817******9622</PaymentMeanField>
            </PaymentMean>
          </PaymentMeans>
        </Document>
        """;

    [Fact]
    public void FindOglobaPayment_ReadsReferenceFromAuthorizationId()
    {
        var line = TotalizationDocumentReader.FindOglobaPayment(RealDocument);

        Assert.NotNull(line);

        // La referencia va en AuthorizationId, NO en FreeField1 como sugiere el ejemplo de ICG.
        Assert.Equal("6227905065", line!.ReferenceNumber);
        Assert.Equal("210100,0000", line.AmountText);
        Assert.Equal("113817******9622", line.CardNumber);
    }

    /// <summary>
    /// El ejemplo de ICG identifica la línea comparando <c>PaymentMeanId</c> contra "OGLOBA".
    /// El id es un número (1000037), así que ese criterio no encuentra nada nunca. Este test
    /// fija que la identificación va por NOMBRE — que además es estable entre tiendas, mientras
    /// que el id lo asigna HioPosCloud.
    /// </summary>
    [Fact]
    public void FindOglobaPayment_IdentifiesByName_NotByNumericId()
    {
        var document = RealDocument.Replace("1000037", "9999999");

        var line = TotalizationDocumentReader.FindOglobaPayment(document);

        Assert.NotNull(line);
        Assert.Equal("6227905065", line!.ReferenceNumber);
    }

    /// <summary>
    /// Una venta pagada en efectivo también dispara TOTALIZATION_CANCELED. Ahí no hay nada que
    /// deshacer, y confundirlo con un error haría fallar cancelaciones perfectamente normales.
    /// </summary>
    [Fact]
    public void FindOglobaPayment_WithoutGiftCardLine_ReturnsNull()
    {
        var document = """
            <Document>
              <PaymentMeans>
                <PaymentMean>
                  <PaymentMeanField Key="PaymentMeanId">1</PaymentMeanField>
                  <PaymentMeanField Key="Description">EFECTIVO</PaymentMeanField>
                  <PaymentMeanField Key="Amount">599000,0000</PaymentMeanField>
                </PaymentMean>
              </PaymentMeans>
            </Document>
            """;

        Assert.Null(TotalizationDocumentReader.FindOglobaPayment(document));
    }

    /// <summary>
    /// Un pago mixto —bono + efectivo, que es el caso de la factura real— tiene que devolver la
    /// línea del bono y no la primera que aparezca.
    /// </summary>
    [Fact]
    public void FindOglobaPayment_WithMixedPayment_ReturnsTheGiftCardLine()
    {
        var document = """
            <Document>
              <PaymentMeans>
                <PaymentMean>
                  <PaymentMeanField Key="PaymentMeanId">1</PaymentMeanField>
                  <PaymentMeanField Key="Description">EFECTIVO</PaymentMeanField>
                  <PaymentMeanField Key="Amount">599000,0000</PaymentMeanField>
                </PaymentMean>
                <PaymentMean>
                  <PaymentMeanField Key="PaymentMeanId">1000037</PaymentMeanField>
                  <PaymentMeanField Key="Description">OGLOBA</PaymentMeanField>
                  <PaymentMeanField Key="AuthorizationId">6227905065</PaymentMeanField>
                  <PaymentMeanField Key="Amount">210100,0000</PaymentMeanField>
                </PaymentMean>
              </PaymentMeans>
            </Document>
            """;

        var line = TotalizationDocumentReader.FindOglobaPayment(document);

        Assert.NotNull(line);
        Assert.Equal("6227905065", line!.ReferenceNumber);
    }

    /// <summary>
    /// Una línea de bono sin referencia no se puede anular en Ogloba. Se ignora en vez de
    /// devolver una referencia vacía que haría fallar la llamada más adelante.
    /// </summary>
    [Fact]
    public void FindOglobaPayment_WithoutAuthorizationId_ReturnsNull()
    {
        var document = RealDocument.Replace(
            "<PaymentMeanField Key=\"AuthorizationId\">6227905065</PaymentMeanField>",
            string.Empty);

        Assert.Null(TotalizationDocumentReader.FindOglobaPayment(document));
    }

    /// <summary>
    /// HiPOS espera respuesta pase lo que pase. Un documento ilegible no puede propagarse como
    /// excepción hasta dejar al POS esperando.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es xml")]
    [InlineData("<Document><PaymentMeans>")]
    public void FindOglobaPayment_WithUnusableInput_ReturnsNullWithoutThrowing(string? input)
    {
        Assert.Null(TotalizationDocumentReader.FindOglobaPayment(input));
    }
}
