using System.Net.Sockets;

namespace Permoda.Pay.Infrastructure.Ogloba;

/// <summary>
/// Cuándo vale la pena reintentar una llamada a Ogloba, y —sobre todo— cuándo NO.
/// <para>
/// <b>Reintentar mal en un módulo de pagos cobra dos veces.</b> Esa es la única frase que hay que
/// tener presente al tocar este archivo. El caso peligroso no es el obvio: es el <i>timeout</i>.
/// Cuando una redención expira, la petición YA SALIÓ y Ogloba pudo haberla aplicado; lo que se
/// perdió fue la respuesta, no la operación. Reintentarla descuenta el saldo del bono dos veces y
/// el cliente se queda sin plata que sí gastó.
/// </para>
/// <para>
/// Por eso las operaciones que mueven plata solo se reintentan cuando se puede <b>demostrar</b>
/// que la petición nunca llegó al servidor: no hubo con quién conectarse, o el nombre no resolvió.
/// Ahí no hay nada que se haya podido aplicar del otro lado. Cualquier otro fallo —incluido el
/// timeout y una conexión cortada a mitad— es ambiguo, y lo ambiguo NO se reintenta: se responde
/// resultado incierto, que es lo que dispara la recuperación al arrancar. «No sé qué pasó, lo
/// averiguo» es recuperable; «lo hago otra vez por si acaso» no.
/// </para>
/// <para>
/// Las lecturas son el caso fácil: consultar un saldo o un catálogo no deja nada a medias, así que
/// se reintentan ante cualquier fallo de red. Ahí el reintento convierte un parpadeo de la red de
/// la tienda en una demora de medio segundo en vez de un error en pantalla.
/// </para>
/// </summary>
/// <remarks>
/// Es pública y no interna para que sus pruebas puedan fijarla directamente. Esa decisión importa:
/// la regla que impide reintentar una redención con timeout es lo que separa un cobro de dos, y
/// tiene que poder verificarse sin pasar por el proveedor entero.
/// </remarks>
public static class OglobaRetryPolicy
{
    /// <summary>
    /// Intentos totales de una LECTURA. Tres es el punto donde deja de compensar: si dos
    /// reintentos con espera no bastaron, no es un parpadeo, es que la red está caída — y seguir
    /// insistiendo solo hace esperar al cajero frente al cliente.
    /// </summary>
    public const int ReadAttempts = 3;

    /// <summary>
    /// Intentos totales de una operación que MUEVE PLATA. Dos, y solo por el camino que demuestra
    /// que la petición nunca salió.
    /// </summary>
    public const int WriteAttempts = 2;

    /// <summary>
    /// Espera antes del intento <paramref name="attempt"/> (el primer reintento es el 1).
    /// <para>
    /// Crece para no golpear una pasarela que ya está sufriendo, pero se queda corta a propósito:
    /// al otro lado hay un cajero con un cliente enfrente, y una espera de varios segundos se
    /// siente peor que un error claro. El tope total de reintentos queda por debajo del segundo.
    /// </para>
    /// </summary>
    public static TimeSpan Delay(int attempt) =>
        TimeSpan.FromMilliseconds(attempt <= 1 ? 200 : 600);

    /// <summary>
    /// Una lectura se reintenta ante cualquier fallo de red o corte por tiempo: no deja nada a
    /// medias del otro lado.
    /// </summary>
    public static bool ShouldRetryRead(Exception exception) =>
        exception is HttpRequestException or OperationCanceledException;

    /// <summary>
    /// Una escritura se reintenta SOLO si se puede demostrar que nunca llegó al servidor.
    /// <para>
    /// Fíjate en lo que NO está: <see cref="OperationCanceledException"/>. Un timeout significa
    /// que la petición salió y no sabemos qué pasó del otro lado, y ese es exactamente el caso en
    /// el que reintentar cobra dos veces.
    /// </para>
    /// </summary>
    public static bool ShouldRetryWrite(Exception exception) =>
        exception is HttpRequestException http && NeverReachedTheServer(http);

    /// <summary>
    /// <c>true</c> solo cuando el fallo ocurrió ANTES de que un solo byte llegara al servidor.
    /// <para>
    /// Se mira el <see cref="SocketException"/> de adentro y se aceptan únicamente los códigos que
    /// significan «no hubo con quién hablar»: nadie escuchando, el nombre no resolvió, la red no
    /// tiene salida. Cualquier otro —una conexión reseteada, por ejemplo— pudo ocurrir DESPUÉS de
    /// que Ogloba recibiera y aplicara la operación, así que se trata como ambiguo.
    /// </para>
    /// <para>
    /// Ante la duda, <c>false</c>. El costo de equivocarse hacia el reintento es cobrarle dos veces
    /// a un cliente; el costo de equivocarse hacia el no-reintento es un mensaje de error y que el
    /// cajero lo intente de nuevo a mano, viendo lo que hace.
    /// </para>
    /// </summary>
    private static bool NeverReachedTheServer(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket.SocketErrorCode
                    is SocketError.ConnectionRefused
                    or SocketError.HostNotFound
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    or SocketError.NetworkDown
                    or SocketError.TryAgain;
            }
        }

        return false;
    }
}
