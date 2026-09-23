namespace Permoda.Pay.Maui.Tests.Pos;

/// <summary>
/// Cobertura de la venta en la pantalla de redimir: cuánto ponen los bonos y cuánto le queda por
/// pagar al cliente.
/// <para>
/// La lógica vive en RedeemTabViewModel, que arrastra sesión, HiPosFlow y navegación de MAUI —
/// montarlo entero para verificar una resta sería falsificar media app. Se prueba la regla, que
/// es lo que puede romperse al tocarla.
/// </para>
/// <para>
/// El bug que motivó estos tests: la cifra grande contaba SOLO los bonos ya agregados a la lista.
/// Entre escanear un bono y agregarlo, mostraba el total de la venta sin descontar nada —como si
/// el bono no existiera— mientras el detalle de abajo ya decía bien cuánto faltaba. Dos números
/// contradiciéndose en la misma pantalla, y el equivocado era el grande.
/// </para>
/// </summary>
public sealed class RedeemCoverageTests
{
    /// <summary>Lo que aportan los bonos de la lista MÁS el que está en pantalla sin agregar.</summary>
    private static long Applied(long queued, long scannedNotQueued) => queued + scannedNotQueued;

    private static long Covered(long saleTotal, long queued, long scanned) =>
        Math.Min(Applied(queued, scanned), saleTotal);

    private static long Pending(long saleTotal, long queued, long scanned) =>
        Math.Max(0, saleTotal - Applied(queued, scanned));

    private static double Ratio(long saleTotal, long queued, long scanned) =>
        saleTotal <= 0 ? 0 : Math.Clamp((double)Covered(saleTotal, queued, scanned) / saleTotal, 0, 1);

    /// <summary>
    /// El caso reportado: bono escaneado, todavía no agregado a la lista. La cifra grande tiene
    /// que descontarlo ya.
    /// </summary>
    [Fact]
    public void Bono_escaneado_sin_agregar_ya_descuenta_de_lo_que_falta()
    {
        const long venta = 119_700;
        const long escaneado = 50_000;

        Assert.Equal(69_700, Pending(venta, queued: 0, scanned: escaneado));
        Assert.Equal(50_000, Covered(venta, queued: 0, scanned: escaneado));
    }

    /// <summary>Sin nada escaneado ni en la lista, falta la venta entera. Ese sí es el total.</summary>
    [Fact]
    public void Sin_bonos_falta_la_venta_completa()
    {
        Assert.Equal(119_700, Pending(119_700, queued: 0, scanned: 0));
        Assert.Equal(0, Covered(119_700, queued: 0, scanned: 0));
        Assert.Equal(0, Ratio(119_700, queued: 0, scanned: 0));
    }

    /// <summary>Los de la lista y el de pantalla se suman: no se cuenta uno u otro.</summary>
    [Fact]
    public void Se_suman_los_de_la_lista_y_el_de_pantalla()
    {
        const long venta = 119_700;

        Assert.Equal(19_700, Pending(venta, queued: 60_000, scanned: 40_000));
        Assert.Equal(100_000, Covered(venta, queued: 60_000, scanned: 40_000));
    }

    /// <summary>
    /// Un bono que vale más que la compra la cubre y nada más: lo cubierto se topa al valor de la
    /// venta y lo que falta nunca baja de cero. Sin el tope, la pantalla mostraría una deuda
    /// negativa.
    /// </summary>
    [Theory]
    [InlineData(89_900, 300_000)]
    [InlineData(10_000, 500_000)]
    public void Un_bono_mayor_que_la_venta_no_produce_saldos_negativos(long venta, long bono)
    {
        Assert.Equal(0, Pending(venta, queued: 0, scanned: bono));
        Assert.Equal(venta, Covered(venta, queued: 0, scanned: bono));
        Assert.Equal(1.0, Ratio(venta, queued: 0, scanned: bono));
    }

    /// <summary>La proporción es la que pinta la barra: entre 0 y 1, nunca fuera.</summary>
    [Theory]
    [InlineData(100_000, 0, 0.0)]
    [InlineData(100_000, 25_000, 0.25)]
    [InlineData(100_000, 50_000, 0.5)]
    [InlineData(100_000, 100_000, 1.0)]
    [InlineData(100_000, 250_000, 1.0)]
    public void La_proporcion_se_mantiene_entre_cero_y_uno(long venta, long bono, double esperado)
    {
        Assert.Equal(esperado, Ratio(venta, queued: 0, scanned: bono), precision: 4);
    }

    /// <summary>
    /// En redención manual no hay factura contra la cual medir. El panel no aplica y la
    /// proporción no puede dividir por cero.
    /// </summary>
    [Fact]
    public void Sin_venta_de_HiPOS_no_hay_cobertura_ni_division_por_cero()
    {
        Assert.Equal(0, Ratio(saleTotal: 0, queued: 0, scanned: 50_000));
        Assert.Equal(0, Pending(saleTotal: 0, queued: 0, scanned: 50_000));
    }

    /// <summary>
    /// Cubierta exacta: no queda nada por pagar y la barra llega al tope. Es el caso en que la
    /// pantalla cambia el rótulo a "la venta queda cubierta".
    /// </summary>
    [Fact]
    public void Cobertura_exacta_deja_cero_por_pagar()
    {
        Assert.Equal(0, Pending(119_700, queued: 119_700, scanned: 0));
        Assert.Equal(1.0, Ratio(119_700, queued: 119_700, scanned: 0));
    }
}
