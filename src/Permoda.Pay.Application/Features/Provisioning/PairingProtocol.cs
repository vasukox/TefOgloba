using System.Text.Json.Serialization;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// El diálogo entre dos cajas de la misma tienda. Tres mensajes, en este orden:
/// <code>
///   caja nueva  →  se conecta
///   caja vieja  →  Saludo   { reto, tienda }
///   caja nueva  →  Prueba   { prueba }
///   caja vieja  →  Entrega  { sobre cifrado }   ...o   { error, intentosRestantes }
/// </code>
/// <para>
/// El orden no es casual: <b>la configuración es lo ÚLTIMO que se manda</b>, y solo después de
/// que el otro lado demostró saber el código. Si el sobre viajara antes, cualquiera podría
/// conectarse, guardarlo y probar el millón de códigos en su casa — sin límite de intentos y sin
/// que la tienda se entere. Con este orden, el que cuenta los intentos es quien tiene el secreto.
/// </para>
/// </summary>
public static class PairingProtocol
{
    /// <summary>
    /// Puerto del emparejamiento. Está en el rango dinámico (49152 no lo incluye, pero sí está
    /// fuera de los puertos registrados) para no chocar con nada del POS ni de la terminal.
    /// </summary>
    public const int Port = 47113;

    /// <summary>
    /// Cuánto vive la ventana. Diez minutos es lo que se pidió: alcanza para caminar la tienda y
    /// configurar las cajas que haya, y es poco para que alguien la encuentre abierta por azar.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Cuánto se espera a un par que se conectó y no habla. Sin esto, alguien que abra conexiones
    /// y las deje colgadas bloquea el emparejamiento de las cajas de verdad.
    /// </summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Tope de lo que se acepta leer en un mensaje. Lo que llega viene de la red: sin un tope,
    /// un par malicioso manda bytes para siempre y tumba la caja por memoria a mitad de una
    /// instalación.
    /// </summary>
    public const int MaxMessageBytes = 512 * 1024;

    public const string ErrorInvalidCode = "invalid_code";
    public const string ErrorNoAttemptsLeft = "no_attempts_left";
    public const string ErrorWindowClosed = "window_closed";
}

/// <summary>Lo primero que dice la caja configurada. Nada de esto es secreto.</summary>
/// <param name="Challenge">
/// Reto en Base64. Va en claro a propósito: su trabajo es no repetirse nunca, no ser secreto.
/// </param>
/// <param name="StoreId">
/// La tienda. Se manda ANTES del código para que el operador confirme que se está copiando de la
/// tienda correcta — en un centro comercial puede haber otra KOAJ en la misma red.
/// </param>
public sealed record PairingGreeting(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("storeId")] string StoreId);

/// <summary>La caja nueva demuestra que sabe el código, sin decirlo.</summary>
public sealed record PairingProof(
    [property: JsonPropertyName("proof")] string Proof);

/// <summary>
/// El cierre: o el sobre cifrado, o el motivo. Nunca los dos.
/// </summary>
/// <param name="AttemptsRemaining">
/// Cuántos intentos quedan. Se informa a propósito: el operador tiene que saber que le quedan dos
/// antes de que el código se queme, no descubrirlo cuando ya se quemó.
/// </param>
public sealed record PairingDelivery(
    [property: JsonPropertyName("payload")] string? Payload,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("attemptsRemaining")] int AttemptsRemaining);
