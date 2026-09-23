using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// Encontrar a la caja que está repartiendo, sin que nadie teclee una IP.
/// <para>
/// <b>Es un atajo, no un requisito.</b> El descubrimiento va por difusión UDP, y la difusión puede
/// estar bloqueada en la red de una tienda AUNQUE las cajas se vean entre sí — que dos equipos se
/// respondan el ping no dice nada sobre si el switch o el punto de acceso deja pasar broadcast.
/// Son cosas distintas y se configuran por separado.
/// </para>
/// <para>
/// Por eso la pantalla emisora muestra SIEMPRE su IP: si esto no encuentra nada, el operador la
/// teclea y el emparejamiento funciona igual. Un atajo que falla en silencio y deja al que instala
/// sin salida sería peor que no tenerlo.
/// </para>
/// </summary>
public static class PairingDiscovery
{
    /// <summary>Puerto de la difusión. Distinto al del emparejamiento: son protocolos distintos.</summary>
    public const int DiscoveryPort = 47114;

    private const string Probe = "TEFOGLOBA-QUIEN-REPARTE";
    private const string Reply = "TEFOGLOBA-YO-REPARTO";

    /// <summary>Cuánto se espera una respuesta antes de rendirse y pedir la IP a mano.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Atiende las preguntas "¿quién reparte?" mientras la ventana esté abierta.
    /// <para>
    /// La respuesta dice SOLO "acá estoy" y la tienda. Ni el código ni nada de la configuración:
    /// esto contesta a cualquiera en la red, así que no puede revelar nada que importe.
    /// </para>
    /// </summary>
    public static async Task RespondAsync(string storeId, CancellationToken cancellationToken)
    {
        using var socket = new UdpClient();

        try
        {
            socket.EnableBroadcast = true;
            socket.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch (SocketException)
        {
            // El puerto está ocupado o la plataforma no deja. No es fatal: el operador teclea la IP.
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult request;

            try
            {
                request = await socket.ReceiveAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            if (Encoding.UTF8.GetString(request.Buffer) != Probe)
            {
                continue;
            }

            var answer = Encoding.UTF8.GetBytes($"{Reply}|{storeId}");

            try
            {
                await socket.SendAsync(answer, request.RemoteEndPoint, cancellationToken);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                // Se perdió la respuesta. El operador teclea la IP y sigue.
            }
        }
    }

    /// <summary>
    /// Pregunta quién reparte. Devuelve la IP y la tienda, o <c>null</c> si nadie contestó — que
    /// NO significa que no haya nadie: puede ser la difusión bloqueada.
    /// </summary>
    public static async Task<(string Host, string StoreId)?> FindAsync(CancellationToken cancellationToken)
    {
        using var socket = new UdpClient { EnableBroadcast = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            await socket.SendAsync(
                Encoding.UTF8.GetBytes(Probe),
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort),
                timeout.Token);

            while (!timeout.Token.IsCancellationRequested)
            {
                var response = await socket.ReceiveAsync(timeout.Token);
                var text = Encoding.UTF8.GetString(response.Buffer);

                if (!text.StartsWith(Reply, StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = text.Split('|');
                return (response.RemoteEndPoint.Address.ToString(), parts.Length > 1 ? parts[1] : string.Empty);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Nadie contestó a tiempo, o la red no deja difundir.
        }

        return null;
    }

    /// <summary>
    /// La IP de esta caja en la red de la tienda, para mostrarla en pantalla como respaldo.
    /// <para>
    /// Se averigua "conectando" un socket UDP a una dirección externa: no manda un solo byte ni
    /// necesita internet, solo hace que el sistema elija por cuál interfaz saldría — que es
    /// exactamente la que las otras cajas van a poder alcanzar. Recorrer las interfaces a mano
    /// devuelve varias (Wi-Fi, datos, virtuales) y no dice cuál sirve.
    /// </para>
    /// </summary>
    public static string? LocalAddress()
    {
        try
        {
            using var probe = new UdpClient();
            probe.Connect("10.255.255.255", 65530);
            return (probe.Client.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            return null;
        }
    }
}
