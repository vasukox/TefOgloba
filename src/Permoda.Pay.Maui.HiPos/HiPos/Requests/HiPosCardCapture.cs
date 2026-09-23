namespace Permoda.Pay.Maui.HiPos;

/// <summary>
/// Cómo se va a operar sobre el bono cuando el cajero lo activa en el módulo.
/// </summary>
public enum HiPosCardKind
{
    Physical = 1,
    Virtual = 2
}

/// <summary>
/// Cómo terminó la operación que el cajero hizo en las pantallas del módulo, en los términos
/// que HiPOS necesita para cerrar (o no) el documento.
/// <para>
/// El módulo llama a Ogloba, muestra el resultado en pantalla y recién entonces devuelve esto.
/// Antes entregaba solo el serial y la llamada la hacía el módulo de pago después de cerrar la
/// pantalla: cuando el bono fallaba, el cajero no veía el motivo — se cerraba el APK y el error
/// aparecía como una alerta genérica de HiPOS.
/// </para>
/// </summary>
/// <param name="Pending">
/// Ogloba aceptó el pedido pero todavía lo está emitiendo (orderStatus 043). Se traduce a
/// <c>UNKNOWN_RESULT</c>: ACCEPTED cerraría el documento por un bono que aún puede fallar, y
/// FAILED perdería la venta por uno que probablemente sí se emita.
/// </param>
/// <param name="AmountAppliedPesos">
/// Lo que realmente se movió, que no siempre es lo que pidió HiPOS: si el bono tenía menos
/// saldo que la venta, se redime lo que hay y el POS cobra la diferencia por otro medio. Va de
/// vuelta en el extra <c>Amount</c> para que HiPOS sepa cuánto quedó pendiente.
/// </param>
/// <param name="Customer">
/// Cliente del recaudo — "NOMBRE · CC 123456" — cuando el cajero lo capturó. Viaja a HiPOS en
/// <c>CardHolder</c> para que quede en el documento y en el comprobante impreso.
/// </param>
public sealed record HiPosOperationOutcome(
    bool Accepted,
    bool Pending,
    long AmountAppliedPesos,
    string? Reference = null,
    string? CardNumber = null,
    string? Customer = null,
    long? RemainingBalancePesos = null,
    string? ErrorMessage = null);

/// <summary>
/// Puerto con el que el orquestrador levanta las pantallas del módulo cuando HiPOS pide un
/// cobro. Vive en Maui.HiPos para que el orquestrador (que no ve tipos de MAUI/Android) pueda
/// probarse sin levantar la Activity.
/// </summary>
public interface IHiPosCardCapture
{
    /// <summary>
    /// Abre el módulo en la pantalla que corresponde y espera a que el cajero termine —
    /// incluyendo la llamada a Ogloba y la confirmación del resultado. Devuelve cómo quedó la
    /// operación, o <c>null</c> si el cajero salió sin operar.
    /// </summary>
    /// <param name="intent">
    /// Activar o Redimir según el TransactionType de HiPOS. El cajero no tiene cómo cambiarlo:
    /// el POS ya decidió qué operación es.
    /// </param>
    /// <param name="amountPesos">Importe del documento, ya convertido a pesos enteros.</param>
    Task<HiPosOperationOutcome?> RunAsync(
        HiPosSaleIntent intent,
        long amountPesos,
        string currency,
        CancellationToken cancellationToken);
}
