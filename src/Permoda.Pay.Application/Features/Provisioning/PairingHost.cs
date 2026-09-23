using System.Net;
using System.Net.Sockets;
using Permoda.Pay.Application.Abstractions.Time;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// La caja YA configurada mientras reparte su configuración a las demás de la tienda.
/// <para>
/// Solo existe mientras el operador tiene la pantalla abierta. No es un servicio que arranque con
/// la app ni que sobreviva a cerrarla: eso sería una caja ofreciendo la llave de producción de la
/// tienda en la red las 24 horas, y el 99% del tiempo sin que nadie lo esté mirando.
/// </para>
/// <para>
/// El código admite VARIAS cajas dentro de la ventana —fue la decisión de operación: el que
/// instala genera uno y camina la tienda—. Lo que sí quema el código son los FALLOS: tres
/// intentos equivocados y hay que generar uno nuevo.
/// </para>
/// </summary>
public sealed class PairingHost : IAsyncDisposable
{
    private readonly TcpListener _listener;

    /// <summary>
    /// De dónde sale el sobre en cada entrega. Es ASÍNCRONA a propósito: del otro lado hay lectura
    /// del almacenamiento seguro, y resolverla con <c>GetAwaiter().GetResult()</c> bloquearía un
    /// hilo del pool en una app que en ese mismo momento puede estar cobrando.
    /// </summary>
    private readonly Func<CancellationToken, Task<TerminalConfigurationEnvelope>> _envelopeFactory;
    private readonly IClock _clock;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly HashSet<string> _reservedTerminalIds = new(StringComparer.OrdinalIgnoreCase);

    private int _attemptsRemaining = PairingSecret.MaxAttempts;
    private int _successfulTransfers;
    private Task? _acceptLoop;

    private PairingHost(
        TcpListener listener,
        Func<CancellationToken, Task<TerminalConfigurationEnvelope>> envelopeFactory,
        IClock clock,
        string code,
        DateTimeOffset expiresAt)
    {
        _listener = listener;
        _envelopeFactory = envelopeFactory;
        _clock = clock;
        Code = code;
        ExpiresAt = expiresAt;
    }

    /// <summary>El código que el operador lee en pantalla y teclea en la otra caja.</summary>
    public string Code { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Puerto real. Es <see cref="PairingProtocol.Port"/> salvo en pruebas.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int AttemptsRemaining
    {
        get { lock (_gate) { return _attemptsRemaining; } }
    }

    /// <summary>Cuántas cajas se configuraron con este código. Va a la pantalla del operador.</summary>
    public int SuccessfulTransfers
    {
        get { lock (_gate) { return _successfulTransfers; } }
    }

    public TimeSpan TimeRemaining
    {
        get
        {
            var left = ExpiresAt - _clock.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// La ventana sigue abierta: queda tiempo Y quedan intentos. Las dos condiciones la cierran.
    /// </summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _attemptsRemaining > 0 && !_shutdown.IsCancellationRequested;
            }
        }
        // El tiempo se comprueba aparte porque depende del reloj y no del candado.
    }

    public bool IsExpired => TimeRemaining == TimeSpan.Zero;

    /// <summary>
    /// Abre la ventana y empieza a escuchar.
    /// </summary>
    /// <param name="envelopeFactory">
    /// Se invoca en CADA entrega, no una sola vez: entre la primera caja y la tercera el
    /// administrador pudo dar de alta otro cajero, y la tercera tiene que recibir la lista de
    /// verdad y no una foto de hace ocho minutos.
    /// </param>
    /// <param name="port">Solo se cambia en pruebas; 0 pide un puerto libre al sistema.</param>
    public static PairingHost Start(
        Func<CancellationToken, Task<TerminalConfigurationEnvelope>> envelopeFactory,
        IClock clock,
        int port = PairingProtocol.Port)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        var host = new PairingHost(
            listener,
            envelopeFactory,
            clock,
            PairingSecret.GenerateCode(),
            clock.UtcNow + PairingProtocol.Window);

        host._acceptLoop = host.AcceptLoopAsync();
        return host;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            // Cada par se atiende aparte: una caja que se conecta y no habla no puede dejar a la
            // siguiente esperando los 20 segundos del timeout.
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        using var perConnection = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        perConnection.CancelAfter(PairingProtocol.ConnectionTimeout);

        try
        {
            await using var stream = client.GetStream();
            var token = perConnection.Token;

            // El diálogo es SIEMPRE el mismo: saludo, prueba, respuesta. Incluso cuando ya se sabe
            // que se va a rechazar.
            //
            // Antes los rechazos por ventana vencida o intentos agotados se mandaban de una, sin
            // saludo, y el cliente —que estaba esperando un saludo— leía ese error como un mensaje
            // que no entendía. El operador veía "no se pudo interpretar la respuesta" cuando lo
            // que pasaba era, textualmente, que se le había vencido la ventana. Lo encontraron los
            // tests de transporte, no una tienda, que es donde uno quiere encontrar estas cosas.
            //
            // El sobre se arma ACÁ y no antes: hace falta la tienda para el saludo, y hay que
            // leerlo fresco en cada entrega.
            var envelope = await _envelopeFactory(token);
            var challenge = PairingSecret.GenerateChallenge();

            await PairingMessageChannel.SendAsync(
                stream,
                new PairingGreeting(
                    TerminalConfigurationEnvelope.CurrentVersion,
                    Convert.ToBase64String(challenge),
                    envelope.StoreId),
                token);

            var proof = await PairingMessageChannel.ReceiveAsync<PairingProof>(stream, token);

            // La ventana pudo vencer entre que se abrió y que este par se conectó.
            if (IsExpired)
            {
                await RejectAsync(stream, PairingProtocol.ErrorWindowClosed, token);
                return;
            }

            // Se comprueba ANTES de verificar la prueba: con los intentos agotados no se mira
            // siquiera si acertó, así que ni el código bueno revive un código quemado.
            if (AttemptsRemaining <= 0)
            {
                await RejectAsync(stream, PairingProtocol.ErrorNoAttemptsLeft, token);
                return;
            }

            if (proof is null || !TryDecodeBase64(proof.Proof, out var proofBytes)
                || !PairingSecret.VerifyProof(Code, challenge, proofBytes))
            {
                // Un intento fallido quema uno de los tres, venga de donde venga. Es lo que hace
                // que adivinar un código de un millón no sea una opción.
                var remaining = ConsumeAttempt();
                await RejectAsync(stream, PairingProtocol.ErrorInvalidCode, token, remaining);
                return;
            }

            // Acertó. Se reserva el nombre de caja que ESTE par va a tomar, para que el siguiente
            // reciba el siguiente y no el mismo. Host y receptor calculan lo mismo porque
            // SuggestNextTerminalId es determinista sobre la lista que va en el sobre.
            var delivered = WithReservations(envelope);
            Reserve(delivered.SuggestNextTerminalId());

            await PairingMessageChannel.SendAsync(
                stream,
                new PairingDelivery(
                    Convert.ToBase64String(
                        PairingSecret.Encrypt(Code, challenge, delivered.ToJson())),
                    Error: null,
                    AttemptsRemaining: AttemptsRemaining),
                token);

            lock (_gate)
            {
                _successfulTransfers++;
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Un par que se desconecta a mitad es normal —el operador cerró la pantalla, la red
            // parpadeó— y no puede tumbar al que está repartiendo.
        }
    }

    /// <summary>
    /// El sobre con los nombres de caja ya entregados en esta ventana sumados a los que la tienda
    /// ya tenía. Sin esto, tres cajas seguidas recibirían todas "CAJA-02".
    /// </summary>
    private TerminalConfigurationEnvelope WithReservations(TerminalConfigurationEnvelope envelope)
    {
        lock (_gate)
        {
            if (_reservedTerminalIds.Count == 0)
            {
                return envelope;
            }

            var combined = envelope.AssignedTerminalIds
                .Concat(_reservedTerminalIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return envelope with { AssignedTerminalIds = combined };
        }
    }

    private void Reserve(string terminalId)
    {
        if (string.IsNullOrWhiteSpace(terminalId))
        {
            return;
        }

        lock (_gate)
        {
            _reservedTerminalIds.Add(terminalId);
        }
    }

    private int ConsumeAttempt()
    {
        lock (_gate)
        {
            if (_attemptsRemaining > 0)
            {
                _attemptsRemaining--;
            }

            return _attemptsRemaining;
        }
    }

    private static Task RejectAsync(
        Stream stream,
        string error,
        CancellationToken cancellationToken,
        int attemptsRemaining = 0) =>
        PairingMessageChannel.SendAsync(
            stream,
            new PairingDelivery(Payload: null, error, attemptsRemaining),
            cancellationToken);

    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Cierra la ventana. Se llama al salir de la pantalla y al vencer el plazo — y tiene que
    /// poder llamarse dos veces sin quejarse, porque las dos cosas pueden pasar casi a la vez.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_shutdown.IsCancellationRequested)
        {
            await _shutdown.CancelAsync();
        }

        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Ya estaba cerrado.
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        _shutdown.Dispose();
    }
}
