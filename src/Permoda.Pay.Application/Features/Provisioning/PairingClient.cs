using System.Net.Sockets;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>Cómo terminó un intento de copiar la configuración desde otra caja.</summary>
public enum PairingOutcome
{
    Succeeded,

    /// <summary>No se pudo llegar a la otra caja: no está repartiendo, o la red no deja.</summary>
    Unreachable,

    /// <summary>El código no era. Quedan intentos (ver <see cref="PairingResult.AttemptsRemaining"/>).</summary>
    InvalidCode,

    /// <summary>Se agotaron los tres intentos: hay que generar un código nuevo en la otra caja.</summary>
    NoAttemptsLeft,

    /// <summary>La ventana de 10 minutos venció.</summary>
    WindowClosed,

    /// <summary>Llegó algo que no se pudo interpretar. Casi siempre, versiones distintas del APK.</summary>
    Unintelligible
}

/// <param name="Envelope">La configuración recibida. Solo viene con <see cref="PairingOutcome.Succeeded"/>.</param>
/// <param name="StoreId">
/// La tienda de la caja emisora. Llega ANTES de validar el código, para poder confirmarle al
/// operador de qué tienda está copiando — en un centro comercial puede haber otra KOAJ en la red.
/// </param>
public sealed record PairingResult(
    PairingOutcome Outcome,
    TerminalConfigurationEnvelope? Envelope = null,
    string? StoreId = null,
    int AttemptsRemaining = 0)
{
    public bool IsSuccess => Outcome == PairingOutcome.Succeeded && Envelope is not null;
}

/// <summary>
/// La caja NUEVA pidiéndole la configuración a una que ya la tiene.
/// <para>
/// Nunca lanza: cualquier problema sale como un <see cref="PairingOutcome"/>. Esto corre mientras
/// alguien está montando una tienda, y una excepción sin atrapar ahí es una app que se cierra
/// dejando la caja a medio configurar.
/// </para>
/// </summary>
public static class PairingClient
{
    public static async Task<PairingResult> FetchAsync(
        string host,
        string code,
        CancellationToken cancellationToken,
        int port = PairingProtocol.Port)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PairingProtocol.ConnectionTimeout);
        var token = timeout.Token;

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, token);
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return new PairingResult(PairingOutcome.Unreachable);
        }

        try
        {
            await using var stream = client.GetStream();

            var greeting = await PairingMessageChannel.ReceiveAsync<PairingGreeting>(stream, token);

            if (greeting is null || string.IsNullOrWhiteSpace(greeting.Challenge))
            {
                return new PairingResult(PairingOutcome.Unintelligible);
            }

            // Una caja con otra versión del APK: se corta acá con un motivo claro en vez de
            // intentar hablar un idioma que no es y terminar con media configuración.
            if (greeting.Version != TerminalConfigurationEnvelope.CurrentVersion)
            {
                return new PairingResult(
                    PairingOutcome.Unintelligible, StoreId: greeting.StoreId);
            }

            byte[] challenge;

            try
            {
                challenge = Convert.FromBase64String(greeting.Challenge);
            }
            catch (FormatException)
            {
                return new PairingResult(PairingOutcome.Unintelligible);
            }

            await PairingMessageChannel.SendAsync(
                stream,
                new PairingProof(Convert.ToBase64String(PairingSecret.ComputeProof(code, challenge))),
                token);

            var delivery = await PairingMessageChannel.ReceiveAsync<PairingDelivery>(stream, token);

            if (delivery is null)
            {
                return new PairingResult(PairingOutcome.Unintelligible, StoreId: greeting.StoreId);
            }

            if (delivery.Error is not null)
            {
                return new PairingResult(
                    delivery.Error switch
                    {
                        PairingProtocol.ErrorInvalidCode => PairingOutcome.InvalidCode,
                        PairingProtocol.ErrorNoAttemptsLeft => PairingOutcome.NoAttemptsLeft,
                        PairingProtocol.ErrorWindowClosed => PairingOutcome.WindowClosed,
                        _ => PairingOutcome.Unintelligible
                    },
                    StoreId: greeting.StoreId,
                    AttemptsRemaining: delivery.AttemptsRemaining);
            }

            if (string.IsNullOrWhiteSpace(delivery.Payload))
            {
                return new PairingResult(PairingOutcome.Unintelligible, StoreId: greeting.StoreId);
            }

            byte[] payload;

            try
            {
                payload = Convert.FromBase64String(delivery.Payload);
            }
            catch (FormatException)
            {
                return new PairingResult(PairingOutcome.Unintelligible, StoreId: greeting.StoreId);
            }

            var json = PairingSecret.Decrypt(code, challenge, payload);
            var envelope = TerminalConfigurationEnvelope.FromJson(json);

            // Descifró pero no se entiende: el sobre venía de otra versión, o alterado. No se
            // guarda nada a medias.
            return envelope is null
                ? new PairingResult(PairingOutcome.Unintelligible, StoreId: greeting.StoreId)
                : new PairingResult(
                    PairingOutcome.Succeeded,
                    envelope,
                    greeting.StoreId,
                    delivery.AttemptsRemaining);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return new PairingResult(PairingOutcome.Unreachable);
        }
    }
}
