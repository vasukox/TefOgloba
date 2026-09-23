using Microsoft.Extensions.Logging.Abstractions;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Maui.HiPos;
using Permoda.Pay.Maui.HiPos.Requests;

namespace Permoda.Pay.Maui.Tests.HiPos;

public sealed class HiPosPaymentOrchestratorTests
{
    [Fact]
    public async Task HandleTransactionAsync_SaleDispatch_CallsProcessSaleHandler()
    {
        var (provider, calls) = CreateRecording();
        provider.RedemptionResults.Enqueue(ApprovedRedemption());
        provider.ConfirmationResults.Enqueue(SuccessUnit());
        provider.ReconciliationResults.Enqueue(SuccessUnit());

        var scenario = BuildScenario(provider, calls);
        var extras = SaleExtras();

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new NeverCalledCapture(),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        // SALE es facturar: el cliente paga, y pagar con un bono es descontarle saldo. Cuando el
        // intent ya trae el serial no hace falta levantar la app — se cobra directo.
        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);
        Assert.Contains("provider.redeem", scenario.Calls);
        Assert.DoesNotContain("provider.activate", scenario.Calls);
        Assert.Contains("provider.confirm", scenario.Calls);
        Assert.Contains("provider.reconcile", scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_SaleWithoutCardNumber_OpensRedeemInTheModule()
    {
        // HiPOS nunca manda el serial: los campos de entrada de un SALE no incluyen CardNumber.
        // El módulo se levanta en Redimir, hace la operación y devuelve el resultado ya cerrado
        // — por eso el orquestrador no toca a Ogloba en este camino.
        var (provider, calls) = CreateRecording();

        var scenario = BuildScenario(provider, calls);
        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);
        var capture = new StubCapture("1138170025515937");

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: capture,
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, capture.Calls);
        Assert.Equal(HiPosSaleIntent.Redemption, capture.SuggestedIntent);
        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);
        Assert.Empty(scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_AdvancedPayment_AlsoOpensRedeem()
    {
        // Un abono llega como SALE con IsAdvancedPayment. Medido en terminal, el POS lo manda
        // igual que una venta, así que ambos entran por Redimir.
        var (provider, calls) = CreateRecording();

        var scenario = BuildScenario(provider, calls);
        var capture = new StubCapture("1138170025515937");

        await scenario.Orchestrator.HandleTransactionAsync(
            AdvancedPaymentExtras(),
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: capture,
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(HiPosSaleIntent.Redemption, capture.SuggestedIntent);
        Assert.Empty(scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_PartialPayment_ReportsWhatWasActuallyApplied()
    {
        // El bono no alcanzaba para la venta y se cobró lo que tenía. HiPOS necesita saber
        // cuánto se aplicó para pedir la diferencia por otro medio: devolverle el importe
        // pedido daría la factura por pagada sin estarlo.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var partialExtras = SaleExtras();
        partialExtras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            partialExtras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: true,
                Pending: false,
                AmountAppliedPesos: 30_000,
                Reference: "00136544716V",
                CardNumber: "1138170025515937")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);

        // De vuelta en la escala del contrato: los dos últimos dígitos son decimales.
        Assert.Equal("3000000", scenario.Sink.StringExtras["Amount"]);

        // El importe del bono viaja UNA vez. Mandar además FixedPaymentMeanAmount hace que HiPOS
        // lo cuente dos veces: medido en terminal el 2026-09-08 con una factura de $67.800 y un
        // bono de $50.000, el POS registró $100.000 entregados y pintó la línea de EFECTIVO en
        // -$32.200. El cajero veía una devolución que no existe.
        Assert.DoesNotContain("FixedPaymentMeanAmount", scenario.Sink.StringExtras.Keys);
        Assert.DoesNotContain("FixedPaymentMeanId", scenario.Sink.StringExtras.Keys);
    }

    /// <summary>
    /// La suma de lo que HiPOS recibe no puede pasarse del total de la factura. Es la propiedad
    /// que se rompió, dicha en términos de lo que el cajero ve: si lo entregado supera el total,
    /// el POS pinta una devolución en negativo.
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_PartialPayment_DoesNotOverstateWhatWasDelivered()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        // Factura de $67.800 cubierta con un bono de $50.000 — el caso exacto de la terminal.
        var extras = BuildExtras("SALE", amount: "6780000", tenderType: "CREDIT");
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: true,
                Pending: false,
                AmountAppliedPesos: 50_000,
                Reference: "00136544716V",
                CardNumber: "1138170025515937")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var declared = long.Parse(scenario.Sink.StringExtras["Amount"]!);

        // 50.000 en la escala del contrato, ni un peso más.
        Assert.Equal(5_000_000, declared);
        Assert.True(
            declared < 6_780_000,
            "Lo declarado no puede llegar al total de la factura: el bono solo cubrió una parte.");

        // Y por ninguna otra vía se vuelve a declarar el mismo importe.
        Assert.DoesNotContain("FixedPaymentMeanAmount", scenario.Sink.StringExtras.Keys);
    }

    /// <summary>
    /// El medio de pago NUNCA viaja vacío. Es lo que hace que la venta se imprima como OGLOBA y
    /// no como otra cosa.
    /// <para>
    /// Este campo ya se equivocó en las dos direcciones. Primero se puso "2" —el medio de
    /// Sistecrédito— y HiPOS aplicó el pago sobre ESE medio: "la forma de pago tarjeta de crédito
    /// no tiene equivalencia". De ahí se concluyó dejarlo vacío, asumiendo que vacío significaba
    /// "deja la línea que HiPOS ya eligió". No significa eso: con el id vacío HiPOS aplica sobre
    /// su medio por defecto y <b>la venta se imprime como EFECTIVO</b> — reportado desde caja el
    /// 2026-09-08, con el bono ya cobrado.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_WhenHiPosOmitsPaymentMean_UsesOglobaAndNeverEmpty()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        // HiPOS no manda PaymentMeanId en el intent de venta: la lista PaymentMeans del documento
        // todavía está vacía cuando nos llama. Es el caso normal, no el raro.
        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);
        extras.Remove("PaymentMeanId");
        extras.Remove("FixedPaymentMeanId");

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var document = System.Xml.Linq.XDocument.Parse(
            scenario.Sink.StringExtras["ModifyDocumentResult"]!);

        // El id viaja como <PaymentMeanField Key="PaymentMeanId">, no como un elemento con ese
        // nombre. Buscarlo por nombre de elemento no encuentra nada y el test pasaría en falso.
        var paymentMeanId = document.Descendants("PaymentMeanField")
            .FirstOrDefault(field => (string?)field.Attribute("Key") == "PaymentMeanId")
            ?.Value;

        Assert.False(
            string.IsNullOrWhiteSpace(paymentMeanId),
            "Sin PaymentMeanId, HiPOS aplica el pago sobre su medio por defecto y la venta se "
            + "imprime como EFECTIVO.");

        Assert.Equal(HiPosPaymentOrchestrator.OglobaPaymentMeanId, paymentMeanId);

        // Y nunca el de otro módulo: "2" es el medio de Sistecrédito. Esa parte de D-05 sigue.
        Assert.NotEqual("2", paymentMeanId);
    }

    [Fact]
    public async Task HandleTransactionAsync_SendsModifyDocumentResultAsXmlWithOnlyPaymentMeans()
    {
        // ModifyDocumentResult enriquece el MEDIO DE PAGO del documento con el AuthorizationId.
        // Antes se mandaba el número de línea pelado, y solo si HiPOS enviaba
        // PaymentMeanLineNumber —que no lo envía—, así que no salía nunca.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var xml = scenario.Sink.StringExtras["ModifyDocumentResult"];
        Assert.False(string.IsNullOrWhiteSpace(xml));

        var document = System.Xml.Linq.XDocument.Parse(xml!);

        // SOLO PaymentMeans. Si el resultado se parece a un documento nuevo, HioPosCloud lo
        // reenvía a la DIAN — factura duplicada.
        Assert.Equal("ModifyDocumentResult", document.Root!.Name.LocalName);
        Assert.Equal(["PaymentMeans"], document.Root.Elements().Select(e => e.Name.LocalName));

        foreach (var forbidden in new[]
                 {
                     "Lines", "Line", "Header", "Totals", "Total", "Company", "Customer",
                     "SerialNumber", "Number", "DocumentNumber"
                 })
        {
            Assert.Empty(document.Descendants(forbidden));
        }

        // El AuthorizationId sí viaja: es lo único que el módulo fiscal necesita de nosotros.
        Assert.Contains(
            document.Descendants("PaymentMeanField"),
            field => (string?)field.Attribute("Key") == "AuthorizationId");
    }

    /// <summary>
    /// No se nombra el medio de pago de OTRO módulo. La regla que sí sigue vigente de D-05.
    /// <para>
    /// Costó una venta rota en terminal (2026-08-28): se puso "2" —el medio "Tarjeta" que usa
    /// Sistecrédito en estas mismas terminales— y HiPOS aplicó el cobro sobre ESE medio, que no
    /// tiene equivalencia DIAN configurada. El POS respondió "la forma de pago tarjeta de crédito
    /// no tiene equivalencia".
    /// </para>
    /// <para>
    /// Lo que <b>cambió</b> el 2026-09-08: de ese episodio se concluyó no mandar ningún id. Esa
    /// conclusión era falsa. Sin id, HiPOS aplica el pago sobre su medio por defecto y la venta
    /// se imprime como EFECTIVO — reportado desde caja, con el bono ya cobrado. Ahora se manda el
    /// de Ogloba, que es el nuestro y está medido. Ver
    /// <see cref="HandleTransactionAsync_WhenHiPosOmitsPaymentMean_UsesOglobaAndNeverEmpty"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_NeverNamesAnotherModulesPaymentMean()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);
        Assert.DoesNotContain("PaymentMeanId", extras.Keys);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var document = System.Xml.Linq.XDocument.Parse(
            scenario.Sink.StringExtras["ModifyDocumentResult"]!);

        var paymentMeanId = document.Descendants("PaymentMeanField")
            .First(field => (string?)field.Attribute("Key") == "PaymentMeanId")
            .Value;

        // Ni el de Sistecrédito ni ningún otro ajeno: el de Ogloba.
        Assert.NotEqual("2", paymentMeanId);
        Assert.Equal(HiPosPaymentOrchestrator.OglobaPaymentMeanId, paymentMeanId);

        // FixedPaymentMeanId es la otra vía por la que se nombra un medio, y solo aplica cuando
        // el bono cubrió PARTE de la venta. Este cobro fue completo: no debe salir.
        Assert.DoesNotContain("FixedPaymentMeanId", scenario.Sink.StringExtras.Keys);
    }

    [Fact]
    public async Task HandleTransactionAsync_WhenHiPosNamesThePaymentMean_EchoesThatOne()
    {
        // La contracara: si el POS sí nos dice sobre qué medio aplicar, se respeta ese y no otro.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);
        extras["PaymentMeanId"] = "17";

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var document = System.Xml.Linq.XDocument.Parse(
            scenario.Sink.StringExtras["ModifyDocumentResult"]!);

        Assert.Equal(
            "17",
            document.Descendants("PaymentMeanField")
                .Single(field => (string?)field.Attribute("Key") == "PaymentMeanId")
                .Value);
    }

    /// <summary>
    /// En un pago parcial el importe del bono se declara UNA sola vez.
    /// <para>
    /// Este test decía lo contrario: que había que mandar además
    /// <c>FixedPaymentMeanId</c>/<c>FixedPaymentMeanAmount</c> para que el POS bajara el medio a
    /// lo que el bono cubrió. La idea era razonable y nunca se pudo comprobar, porque el par
    /// exigía un <c>PaymentMeanId</c> no vacío y en producción ese id siempre iba vacío: el
    /// camino jamás se ejecutó fuera de esta prueba.
    /// </para>
    /// <para>
    /// El 2026-09-08, al empezar a mandar el id real de Ogloba, el camino se despertó y se midió
    /// en terminal: factura de $67.800 con un bono de $50.000, HiPOS registró <b>$100.000</b>
    /// entregados y pintó la línea de EFECTIVO en <b>-$32.200</b>. El POS ya tenía el importe por
    /// el extra <c>Amount</c> y por la línea de <c>ModifyDocumentResult</c>; el par Fixed* lo
    /// sumaba una tercera vez.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_WhenTheGiftCardCoversOnlyPartOfTheBill_DeclaresTheAmountOnce()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("SALE", amount: "5000000", tenderType: "CREDIT");
        extras["PaymentMeanId"] = "17";

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: true,
                Pending: false,
                AmountAppliedPesos: 30_000,
                Reference: "00136544716V",
                CardNumber: "1138170025515937")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);

        // Lo aplicado, una vez, en la escala del contrato.
        Assert.Equal("3000000", scenario.Sink.StringExtras["Amount"]);

        // Y por ninguna otra vía. Es lo que producía el negativo en pantalla.
        Assert.DoesNotContain("FixedPaymentMeanAmount", scenario.Sink.StringExtras.Keys);
        Assert.DoesNotContain("FixedPaymentMeanId", scenario.Sink.StringExtras.Keys);

        // El medio sigue siendo el que HiPOS nombró: esto no cambia con el arreglo.
        var xml = System.Xml.Linq.XDocument.Parse(scenario.Sink.StringExtras["ModifyDocumentResult"]!);

        Assert.Equal(
            "17",
            xml.Descendants("PaymentMeanField")
                .Single(field => (string?)field.Attribute("Key") == "PaymentMeanId")
                .Value);
    }

    [Fact]
    public async Task HandleTransactionAsync_WhenTheGiftCardCoversTheWholeBill_DoesNotFixThePaymentMeanAmount()
    {
        // La contracara: si el bono alcanzó, no hay nada que corregir. Mandar FixedPaymentMean
        // igual sería pisar el importe del medio con el mismo valor sin motivo.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("SALE", amount: "5000000", tenderType: "CREDIT");

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: true,
                Pending: false,
                AmountAppliedPesos: 50_000,
                Reference: "00136544716V",
                CardNumber: "1138170025515937")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.DoesNotContain("FixedPaymentMeanId", scenario.Sink.StringExtras.Keys);
        Assert.DoesNotContain("FixedPaymentMeanAmount", scenario.Sink.StringExtras.Keys);
    }

    [Fact]
    public async Task HandleTransactionAsync_WhenModuleReportsFailure_TellsHiPosWhy()
    {
        // El motivo del rechazo lo produce el módulo —que ya se lo mostró al cajero— y viaja a
        // HiPOS tal cual, en vez de una alerta genérica de "no se puede pagar con el bono".
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var failedExtras = SaleExtras();
        failedExtras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            failedExtras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: false,
                Pending: false,
                AmountAppliedPesos: 0,
                ErrorMessage: "73: Wrong activation amount")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        // El rechazo se responde por SetResultOk con TransactionResult=FAILED, no por
        // SetResultFailed: HiPOS necesita igual el eco del TransactionType y el comprobante de
        // error para cerrar el documento e imprimirle algo al cliente.
        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Equal("73: Wrong activation amount", scenario.Sink.StringExtras["ErrorMessage"]);
        Assert.Equal("SALE", scenario.Sink.StringExtras["TransactionType"]);
        Assert.NotNull(scenario.Sink.StringExtras["CustomerReceipt"]);
        Assert.Empty(scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_EchoesTheTransactionTypeAndNeverSendsFiscalNumbering()
    {
        // Dos reglas del contrato fiscal de ICG, juntas porque se rompen juntas:
        //
        // 1. El TransactionType se devuelve TAL CUAL llegó. Contestarle un tipo distinto hacía
        //    que el POS no diera la operación por cerrada y relanzara el intent — se veía como
        //    "error de módulo externo" en la entrada de caja.
        // 2. El módulo NO aporta numeración fiscal. El consecutivo, el rango y la resolución
        //    DIAN son de HiPOS; si el módulo externo los manda, chocan con el rango del POS y
        //    sale "faltan los datos de rango de numeración de factura".
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = NegativeSaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal("SALE", scenario.Sink.StringExtras["TransactionType"]);

        // Lo que HiPOS imprime. Sin esto no sale nada por la impresora.
        Assert.NotNull(scenario.Sink.StringExtras["CustomerReceipt"]);

        // UNA sola tirilla. Mandar también MerchantReceipt hacía que la térmica sacara dos, y
        // las dos con el encabezado "COPIA COMERCIO".
        Assert.DoesNotContain("MerchantReceipt", scenario.Sink.StringExtras.Keys);

        // Ni un solo campo de numeración fiscal sale del módulo.
        foreach (var forbidden in new[]
                 {
                     "ResolutionNumber", "InvoiceNumber", "DocumentNumber",
                     "Serie", "Number", "RangeFrom", "RangeTo"
                 })
        {
            Assert.DoesNotContain(forbidden, scenario.Sink.StringExtras.Keys);
        }
    }

    [Fact]
    public async Task HandleTransactionAsync_SanitizesTheAuthorizationIdForTheFiscalModule()
    {
        // AuthorizationId es el único campo del TEF que llega a la DIAN. Va sin separadores y
        // recortado a 40, o el fiscal rechaza el documento.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(new HiPosOperationOutcome(
                Accepted: true,
                Pending: false,
                AmountAppliedPesos: 50_000,
                Reference: "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
                CardNumber: "1138170025515937")),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        var authorizationId = scenario.Sink.StringExtras["AuthorizationId"];

        Assert.Equal("3f2504e04f8911d39a0c0305e82c3301", authorizationId);
        Assert.DoesNotContain('-', authorizationId!);
        Assert.True(authorizationId!.Length <= 40);
    }

    [Fact]
    public async Task HandleTransactionAsync_CashEntry_IsRejectedBecauseActivationIsManual()
    {
        // Entrada de caja: llega como SALE sin tender. Activar bonos dejó de entrar por HiPOS —
        // es un flujo manual con su propia sesión de cajero (usuario y contraseña), y atenderlo
        // desde el intent significaría activar sin esa autenticación.
        //
        // No se abre el módulo ni se toca a Ogloba: se le responde a HiPOS con el motivo y con
        // comprobante, para que el cajero vea en el POS qué tiene que hacer.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = NegativeSaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new NeverCalledCapture(),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Contains("Activar bono", scenario.Sink.StringExtras["ErrorMessage"]);
        Assert.Equal("SALE", scenario.Sink.StringExtras["TransactionType"]);
        Assert.Empty(scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_WhenCashierCancelsCapture_DoesNotChargeAnything()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);
        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(null),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        // Lo importante: no se llamó a Ogloba y aun así se le respondió a HiPOS.
        // SetResultCanceled (no SetResultFailed): el cajero salió del módulo sin operar, así
        // que HiPOS debe tratarlo como "cancelado" y devolverlo a la pantalla de selección de
        // medio de pago, no abortar el flujo. Ver docs/OPERACION_Y_SOPORTE.md §3.
        Assert.Empty(scenario.Calls);
        Assert.Single(scenario.Sink.CanceledCalls);
        Assert.Empty(scenario.Sink.FailedCalls);
        Assert.Empty(scenario.Sink.SetOkResults);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    /// <summary>
    /// Salir con "Volver a HiPOS" no es un error, y el POS no debe anunciarlo como uno.
    /// <para>
    /// El intent de cancelación viajaba vacío. Sin el eco de <c>TransactionType</c>, HiPOS no
    /// puede casar la respuesta con la operación que lanzó y muestra su alerta genérica de
    /// "error en módulo externo" — sobre una salida que el propio cajero pidió. Se le manda el
    /// motivo redactado para que tenga qué mostrar en su lugar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_WhenCashierCancelsCapture_ExplainsTheExitToHiPos()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);
        var extras = SaleExtras();
        extras.Remove(HiPosRequestMapper.CardNumberProperty);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture(null),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal("SALE", scenario.Sink.CanceledExtras["TransactionType"]);
        Assert.Equal("CANCELED", scenario.Sink.CanceledExtras["TransactionResult"]);

        // El texto es lo que el cajero lee con el cliente en frente: dice en qué quedó la venta
        // y qué hacer, sin llamarle error a algo que no lo es.
        var title = scenario.Sink.CanceledExtras["ErrorMessageTitle"];
        var message = scenario.Sink.CanceledExtras["ErrorMessage"];

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("error", title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("venta sigue abierta", message, StringComparison.OrdinalIgnoreCase);

        // Y sigue siendo una CANCELACIÓN, no un RESULT_OK: mandar OK acá devolvía al cajero al
        // launcher en vez de a la pantalla de medios de pago.
        Assert.Empty(scenario.Sink.SetOkResults);
        Assert.Single(scenario.Sink.CanceledCalls);
    }

    [Fact]
    public void HandleVersion_SendsVersionAsIntegerAndFinishes()
    {
        // HioPos lee Version con getIntExtra: como String obtiene -1 y pide reinstalar el módulo
        // en cada arranque. Y sin FinishActivity el resultado nunca le llega.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        scenario.Orchestrator.HandleVersion(scenario.Sink);

        Assert.Equal(
            HiPosPaymentOrchestrator.ModuleVersionCode,
            scenario.Sink.IntExtras[HiPosPaymentOrchestrator.VersionExtra]);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    [Fact]
    public void HandleUnsupportedCardOperation_ReturnsCanceledAndFinishes()
    {
        // El módulo redime bonos, no opera tarjetas bancarias: ICG espera Canceled.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        scenario.Orchestrator.HandleUnsupportedCardOperation("READ_CARD", scenario.Sink);

        Assert.Single(scenario.Sink.CanceledCalls);
        Assert.Empty(scenario.Sink.SetOkResults);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    [Fact]
    public void HandleCustomParams_SendsNameAndLogoBytes()
    {
        // El logo es el único extra binario del contrato: PNG crudo, ni ruta ni Base64.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        scenario.Orchestrator.HandleCustomParams(scenario.Sink, png);

        Assert.Equal(["OK"], scenario.Sink.SetOkResults);
        Assert.Equal(
            HiPosPaymentOrchestrator.ModuleDisplayName,
            scenario.Sink.StringExtras[HiPosPaymentOrchestrator.NameExtra]);
        Assert.Equal(png, scenario.Sink.ByteExtras[HiPosPaymentOrchestrator.LogoExtra]);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    [Fact]
    public void HandleCustomParams_WithoutLogo_StillAnswersWithName()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        scenario.Orchestrator.HandleCustomParams(scenario.Sink, logoPng: null);

        Assert.Equal(["OK"], scenario.Sink.SetOkResults);
        Assert.Empty(scenario.Sink.ByteExtras);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    [Fact]
    public void HandleBehavior_DeclaresFlagsAsBooleansWithContractLiterals()
    {
        // Las flags van como booleanos nativos del Intent (HiPOS las lee con getBooleanExtra) y
        // `canAudit` arranca en minúscula mientras el resto es PascalCase. Si el nombre o el tipo
        // no calzan, HioPos no falla: descarta el modulo y la forma de pago no aparece.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        scenario.Orchestrator.HandleBehavior(scenario.Sink);

        var extras = scenario.Sink.BoolExtras;

        // SupportsCredit es la que habilita la forma de pago: sin ella el TEF no aparece.
        Assert.True(extras["SupportsCredit"]);
        Assert.True(extras["HasCustomParams"]);
        Assert.True(extras["canAudit"]);
        Assert.DoesNotContain("CanAudit", extras.Keys);

        // La anulación es lo que sostiene la papelera de HiPOS sobre la línea de pago del bono.
        // En false, HiPOS congelaba esa línea y una factura que no integraba con la DIAN dejaba
        // la caja trabada con el saldo ya descontado.
        Assert.True(extras["SupportsTransactionVoid"]);

        // Devolución parcial sigue fuera del alcance: anular suelta la línea completa.
        Assert.False(extras["SupportsPartialRefund"]);

        Assert.True(extras["CallOnTotalizationCanceled"]);

        // DEBE ir en false. En true, HiPOS manda VOID_TRANSACTION en lugar de REFUND para un
        // abono del total en la Z actual — y las notas de crédito dejan de pasar por el camino
        // que las rechaza, colándose como ACCEPTED en silencio. Medido en terminal el
        // 2026-09-02: cuatro abonos dados por hechos sin mover un peso.
        Assert.False(extras["ExecuteVoidWhenAvailable"]);

        // QUERY_TRANSACTION es recuperar una venta con resultado incierto, no consultar saldo.
        // Declararlo en true con nuestra semántica actual haría que HiPOS diera por cobradas
        // ventas que nunca se cobraron.
        Assert.False(extras["SupportsTransactionQuery"]);

        // El documento de venta debe llegar inline: con true, el scoped storage lo deja nulo.
        Assert.False(extras["OnlyUseDocumentPath"]);

        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    [Theory]
    [InlineData(nameof(HiPosPaymentOrchestrator.HandleBehavior))]
    [InlineData(nameof(HiPosPaymentOrchestrator.HandlePrintInfo))]
    [InlineData(nameof(HiPosPaymentOrchestrator.HandleSetupScreen))]
    public void QueryHandlers_AlwaysRespondAndFinish(string handler)
    {
        // Regla dura del contrato: ningún handler puede terminar sin devolver resultado, o HioPos
        // se queda esperando indefinidamente y el cajero pierde la venta.
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        switch (handler)
        {
            case nameof(HiPosPaymentOrchestrator.HandleBehavior):
                scenario.Orchestrator.HandleBehavior(scenario.Sink);
                break;
            case nameof(HiPosPaymentOrchestrator.HandlePrintInfo):
                scenario.Orchestrator.HandlePrintInfo(scenario.Sink);
                break;
            default:
                scenario.Orchestrator.HandleSetupScreen(scenario.Sink);
                break;
        }

        Assert.Equal(["OK"], scenario.Sink.SetOkResults);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    /// <summary>
    /// REFUND —"abono de una transacción existente" en la API TEF 4.0— y VOID_TRANSACTION son
    /// los intents con que HiPOS pide soltar una línea de pago ya cobrada.
    /// <para>
    /// NO se llama a Ogloba. Decisión de producto de KOAJ (2026-09-01): quitar la línea es una
    /// acción del POS; lo que haya que hacer con el saldo del bono va por el flujo de anulación
    /// de venta, que es otro camino. Este test fija esa decisión — si alguien vuelve a conectar
    /// el provider acá, falla y obliga a revisarlo con KOAJ antes.
    /// </para>
    /// </summary>
    /// <summary>
    /// Un cobro con bono NO se deshace desde el POS, ni por REFUND (nota de crédito) ni por
    /// VOID_TRANSACTION (anulación). Los DOS se rechazan.
    /// <para>
    /// Cerrar los dos es lo que hace real la prohibición. Cuando solo se cerraba REFUND, prender
    /// <c>ExecuteVoidWhenAvailable</c> hizo que HiPOS mandara los abonos como VOID_TRANSACTION y
    /// se colaron cuatro en silencio (2026-09-02). Este test existe para que no vuelva a quedar
    /// una sola puerta abierta.
    /// </para>
    /// </summary>
    /// <summary>
    /// Documento con el tipo y el importe neto que se quieran. El <c>NetAmount</c> por defecto
    /// es POSITIVO: una venta en curso.
    /// </summary>
    private static string SaleDocument(string documentTypeId = "2", string netAmount = "119700,0000") => $"""
        <Document>
          <Header>
            <HeaderField Key="DocumentTypeId">{documentTypeId}</HeaderField>
            <HeaderField Key="NetAmount">{netAmount}</HeaderField>
          </Header>
        </Document>
        """;

    /// <summary>
    /// El abono REAL capturado en la caja 037 T.KOAJ CALLE 18 MONTEVIDEO el 2026-09-08, con los
    /// campos que importan tal como llegaron. Este documento se ACEPTÓ en producción —el abono
    /// se hizo— porque el 28 no estaba en la lista de tipos conocidos.
    /// </summary>
    private const string RealAbonoDocument = """
        <Document>
          <Header>
            <HeaderField Key="Serie">K50H</HeaderField>
            <HeaderField Key="Number">117</HeaderField>
            <HeaderField Key="DocumentTypeId">28</HeaderField>
            <HeaderField Key="TaxesAmount">-19112,0000</HeaderField>
            <HeaderField Key="NetAmount">-119700,0000</HeaderField>
          </Header>
        </Document>
        """;

    /// <summary>
    /// El caso que se nos coló: un abono con un <c>DocumentTypeId</c> que el contrato de ICG no
    /// enumera. Se rechaza por el SIGNO del importe, que es lo que no depende de conocer de
    /// antemano cada número que HiPOS decida usar.
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_RealAbonoWithUnlistedDocumentType_IsRejected()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("REFUND", amount: "11970000", referenceNumber: "4930485064");
        extras["DocumentData"] = RealAbonoDocument;

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Equal("FAILED", scenario.Sink.StringExtras["TransactionResult"]);
        Assert.Equal("REFUND", scenario.Sink.StringExtras["TransactionType"]);
        Assert.Contains(
            "otro medio de pago",
            scenario.Sink.StringExtras["ErrorMessage"],
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("provider.void", scenario.Calls);
        Assert.Empty(provider.VoidRequests);
    }

    /// <summary>
    /// El signo manda por encima del tipo: un documento cuyo tipo parece de venta pero cuyo neto
    /// es negativo devuelve plata, y no se acepta. Es lo que cubre los tipos que todavía no
    /// conocemos.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("99")]
    public async Task HandleTransactionAsync_RefundWithNegativeNetAmount_IsRejected(string documentTypeId)
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("REFUND", amount: "50000", referenceNumber: "00136544716V");
        extras["DocumentData"] = SaleDocument(documentTypeId, netAmount: "-50000,0000");

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.DoesNotContain("provider.void", scenario.Calls);
    }

    /// <summary>
    /// REFUND sobre un documento de VENTA = el cajero está quitando la línea de pago del bono.
    /// Se acepta para que HiPOS pueda desmarcarla, y NO se llama a Ogloba (decisión de KOAJ
    /// confirmada con ICG el 2026-09-03: el saldo se resuelve por la anulación de venta).
    /// </summary>
    [Theory]
    [InlineData("1")]   // Tiquet
    [InlineData("2")]   // Factura
    public async Task HandleTransactionAsync_RefundOnSaleDocument_ReleasesLineWithoutCallingOgloba(
        string documentTypeId)
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("REFUND", amount: "50000", referenceNumber: "00136544716V");
        extras["DocumentData"] = SaleDocument(documentTypeId);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);
        Assert.Equal("ACCEPTED", scenario.Sink.StringExtras["TransactionResult"]);
        Assert.Equal("REFUND", scenario.Sink.StringExtras["TransactionType"]);

        Assert.DoesNotContain("provider.void", scenario.Calls);
        Assert.Empty(provider.VoidRequests);
    }

    /// <summary>
    /// REFUND sobre un documento de ABONO (3 tiquet, 4 factura) = NOTA DE CRÉDITO. Se rechaza.
    /// <para>
    /// Es el único dato que separa los dos motivos por los que llega REFUND. Sin esta distinción
    /// habría que elegir entre trabar la caja o dejar pasar notas de crédito en silencio.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("3")]   // Abono tiquet
    [InlineData("4")]   // Abono factura
    public async Task HandleTransactionAsync_RefundOnCreditNoteDocument_IsRejected(string documentTypeId)
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = BuildExtras("REFUND", amount: "50000", referenceNumber: "00136544716V");
        extras["DocumentData"] = SaleDocument(documentTypeId);

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Contains("no admiten notas de crédito", scenario.Sink.StringExtras["ErrorMessage"]);
    }

    /// <summary>
    /// VOID_TRANSACTION sigue rechazado. Solo llega tras un UNKNOWN_RESULT, y aceptarlo sin
    /// verificar contra Ogloba es lo que dejó pasar cuatro abonos por hechos el 2026-09-02.
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_VoidTransaction_IsStillRejected()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        await scenario.Orchestrator.HandleTransactionAsync(
            BuildExtras("VOID_TRANSACTION", amount: "50000", referenceNumber: "00136544716V"),
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.DoesNotContain("provider.void", scenario.Calls);
    }

    /// <summary>
    /// Una NOTA CRÉDITO contra Ogloba se rechaza con un motivo legible, sin abrir el módulo.
    /// <para>
    /// HiPOS las manda como <c>REFUND</c>. Este test existe para impedir que ese caso vuelva a
    /// responder ACCEPTED: durante unas horas del 2026-09-01 lo hacía, y eso daba la nota crédito
    /// por pagada sin que se moviera un peso en Ogloba.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_CreditNote_IsRejectedWithReason()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        var extras = RefundExtras();
        extras["DocumentData"] = SaleDocument("4");   // Abono factura = nota de crédito

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        // Los nombres son los DEL CONTRATO (API TEF 4.0 §Transaction). Con otros, HiPOS no lo
        // lee como transacción fallida y el cajero no ve ningún aviso: la operación no pasa pero
        // nadie le dice por qué.
        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Equal("FAILED", scenario.Sink.StringExtras["TransactionResult"]);
        Assert.Equal("Forma de pago no válida", scenario.Sink.StringExtras["ErrorMessageTitle"]);
        Assert.Contains("no admiten notas de crédito", scenario.Sink.StringExtras["ErrorMessage"]);

        // Eco del tipo: sin él el POS no da la operación por cerrada y relanza el intent.
        Assert.Equal("REFUND", scenario.Sink.StringExtras["TransactionType"]);

        // No se toca a Ogloba y la Activity cierra: HiPOS no puede quedarse esperando.
        Assert.Empty(scenario.Calls);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    /// <summary>
    /// REFUND SIN documento legible: se RECHAZA.
    /// <para>
    /// Sin <c>DocumentTypeId</c> no hay forma de saber si el cajero está quitando la línea de
    /// pago o haciendo un abono, y un abono con bono Ogloba no se hace nunca. Ante la duda no se
    /// acepta: aceptar "por si acaso" era la única rendija que quedaba abierta.
    /// </para>
    /// <para>
    /// Cerrarla no cuesta la papelera. Cuando el cajero la aprieta, el documento que viaja es la
    /// venta en curso y llega entero — medido en terminal, <c>DocumentData</c> con 15.236
    /// caracteres. Este caso no se ha observado nunca en ese camino.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_RefundWithoutDocument_IsRejected()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        await scenario.Orchestrator.HandleTransactionAsync(
            RefundExtras(),
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Equal("FAILED", scenario.Sink.StringExtras["TransactionResult"]);
        Assert.Equal("REFUND", scenario.Sink.StringExtras["TransactionType"]);

        // El cajero tiene que poder seguir vendiendo: se le dice qué hacer, no solo que no.
        Assert.Contains(
            "otro medio de pago",
            scenario.Sink.StringExtras["ErrorMessage"],
            StringComparison.OrdinalIgnoreCase);

        // Y sobre todo: no se tocó a Ogloba.
        Assert.DoesNotContain("provider.void", scenario.Calls);
    }

    /// <summary>
    /// Sin la referencia original tampoco se acepta. La referencia solo sirve para trazar; que
    /// falte no vuelve válida una operación que no lo es.
    /// </summary>
    [Fact]
    public async Task HandleTransactionAsync_UndoWithoutReference_IsStillRejected()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        await scenario.Orchestrator.HandleTransactionAsync(
            BuildExtras("VOID_TRANSACTION", amount: "50000"),
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["FAILED"], scenario.Sink.SetOkResults);
        Assert.Equal("FAILED", scenario.Sink.StringExtras["TransactionResult"]);
        Assert.DoesNotContain("provider.void", scenario.Calls);
    }

    private const string CanceledDocument = """
        <Document>
          <PaymentMeans>
            <PaymentMean>
              <PaymentMeanField Key="PaymentMeanId">1000037</PaymentMeanField>
              <PaymentMeanField Key="Description">OGLOBA</PaymentMeanField>
              <PaymentMeanField Key="AuthorizationId">00136544716V</PaymentMeanField>
              <PaymentMeanField Key="Amount">210100,0000</PaymentMeanField>
            </PaymentMean>
          </PaymentMeans>
        </Document>
        """;

    /// <summary>
    /// TOTALIZATION_CANCELED con un cobro de bono hecho: se acepta sin tocar Ogloba, misma
    /// decisión que en REFUND.
    /// </summary>
    [Fact]
    public async Task HandleTotalizationCanceledAsync_WithGiftCardPayment_AcceptsWithoutCallingOgloba()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        await scenario.Orchestrator.HandleTotalizationCanceledAsync(
            CanceledDocument,
            storeId: "K00036",
            terminalId: "CAJA-01",
            cashierId: "10457",
            scenario.Sink,
            CancellationToken.None);

        Assert.DoesNotContain("provider.void", scenario.Calls);
        Assert.Empty(scenario.Sink.FailedCalls);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }

    /// <summary>
    /// Este intent llega en TODA totalización cancelada, se haya pagado con bono o no. Sin línea
    /// de bono se responde OK igual: fallar acá haría que cancelar una venta en efectivo diera
    /// error.
    /// </summary>
    [Fact]
    public async Task HandleTotalizationCanceledAsync_WithoutGiftCardPayment_Accepts()
    {
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);

        await scenario.Orchestrator.HandleTotalizationCanceledAsync(
            "<Document><PaymentMeans /></Document>",
            storeId: "K00036",
            terminalId: "CAJA-01",
            cashierId: "10457",
            scenario.Sink,
            CancellationToken.None);

        Assert.DoesNotContain("provider.void", scenario.Calls);
        Assert.Empty(scenario.Sink.FailedCalls);
        Assert.Equal(1, scenario.Sink.FinishCalls);
    }


    [Fact]
    public async Task HandleTransactionAsync_QueryDispatch_CallsGetBalanceAsync()
    {
        // Antes del fix, QUERY_TRANSACTION caía en ProcessSaleHandler y
        // devolvía FAILED por la validación de cardNumber requerido. Ahora debe llamar a
        // GetBalanceAsync y devolver el saldo en AuthorizationId.
        var (provider, calls) = CreateRecording();
        provider.BalanceResults.Enqueue(PortResult<CardBalance>.Success(
            new CardBalance(50_000, "COP", "ACTIVE", "20270101")));

        var scenario = BuildScenario(provider, calls);
        var extras = QueryExtras();

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Contains("provider.balance", scenario.Calls);
        Assert.DoesNotContain("provider.redeem", scenario.Calls);
        Assert.Empty(scenario.Sink.FailedCalls);
    }

    [Fact]
    public async Task HandleTransactionAsync_BatchClose_AcceptsWithoutCallingProvider()
    {
        // BATCH_CLOSE no tiene endpoint dedicado en Ogloba. El
        // flujo per-tx (ProcessSaleHandler.CompleteAuthorizedSaleAsync) ya llama
        // /reconciliation tras cada /confirmTransaction. Devolvemos ACCEPTED para que
        // HiPOS no bloquee el cierre de caja.
        var (provider, calls) = CreateRecording();

        var scenario = BuildScenario(provider, calls);
        var extras = BatchCloseExtras();

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: true,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["ACCEPTED"], scenario.Sink.SetOkResults);
        Assert.Empty(scenario.Sink.FailedCalls);
        Assert.DoesNotContain("provider.redeem", scenario.Calls);
        Assert.DoesNotContain("provider.void", scenario.Calls);
        Assert.DoesNotContain("provider.balance", scenario.Calls);
    }

    [Fact]
    public async Task HandleTransactionAsync_UnregisteredCashier_ReturnsFailedWithoutCallingProvider()
    {
        // Si el cashierId que HiPOS manda no está en la lista de
        // cajeros registrados, el orquestador falla rápido sin gastar una llamada a Ogloba
        // (que devolvería error 59/230040 con mensaje incomprensible para el operador).
        var (provider, calls) = CreateRecording();
        var scenario = BuildScenario(provider, calls);
        var extras = SaleExtras();

        await scenario.Orchestrator.HandleTransactionAsync(
            extras,
            configurationStoreId: null, sessionTerminalId: null, sessionCashierId: null,
            isCashierRegistered: false,
            capture: new StubCapture("1138170025515937"),
            digitalProductCode: "113816",
            senderName: "KOAJ Sandbox",
            sink: scenario.Sink,
            cancellationToken: CancellationToken.None);

        Assert.Single(scenario.Sink.FailedCalls);
        Assert.Empty(scenario.Calls);
        Assert.DoesNotContain("provider.redeem", scenario.Calls);
    }

    private static (RecordingProvider Provider, List<string> Calls) CreateRecording()
    {
        var calls = new List<string>();
        return (new RecordingProvider(calls), calls);
    }

    private static OrchestratorScenario BuildScenario(IGiftCardProvider provider, List<string> calls)
    {
        var repository = new RecordingRepository(calls);
        var audit = new RecordingAudit(calls);
        var clock = new RecordingClock();
        var transactionNumbers = new RecordingTransactionNumbers("1756113296", "1756113297");

        var sales = new ProcessSaleHandler(provider, repository, transactionNumbers, audit, clock);

        var orchestrator = new HiPosPaymentOrchestrator(
            sales, provider, NullLogger<HiPosPaymentOrchestrator>.Instance);

        return new OrchestratorScenario(orchestrator, new RecordingSink(), calls);
    }

    /// <summary>
    /// Venta normal: HiPOS no marca adelanto de pedido, así que el módulo abre en Activar.
    /// </summary>
    /// <summary>
    /// Venta facturando. Lo que la identifica es el TENDER declarado, no el TransactionType:
    /// medido en terminal, venta y entrada de caja llegan las dos como SALE.
    /// </summary>
    private static Dictionary<string, string?> SaleExtras() => BuildExtras(
        "SALE", amount: "50000", cardNumber: "1138170025515937", tenderType: "CREDIT");

    /// <summary>
    /// Abono: el mismo SALE pero con <c>IsAdvancedPayment</c>, que es lo único que en el
    /// contrato de ICG distingue el adelanto de pedido de una venta.
    /// </summary>
    private static Dictionary<string, string?> AdvancedPaymentExtras()
    {
        var extras = BuildExtras("SALE", amount: "50000", tenderType: "CREDIT");
        extras[HiPosRequestMapper.IsAdvancedPaymentProperty] = "true";
        return extras;
    }

    private static Dictionary<string, string?> RefundExtras() => BuildExtras(
        "REFUND", amount: "50000", referenceNumber: "00136544716V");

    private static Dictionary<string, string?> VoidExtras() => BuildExtras(
        "VOID_TRANSACTION", amount: "50000", referenceNumber: "00136544716V");

    /// <summary>
    /// NEGATIVE_SALE = "entrada de caja" en HiPOS. El cajero cobra aplicando un bono existente
    /// como medio de pago; el módulo debe abrir directamente en Redimir, sin chooser.
    /// </summary>
    /// <summary>
    /// Entrada de caja: llega como SALE pero SIN tender. Es lo único que la separa de una venta
    /// (medido en terminal el 2026-08-26).
    /// </summary>
    private static Dictionary<string, string?> NegativeSaleExtras() => BuildExtras(
        "SALE", amount: "50000", cardNumber: "1138170025515937");

    private static Dictionary<string, string?> QueryExtras() => BuildExtras(
        "QUERY_TRANSACTION", amount: "0", cardNumber: "1138170025515937");

    private static Dictionary<string, string?> BatchCloseExtras() => BuildExtras("BATCH_CLOSE", amount: "0");

    private static Dictionary<string, string?> BuildExtras(
        string transactionType, string amount, string? cardNumber = null, string? referenceNumber = null,
        string? tenderType = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HiPosRequestMapper.TransactionTypeProperty] = transactionType,
            [HiPosRequestMapper.StoreIdIntentProperty] = "K00036",
            [HiPosRequestMapper.TerminalIdProperty] = "caja-5",
            [HiPosRequestMapper.CashierIdProperty] = "operador-123",
            [HiPosRequestMapper.AmountProperty] = amount,
            [HiPosRequestMapper.CurrencyProperty] = "COP"
        };

        // Presente = venta facturando; ausente = entrada de caja. Es el discriminador real.
        if (tenderType is not null)
        {
            extras[HiPosRequestMapper.TenderTypeProperty] = tenderType;
        }

        if (cardNumber is not null)
        {
            extras[HiPosRequestMapper.CardNumberProperty] = cardNumber;
        }

        if (referenceNumber is not null)
        {
            extras[HiPosRequestMapper.ReferenceNumberProperty] = referenceNumber;
        }

        return extras;
    }

    private static PortResult<RedemptionAuthorization> ApprovedRedemption() =>
        PortResult<RedemptionAuthorization>.Success(new RedemptionAuthorization(
            ReferenceNumber.Create("00136544716V").Value,
            Money.Create(0, "COP").Value,
            "1138170025515937",
            EGiftCardUrl: null));

    private static PortResult<Unit> SuccessUnit() => PortResult<Unit>.Success(Unit.Value);

    private sealed class OrchestratorScenario
    {
        public OrchestratorScenario(
            HiPosPaymentOrchestrator orchestrator,
            RecordingSink sink,
            List<string> calls)
        {
            Orchestrator = orchestrator;
            Sink = sink;
            Calls = calls;
        }

        public HiPosPaymentOrchestrator Orchestrator { get; }

        public RecordingSink Sink { get; }

        public List<string> Calls { get; }
    }

    /// <summary>
    /// Módulo simulado. Devuelve el resultado configurado, o null para "el cajero salió sin
    /// operar". Las pantallas reales son las que llaman a Ogloba, así que un cobro que pasa por
    /// aquí NO toca el provider — eso es justamente lo que verifican los tests.
    /// </summary>
    private sealed class StubCapture : IHiPosCardCapture
    {
        private readonly HiPosOperationOutcome? _outcome;

        public StubCapture(string? value, long amountApplied = 50_000)
            => _outcome = value is null
                ? null
                : new HiPosOperationOutcome(
                    Accepted: true,
                    Pending: false,
                    AmountAppliedPesos: amountApplied,
                    Reference: "00136544716V",
                    CardNumber: value);

        public StubCapture(HiPosOperationOutcome outcome) => _outcome = outcome;

        public int Calls { get; private set; }

        public HiPosSaleIntent? SuggestedIntent { get; private set; }

        public long AmountRequested { get; private set; }

        public Task<HiPosOperationOutcome?> RunAsync(
            HiPosSaleIntent intent,
            long amountPesos,
            string currency,
            CancellationToken cancellationToken)
        {
            Calls++;
            SuggestedIntent = intent;
            AmountRequested = amountPesos;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>
    /// Módulo que falla el test si llega a invocarse. Sirve para verificar que el orquestrador
    /// NO levantó la app cuando el intent ya traía CardNumber.
    /// </summary>
    private sealed class NeverCalledCapture : IHiPosCardCapture
    {
        public Task<HiPosOperationOutcome?> RunAsync(
            HiPosSaleIntent intent,
            long amountPesos,
            string currency,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "El módulo no debió levantarse: el intent ya traía CardNumber.");
        }
    }

    private sealed class RecordingSink : IHiPosResponseSink
    {
        public List<string> SetOkResults { get; } = [];

        public List<string> FailedCalls { get; } = [];

        public void SetResultOk(string transactionResult, IDictionary<string, string?> extras)
        {
            SetOkResults.Add(transactionResult);

            foreach (var (key, value) in extras)
            {
                StringExtras[key] = value;
            }
        }

        public void SetResultFailed(string errorMessage)
        {
            FailedCalls.Add(errorMessage);
        }

        public List<string> CanceledCalls { get; } = [];

        public Dictionary<string, int> IntExtras { get; } = [];

        public int FinishCalls { get; private set; }

        /// <summary>Extras del último RESULT_CANCELED, aparte de los de RESULT_OK.</summary>
        public Dictionary<string, string?> CanceledExtras { get; } = [];

        public void SetResultCanceled(IDictionary<string, string?> extras)
        {
            CanceledCalls.Add("CANCELED");

            foreach (var (key, value) in extras)
            {
                CanceledExtras[key] = value;
            }
        }

        public Dictionary<string, string?> StringExtras { get; } = [];

        public Dictionary<string, byte[]> ByteExtras { get; } = [];

        public Dictionary<string, bool> BoolExtras { get; } = [];

        public void PutIntExtra(string key, int value)
        {
            IntExtras[key] = value;
        }

        public void PutBoolExtra(string key, bool value)
        {
            BoolExtras[key] = value;
        }

        public void PutByteArrayExtra(string key, byte[] value)
        {
            ByteExtras[key] = value;
        }

        public void FinishActivity()
        {
            FinishCalls++;
        }
    }

    // Stubs locales — el proyecto Maui.Tests no ve los TestDoubles de Application.Tests.
    private sealed class RecordingProvider : IGiftCardProvider
    {
        private readonly List<string> _calls;

        public RecordingProvider(List<string> calls) => _calls = calls;

        public Queue<PortResult<RedemptionAuthorization>> RedemptionResults { get; } = new();

        public Queue<PortResult<RedemptionAuthorization>> ActivationResults { get; } = new();

        public Queue<PortResult<Unit>> ConfirmationResults { get; } = new();

        public Queue<PortResult<Unit>> CancellationResults { get; } = new();

        public Queue<PortResult<Unit>> ReversalResults { get; } = new();

        public Queue<PortResult<Unit>> VoidResults { get; } = new();

        public List<VoidTransactionRequest> VoidRequests { get; } = [];

        public Queue<PortResult<Unit>> ReconciliationResults { get; } = new();

        public Queue<PortResult<CardBalance>> BalanceResults { get; } = new();

        public Task<PortResult<RedemptionAuthorization>> RedeemAsync(
            RedemptionRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.redeem");
            return Task.FromResult(RedemptionResults.Count > 0
                ? RedemptionResults.Dequeue()
                : throw new InvalidOperationException("No redemption result configured."));
        }

        public Task<PortResult<RedemptionAuthorization>> ActivateAsync(
            RedemptionRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.activate");
            return Task.FromResult(ActivationResults.Count > 0
                ? ActivationResults.Dequeue()
                : throw new InvalidOperationException("No activation result configured."));
        }

        public Task<PortResult<Unit>> ConfirmAsync(
            TransactionActionRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.confirm");
            return Task.FromResult(ConfirmationResults.Count > 0
                ? ConfirmationResults.Dequeue()
                : PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<Unit>> CancelAsync(
            TransactionActionRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.cancel");
            return Task.FromResult(CancellationResults.Count > 0
                ? CancellationResults.Dequeue()
                : PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<Unit>> ReverseAsync(
            ReversalRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.reverse");
            return Task.FromResult(ReversalResults.Count > 0
                ? ReversalResults.Dequeue()
                : PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<Unit>> VoidAsync(
            VoidTransactionRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.void");
            VoidRequests.Add(request);
            return Task.FromResult(VoidResults.Count > 0
                ? VoidResults.Dequeue()
                : PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<Unit>> ReconcileAsync(
            ReconciliationRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.reconcile");
            return Task.FromResult(ReconciliationResults.Count > 0
                ? ReconciliationResults.Dequeue()
                : PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<CardBalance>> GetBalanceAsync(
            BalanceQuery request, CancellationToken cancellationToken)
        {
            _calls.Add("provider.balance");
            return Task.FromResult(BalanceResults.Count > 0
                ? BalanceResults.Dequeue()
                : PortResult<CardBalance>.Failed(new PortFailure(
                    "no.balance.configured", "No balance result configured.",
                    PortFailureType.Technical)));
        }

        public Task<PortResult<Unit>> CheckConnectivityAsync(
            StoreId storeId, CancellationToken cancellationToken) =>
            Task.FromResult(PortResult<Unit>.Success(Unit.Value));

        public Task<PortResult<BusinessUnitInfo>> GetBusinessUnitInfoAsync(
            StoreId storeId, CancellationToken cancellationToken) =>
            Task.FromResult(PortResult<BusinessUnitInfo>.Success(new BusinessUnitInfo("test", 0)));

        public Task<PortResult<RedemptionAuthorization>> ReloadAsync(
            RedemptionRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<GiftCardOrderCreated>> CreateOrderAsync(
            CreateGiftCardOrderRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<GiftCardOrderConfirmation>> ConfirmOrderAsync(
            ConfirmGiftCardOrderRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<Unit>> CancelOrderAsync(
            ConfirmGiftCardOrderRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<GiftCardOrderConfirmation>> GetOrderStatusAsync(
            ConfirmGiftCardOrderRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<OrderReturnResult>> ReturnOrderAsync(
            ReturnGiftCardOrderRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<IReadOnlyList<GiftCardProduct>>> GetProductsAsync(
            Permoda.Pay.Domain.Payments.StoreId storeId, string? itemCode, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PortResult<IReadOnlyList<OglobaTransactionRecord>>> QueryTransactionsHistoryAsync(
            Permoda.Pay.Domain.Payments.StoreId storeId, string? transDateFrom, string? transDateTo,
            int pageNo, int numberOfPage, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class RecordingRepository : IPendingPaymentRepository
    {
        private readonly List<string> _calls;

        public RecordingRepository(List<string> calls) => _calls = calls;

        public Task<PortResult<Unit>> SaveAsync(PaymentTransaction transaction, CancellationToken cancellationToken)
        {
            _calls.Add($"repository.save.{transaction.Status}");
            return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<Unit>> DeleteAsync(TransactionNumber transactionNumber, CancellationToken cancellationToken)
        {
            _calls.Add("repository.delete");
            return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
        }

        public Task<PortResult<IReadOnlyCollection<PaymentTransaction>>> LoadPendingAsync(CancellationToken cancellationToken)
        {
            _calls.Add("repository.load");
            return Task.FromResult(PortResult<IReadOnlyCollection<PaymentTransaction>>.Success(
                Array.Empty<PaymentTransaction>()));
        }
    }

    private sealed class RecordingAudit : ITransactionAudit
    {
        private readonly List<string> _calls;

        public RecordingAudit(List<string> calls) => _calls = calls;

        public Task<PortResult<Unit>> RecordAsync(TransactionAuditEntry entry, CancellationToken cancellationToken)
        {
            _calls.Add($"audit.{entry.Event}");
            return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
        }
    }

    private sealed class RecordingClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingTransactionNumbers : ITransactionNumberGenerator
    {
        private readonly Queue<TransactionNumber> _queue;

        public RecordingTransactionNumbers(params string[] numbers)
        {
            _queue = new Queue<TransactionNumber>(numbers.Select(n => TransactionNumber.Create(n).Value));
        }

        public TransactionNumber Generate() =>
            _queue.Count > 0 ? _queue.Dequeue() : TransactionNumber.Create(Guid.NewGuid().ToString("N")).Value;
    }
}

