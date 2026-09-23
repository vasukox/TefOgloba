using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Features.Provisioning;

namespace Permoda.Pay.Application.Tests.Features.Provisioning;

/// <summary>
/// El emparejamiento completo, con sockets de verdad sobre loopback. No hay dobles del transporte
/// a propósito: lo que puede fallar acá —el orden de los mensajes, un par que se cuelga, tres
/// cajas pidiendo el mismo nombre— solo aparece cuando hay un socket de por medio.
/// </summary>
public sealed class PairingTransportTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    }

    private const string Loopback = "127.0.0.1";

    private static TerminalConfigurationEnvelope Envelope(params string[] assigned) =>
        new(
            StoreId: "K00537",
            SubscriptionKey: "LLAVE-FALSA-SOLO-PARA-PRUEBAS-01",
            BaseUrl: "https://apim-permoda-prod.azure-api.net",
            ApiVersion: "2.18",
            StoreName: "KOAJ EVENTOS",
            AdminPinHash: "PINHASH",
            Cashiers: [new ReplicatedCashier("felipe", "HASH1", IsEnabled: true)],
            AssignedTerminalIds: assigned.Length == 0 ? ["CAJA-01"] : assigned);

    /// <summary>Puerto 0 = que el sistema dé uno libre. Si no, los tests chocan entre sí.</summary>
    private static PairingHost StartHost(
        FixedClock clock,
        Func<CancellationToken, Task<TerminalConfigurationEnvelope>>? factory = null) =>
        PairingHost.Start(factory ?? (_ => Task.FromResult(Envelope())), clock, port: 0);

    [Fact]
    public async Task Con_el_codigo_correcto_la_configuracion_llega_completa()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var result = await PairingClient.FetchAsync(
            Loopback, host.Code, CancellationToken.None, host.Port);

        Assert.True(result.IsSuccess);
        Assert.Equal("K00537", result.Envelope!.StoreId);
        Assert.Equal("LLAVE-FALSA-SOLO-PARA-PRUEBAS-01", result.Envelope.SubscriptionKey);
        Assert.Equal("PINHASH", result.Envelope.AdminPinHash);
        Assert.Single(result.Envelope.Cashiers);
    }

    /// <summary>
    /// La tienda se conoce ANTES de acertar el código: es lo que deja confirmar que se está
    /// copiando de la KOAJ correcta y no de otra que esté en la misma red.
    /// </summary>
    [Fact]
    public async Task La_tienda_se_informa_aunque_el_codigo_falle()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var result = await PairingClient.FetchAsync(
            Loopback, "000000", CancellationToken.None, host.Port);

        Assert.Equal(PairingOutcome.InvalidCode, result.Outcome);
        Assert.Equal("K00537", result.StoreId);
    }

    [Fact]
    public async Task Un_codigo_equivocado_no_entrega_nada()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var wrong = host.Code == "000000" ? "111111" : "000000";
        var result = await PairingClient.FetchAsync(
            Loopback, wrong, CancellationToken.None, host.Port);

        Assert.Equal(PairingOutcome.InvalidCode, result.Outcome);
        Assert.Null(result.Envelope);
    }

    /// <summary>
    /// LA defensa del diseño: tres fallos y el código se muere. Sin esto, un millón de
    /// combinaciones se prueban por red en un rato.
    /// </summary>
    [Fact]
    public async Task A_los_tres_fallos_el_codigo_se_quema()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var wrong = host.Code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < PairingSecret.MaxAttempts; attempt++)
        {
            var failed = await PairingClient.FetchAsync(
                Loopback, wrong, CancellationToken.None, host.Port);
            Assert.Equal(PairingOutcome.InvalidCode, failed.Outcome);
        }

        Assert.Equal(0, host.AttemptsRemaining);

        // Y ahora ni siquiera el código BUENO sirve: hay que generar uno nuevo.
        var afterBurn = await PairingClient.FetchAsync(
            Loopback, host.Code, CancellationToken.None, host.Port);

        Assert.Equal(PairingOutcome.NoAttemptsLeft, afterBurn.Outcome);
        Assert.Null(afterBurn.Envelope);
    }

    /// <summary>
    /// Los intentos se informan para que el operador sepa que le quedan dos, en vez de descubrir
    /// que se quemó cuando ya no hay nada que hacer.
    /// </summary>
    [Fact]
    public async Task Se_informa_cuantos_intentos_quedan()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var wrong = host.Code == "000000" ? "111111" : "000000";
        var first = await PairingClient.FetchAsync(
            Loopback, wrong, CancellationToken.None, host.Port);

        Assert.Equal(PairingSecret.MaxAttempts - 1, first.AttemptsRemaining);
    }

    /// <summary>
    /// La decisión de operación: un código, varias cajas. El que instala genera uno y camina la
    /// tienda sin volver a la primera caja.
    /// </summary>
    [Fact]
    public async Task Un_mismo_codigo_configura_varias_cajas()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        for (var caja = 0; caja < 4; caja++)
        {
            var result = await PairingClient.FetchAsync(
                Loopback, host.Code, CancellationToken.None, host.Port);

            Assert.True(result.IsSuccess, $"La caja {caja + 2} no pudo copiar.");
        }

        Assert.Equal(4, host.SuccessfulTransfers);
        Assert.Equal(PairingSecret.MaxAttempts, host.AttemptsRemaining);
    }

    /// <summary>
    /// Tres cajas seguidas NO pueden quedar todas como CAJA-02. Es el bug que haría que la
    /// bitácora de Ogloba dejara de decir dónde ocurrió cada cobro.
    /// </summary>
    [Fact]
    public async Task Cada_caja_recibe_un_nombre_distinto()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        var names = new List<string>();

        for (var caja = 0; caja < 3; caja++)
        {
            var result = await PairingClient.FetchAsync(
                Loopback, host.Code, CancellationToken.None, host.Port);

            Assert.True(result.IsSuccess);
            names.Add(result.Envelope!.SuggestNextTerminalId());
        }

        Assert.Equal(["CAJA-02", "CAJA-03", "CAJA-04"], names);
        Assert.Equal(3, names.Distinct().Count());
    }

    /// <summary>
    /// El sobre se relee en cada entrega: entre la primera caja y la tercera el administrador
    /// pudo dar de alta otro cajero, y la tercera tiene que recibir la lista de verdad.
    /// </summary>
    [Fact]
    public async Task El_sobre_se_relee_en_cada_entrega()
    {
        var clock = new FixedClock();
        var cashiers = new List<ReplicatedCashier>
        {
            new("felipe", "HASH1", IsEnabled: true)
        };

        await using var host = PairingHost.Start(
            _ => Task.FromResult(Envelope() with { Cashiers = cashiers.ToArray() }),
            clock,
            port: 0);

        var first = await PairingClient.FetchAsync(
            Loopback, host.Code, CancellationToken.None, host.Port);
        Assert.Single(first.Envelope!.Cashiers);

        cashiers.Add(new ReplicatedCashier("yaid", "HASH2", IsEnabled: true));

        var second = await PairingClient.FetchAsync(
            Loopback, host.Code, CancellationToken.None, host.Port);
        Assert.Equal(2, second.Envelope!.Cashiers.Count);
    }

    [Fact]
    public async Task Vencida_la_ventana_no_se_entrega_nada()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        clock.UtcNow += PairingProtocol.Window + TimeSpan.FromSeconds(1);

        var result = await PairingClient.FetchAsync(
            Loopback, host.Code, CancellationToken.None, host.Port);

        Assert.Equal(PairingOutcome.WindowClosed, result.Outcome);
        Assert.Null(result.Envelope);
        Assert.True(host.IsExpired);
    }

    /// <summary>
    /// Cerrar la pantalla apaga el servicio EN EL ACTO. Si sobreviviera, quedaría una caja
    /// ofreciendo la llave de producción sin que nadie lo esté mirando.
    /// </summary>
    [Fact]
    public async Task Al_cerrar_la_ventana_el_servicio_deja_de_responder()
    {
        var clock = new FixedClock();
        var host = StartHost(clock);
        var port = host.Port;
        var code = host.Code;

        await host.DisposeAsync();

        var result = await PairingClient.FetchAsync(
            Loopback, code, CancellationToken.None, port);

        Assert.Equal(PairingOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task Cerrar_dos_veces_no_se_queja()
    {
        var clock = new FixedClock();
        var host = StartHost(clock);

        await host.DisposeAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Si_no_hay_nadie_repartiendo_se_reporta_inalcanzable()
    {
        // Puerto muy improbable de estar ocupado por algo que hable este protocolo.
        var result = await PairingClient.FetchAsync(
            Loopback, "427913", CancellationToken.None, port: 47199);

        Assert.Equal(PairingOutcome.Unreachable, result.Outcome);
        Assert.Null(result.Envelope);
    }

    /// <summary>
    /// La llave de producción no puede salir por el socket en claro. Es la comprobación que más
    /// duele si algún día alguien "simplifica" el cifrado.
    /// </summary>
    [Fact]
    public async Task La_llave_nunca_viaja_legible()
    {
        var clock = new FixedClock();
        await using var host = StartHost(clock);

        using var raw = new System.Net.Sockets.TcpClient();
        await raw.ConnectAsync(Loopback, host.Port);
        await using var stream = raw.GetStream();

        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer);
        var greeting = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

        // Lo primero que manda la caja es el saludo, y ahí no puede ir la llave ni el código.
        Assert.DoesNotContain("LLAVE-FALSA-SOLO-PARA-PRUEBAS-01", greeting, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Code, greeting, StringComparison.Ordinal);
        Assert.DoesNotContain("PINHASH", greeting, StringComparison.Ordinal);
    }
}
