using System.Security.Cryptography;
using System.Text;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// El código que el operador lee en una caja y escribe en otra, y todo lo que se deriva de él.
/// <para>
/// <b>El código no lleva la configuración: la autoriza.</b> Los datos van por la red; esto es solo
/// la prueba de que quien pide estaba parado frente a la caja que ya está configurada.
/// </para>
/// <para>
/// <b>Por qué el protocolo tiene un reto y no manda el paquete de una.</b> Si la caja emisora
/// entregara el sobre cifrado a quien se conecte, cualquiera podría guardarlo y probar los
/// 1.000.000 de códigos en su casa, sin límite de intentos y sin que nadie se entere. Con reto,
/// no sale un solo byte de configuración hasta que el otro lado demuestra que sabe el código, y
/// ese intento lo cuenta la caja emisora —que corta a los tres.
/// </para>
/// <para>
/// <b>Lo que esto NO protege, dicho sin adornos.</b> Quien esté capturando el tráfico de la tienda
/// durante la ventana y vea un emparejamiento exitoso, se lleva el reto y la prueba, y con eso
/// puede romper un código de 6 dígitos por fuerza bruta fuera de línea. Con las iteraciones de
/// abajo eso le cuesta del orden de un minuto en una GPU decente. No es teórico.
/// </para>
/// <para>
/// Se acepta a conciencia porque el atacante tiene que estar dentro de la red de la tienda,
/// capturando, justo en esos 10 minutos — y lo que obtiene es la llave de ESA tienda. Si algún día
/// eso deja de parecer aceptable, la palanca es <see cref="CodeLength"/>: 8 dígitos llevan ese
/// minuto a un par de horas, y no cambia nada más del diseño.
/// </para>
/// </summary>
public static class PairingSecret
{
    /// <summary>
    /// Dígitos del código. Seis es lo que se pidió: se lee de un vistazo y se teclea sin
    /// equivocarse, que es el punto de todo esto. Subirlo es la única palanca contra el ataque
    /// fuera de línea descrito arriba — cada dígito multiplica por diez el costo del atacante.
    /// </summary>
    public const int CodeLength = 6;

    /// <summary>
    /// Intentos antes de quemar el código. Tres es suficiente para un dedo torpe y ridículamente
    /// insuficiente para adivinar uno de un millón.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Iteraciones de PBKDF2 (recomendación OWASP para PBKDF2-HMAC-SHA256).
    /// <para>
    /// No se nota en el uso: se calcula UNA vez por emparejamiento, en una tablet que no está
    /// haciendo nada más. Lo que compra es que cada intento del atacante cueste lo mismo.
    /// </para>
    /// </summary>
    private const int Iterations = 310_000;

    private const int ChallengeSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int GcmNonceSizeBytes = 12;
    private const int GcmTagSizeBytes = 16;

    /// <summary>
    /// Un código nuevo, con <see cref="RandomNumberGenerator"/> y no con <c>Random</c>: este
    /// número es lo único que separa la llave de producción de la tienda de cualquiera en la red,
    /// y un generador predecible lo volvería adivinable sin necesidad de fuerza bruta.
    /// </summary>
    public static string GenerateCode()
    {
        var max = (int)Math.Pow(10, CodeLength);
        return RandomNumberGenerator.GetInt32(max).ToString($"D{CodeLength}");
    }

    /// <summary>Reto aleatorio. Va en claro por la red: su trabajo es no repetirse, no ser secreto.</summary>
    public static byte[] GenerateChallenge() =>
        RandomNumberGenerator.GetBytes(ChallengeSizeBytes);

    /// <summary>
    /// Deriva del código las DOS llaves del intercambio: una para probar que se sabe el código y
    /// otra para cifrar el sobre.
    /// <para>
    /// Van separadas a propósito. Usar la misma para las dos cosas significaría que la prueba
    /// —que viaja en claro— se calcula con la llave que protege la configuración.
    /// </para>
    /// </summary>
    private static (byte[] Proof, byte[] Encryption) DeriveKeys(string code, byte[] challenge)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(code.Trim()),
            challenge,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes * 2);

        return (material[..KeySizeBytes], material[KeySizeBytes..]);
    }

    /// <summary>
    /// Lo que la caja receptora manda para demostrar que sabe el código, sin decirlo.
    /// </summary>
    public static byte[] ComputeProof(string code, byte[] challenge)
    {
        var (proofKey, _) = DeriveKeys(code, challenge);
        return HMACSHA256.HashData(proofKey, challenge);
    }

    /// <summary>
    /// Verifica la prueba en tiempo fijo.
    /// <para>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> y no <c>SequenceEqual</c>: una
    /// comparación que corta en el primer byte distinto tarda distinto según cuántos acertó, y
    /// eso deja adivinar la prueba byte a byte midiendo el tiempo de respuesta.
    /// </para>
    /// </summary>
    public static bool VerifyProof(string code, byte[] challenge, byte[] candidateProof)
    {
        var expected = ComputeProof(code, challenge);
        return CryptographicOperations.FixedTimeEquals(expected, candidateProof);
    }

    /// <summary>
    /// Cifra el sobre con AES-GCM, que además de ocultar AUTENTICA: si algo en la red altera un
    /// byte, el descifrado falla en vez de entregar una configuración corrupta que la caja
    /// guardaría como buena.
    /// </summary>
    public static byte[] Encrypt(string code, byte[] challenge, string plaintext)
    {
        var (_, key) = DeriveKeys(code, challenge);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSizeBytes);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[GcmTagSizeBytes];

        using (var aes = new AesGcm(key, GcmTagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        // nonce | tag | ciphertext, en un solo arreglo para que el transporte mande un bloque y
        // no tres campos que alguien pueda reordenar sin darse cuenta.
        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        cipher.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    /// <summary>
    /// Descifra. Devuelve <c>null</c> si el código no era, si el paquete venía alterado o si
    /// llegó truncado — los tres casos terminan igual para el operador ("no se pudo copiar") y
    /// ninguno puede tumbar la app a mitad de la instalación de una tienda.
    /// </summary>
    public static string? Decrypt(string code, byte[] challenge, byte[] payload)
    {
        if (payload.Length <= GcmNonceSizeBytes + GcmTagSizeBytes)
        {
            return null;
        }

        var (_, key) = DeriveKeys(code, challenge);

        var nonce = payload[..GcmNonceSizeBytes];
        var tag = payload[GcmNonceSizeBytes..(GcmNonceSizeBytes + GcmTagSizeBytes)];
        var cipher = payload[(GcmNonceSizeBytes + GcmTagSizeBytes)..];
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, GcmTagSizeBytes);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(plain);
    }
}
