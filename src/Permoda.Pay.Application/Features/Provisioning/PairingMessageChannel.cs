using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// Mensajes JSON, uno por línea, sobre un socket.
/// <para>
/// Un mensaje por línea y no un largo binario al principio: se puede leer con <c>nc</c> desde una
/// terminal cuando algo falle en una tienda, y eso vale más que ahorrar cuatro bytes en un
/// intercambio que ocurre una vez por caja en toda su vida.
/// </para>
/// </summary>
internal static class PairingMessageChannel
{
    private static readonly JsonSerializerOptions Options = new();

    public static async Task SendAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(message, Options) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Lee una línea y la deserializa. Devuelve <c>null</c> ante cualquier problema —conexión
    /// cortada, basura, mensaje demasiado largo— porque todos terminan igual para el operador y
    /// ninguno puede tumbar la app.
    /// </summary>
    public static async Task<T?> ReceiveAsync<T>(Stream stream, CancellationToken cancellationToken)
        where T : class
    {
        var buffer = new List<byte>(256);
        var single = new byte[1];

        while (buffer.Count < PairingProtocol.MaxMessageBytes)
        {
            int read;

            try
            {
                read = await stream.ReadAsync(single, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                return null;
            }

            // El par cerró antes de terminar la línea.
            if (read == 0)
            {
                return null;
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            buffer.Add(single[0]);
        }

        // Se pasó del tope sin cerrar la línea: se corta en vez de seguir creciendo.
        if (buffer.Count >= PairingProtocol.MaxMessageBytes)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(buffer.ToArray()), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
