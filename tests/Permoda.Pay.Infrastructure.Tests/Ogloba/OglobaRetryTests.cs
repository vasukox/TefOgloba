using System.Net;
using System.Net.Sockets;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba;
using Permoda.Pay.Infrastructure.Tests.TestDoubles;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

/// <summary>
/// Los reintentos contra Ogloba. Lo que se prueba acá no es que reintente: es que
/// <b>NO reintente cuando reintentar cobraría dos veces</b>.
/// <para>
/// El caso peligroso es el timeout de una redención. La petición ya salió y Ogloba pudo haberla
/// aplicado; lo que se perdió fue la respuesta. Repetirla descuenta el saldo del bono dos veces y
/// el cliente se queda sin plata que sí gastó. Estas pruebas existen para que nadie "mejore" la
/// resiliencia agregando ese reintento.
/// </para>
/// </summary>
public sealed class OglobaRetryTests
{
    // Una redención exitosa necesita referenceNumber, balance Y currency: sin los tres, el
    // proveedor la da por respuesta malformada. No es capricho del test — es la comprobación que
    // evita construir una autorización a medias con la que después se firma un cobro.
    private const string SuccessBody = """
        {"errorCode":"0","isSuccessful":true,"referenceNumber":"REF1",
         "cardNumber":"1138110025515937","balance":50000,"currency":"COP"}
        """;

    private static OglobaGiftCardProvider Provider(
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new OglobaOptions(
                new Uri("https://ogloba.test/giftCardService"),
                requestTimeout: timeout ?? TimeSpan.FromSeconds(30)),
            new StubOglobaCredentialProvider("test-password"));

    private static RedemptionRequest Redemption() =>
        new(
            TransactionNumber.Create("1788968903").Value,
            StoreId.Create("K00037").Value,
            TerminalId.Create("CAJA-01").Value,
            CashierId.Create("felipe").Value,
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value);

    private static HttpRequestException ConnectionRefused() =>
        new("No se pudo conectar.", new SocketException((int)SocketError.ConnectionRefused));

    private static HttpRequestException ConnectionReset() =>
        new("La conexión se cortó.", new SocketException((int)SocketError.ConnectionReset));

    // ===================== LO CRÍTICO: qué NO se reintenta =====================

    /// <summary>
    /// UN TIMEOUT EN UNA REDENCIÓN NO SE REINTENTA. Si este test se pone en rojo porque alguien
    /// agregó el reintento, lo que está en juego es cobrarle dos veces a un cliente.
    /// </summary>
    [Fact]
    public async Task Una_redencion_que_expira_no_se_reintenta_jamas()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;
            throw new TaskCanceledException("Se acabó el tiempo.");
        });

        await Provider(handler).RedeemAsync(Redemption(), CancellationToken.None);

        Assert.Equal(1, intentos);
    }

    /// <summary>
    /// Una conexión CORTADA a mitad tampoco: pudo cortarse después de que Ogloba recibiera y
    /// aplicara la operación. Es ambiguo, y lo ambiguo no se repite.
    /// </summary>
    [Fact]
    public async Task Una_redencion_con_la_conexion_cortada_no_se_reintenta()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;
            throw ConnectionReset();
        });

        await Provider(handler).RedeemAsync(Redemption(), CancellationToken.None);

        Assert.Equal(1, intentos);
    }

    // ===================== Lo que SÍ se reintenta =====================

    /// <summary>
    /// Si no hubo con quién conectarse, la petición nunca llegó: no hay nada aplicado del otro
    /// lado y repetirla es seguro. Es el parpadeo de red típico de una tienda.
    /// </summary>
    [Fact]
    public async Task Una_redencion_que_no_alcanzo_a_salir_si_se_reintenta()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;

            if (intentos == 1)
            {
                throw ConnectionRefused();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, System.Text.Encoding.UTF8, "application/json")
            });
        });

        var result = await Provider(handler).RedeemAsync(Redemption(), CancellationToken.None);

        Assert.Equal(2, intentos);
        Assert.True(result.IsSuccess, "El segundo intento respondió bien y debió aceptarse.");
    }

    /// <summary>Y no insiste para siempre: dos intentos y se rinde con un fallo legible.</summary>
    [Fact]
    public async Task La_escritura_se_rinde_al_segundo_intento()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;
            throw ConnectionRefused();
        });

        var result = await Provider(handler).RedeemAsync(Redemption(), CancellationToken.None);

        Assert.Equal(OglobaRetryPolicy.WriteAttempts, intentos);
        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Una LECTURA sí se reintenta ante un timeout: consultar no deja nada a medias. Acá el
    /// reintento convierte un parpadeo de red en medio segundo de demora.
    /// </summary>
    [Fact]
    public async Task Una_lectura_que_expira_si_se_reintenta()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;

            if (intentos < OglobaRetryPolicy.ReadAttempts)
            {
                throw new TaskCanceledException("Se acabó el tiempo.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"errorCode":"0","isSuccessful":true}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        });

        await Provider(handler).CheckConnectivityAsync(
            StoreId.Create("K00037").Value, CancellationToken.None);

        Assert.Equal(OglobaRetryPolicy.ReadAttempts, intentos);
    }

    /// <summary>
    /// Una respuesta de NEGOCIO no es un fallo de red. Que Ogloba diga «la tarjeta no existe» es
    /// una respuesta, y repetirla solo haría esperar al cajero para recibir lo mismo.
    /// </summary>
    [Fact]
    public async Task Un_rechazo_de_negocio_no_se_reintenta()
    {
        var intentos = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            intentos++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"errorCode":"52","isSuccessful":false,"errorMessage":"La tarjeta no existe"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        });

        var result = await Provider(handler).RedeemAsync(Redemption(), CancellationToken.None);

        Assert.Equal(1, intentos);
        Assert.True(result.IsFailure);
    }

    // ===================== La política, aislada =====================

    [Fact]
    public void La_politica_nunca_reintenta_una_escritura_por_timeout()
    {
        Assert.False(OglobaRetryPolicy.ShouldRetryWrite(new TaskCanceledException()));
        Assert.False(OglobaRetryPolicy.ShouldRetryWrite(new OperationCanceledException()));
    }

    [Fact]
    public void La_politica_solo_reintenta_escrituras_que_no_llegaron_al_servidor()
    {
        Assert.True(OglobaRetryPolicy.ShouldRetryWrite(ConnectionRefused()));
        Assert.False(OglobaRetryPolicy.ShouldRetryWrite(ConnectionReset()));

        // Sin SocketException adentro no hay forma de demostrar que no llegó: ante la duda, no.
        Assert.False(OglobaRetryPolicy.ShouldRetryWrite(new HttpRequestException("algo pasó")));
    }

    /// <summary>
    /// La espera total de los reintentos se queda por debajo del segundo. Al otro lado hay un
    /// cajero con un cliente enfrente, y una espera larga se siente peor que un error claro.
    /// </summary>
    [Fact]
    public void La_espera_total_no_hace_esperar_al_cajero()
    {
        var total = TimeSpan.Zero;

        for (var attempt = 1; attempt < OglobaRetryPolicy.ReadAttempts; attempt++)
        {
            total += OglobaRetryPolicy.Delay(attempt);
        }

        Assert.True(total < TimeSpan.FromSeconds(1), $"La espera acumulada es {total}.");
    }
}
