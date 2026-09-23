using System.Text;
using Permoda.Pay.Application.Features.Provisioning;

namespace Permoda.Pay.Application.Tests.Features.Provisioning;

/// <summary>
/// El emparejamiento entre cajas de una misma tienda. Lo que se prueba acá no es "que funcione":
/// es que NO funcione cuando no debe, que es lo que protege la llave de producción de la tienda.
/// </summary>
public sealed class PairingSecretTests
{
    private const string Payload = """{"StoreId":"K00537","SubscriptionKey":"secreto"}""";

    [Fact]
    public void El_codigo_tiene_la_longitud_pedida_y_son_solo_digitos()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = PairingSecret.GenerateCode();

            Assert.Equal(PairingSecret.CodeLength, code.Length);
            Assert.True(code.All(char.IsAsciiDigit), $"'{code}' trae algo que no es dígito.");
        }
    }

    /// <summary>
    /// Un código que perdiera los ceros de la izquierda dejaría de tener la longitud anunciada, y
    /// el operador estaría tecleando cinco dígitos donde la pantalla le pide seis.
    /// </summary>
    [Fact]
    public void El_codigo_conserva_los_ceros_a_la_izquierda()
    {
        var codes = Enumerable.Range(0, 400).Select(_ => PairingSecret.GenerateCode()).ToArray();

        Assert.All(codes, code => Assert.Equal(PairingSecret.CodeLength, code.Length));
    }

    [Fact]
    public void Dos_codigos_seguidos_no_son_el_mismo()
    {
        var codes = Enumerable.Range(0, 100)
            .Select(_ => PairingSecret.GenerateCode())
            .Distinct()
            .Count();

        // Con 100 extracciones de un millón, repetir más de un par de veces delataría un
        // generador que no es aleatorio de verdad.
        Assert.True(codes > 95, $"Solo {codes} códigos distintos de 100: el generador no es aleatorio.");
    }

    [Fact]
    public void El_reto_cambia_en_cada_emparejamiento()
    {
        var a = PairingSecret.GenerateChallenge();
        var b = PairingSecret.GenerateChallenge();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Con_el_codigo_correcto_la_prueba_se_acepta()
    {
        var code = "427913";
        var challenge = PairingSecret.GenerateChallenge();

        var proof = PairingSecret.ComputeProof(code, challenge);

        Assert.True(PairingSecret.VerifyProof(code, challenge, proof));
    }

    [Fact]
    public void Con_el_codigo_equivocado_la_prueba_se_rechaza()
    {
        var challenge = PairingSecret.GenerateChallenge();
        var proof = PairingSecret.ComputeProof("427913", challenge);

        Assert.False(PairingSecret.VerifyProof("427914", challenge, proof));
    }

    /// <summary>
    /// La prueba de un emparejamiento no puede servir en otro. Si sirviera, quien capturó UNA
    /// conexión podría emparejarse cuando quisiera sin saber el código nunca.
    /// </summary>
    [Fact]
    public void La_prueba_de_un_reto_no_sirve_para_otro()
    {
        const string code = "427913";
        var proof = PairingSecret.ComputeProof(code, PairingSecret.GenerateChallenge());

        Assert.False(PairingSecret.VerifyProof(code, PairingSecret.GenerateChallenge(), proof));
    }

    /// <summary>
    /// La prueba viaja en claro. Si de ella se pudiera leer el código, todo lo demás sobra.
    /// </summary>
    [Fact]
    public void La_prueba_no_contiene_el_codigo()
    {
        const string code = "427913";
        var proof = PairingSecret.ComputeProof(code, PairingSecret.GenerateChallenge());

        Assert.True(
            proof.AsSpan().IndexOf(Encoding.UTF8.GetBytes(code)) < 0,
            "El código aparece en claro dentro de la prueba, que viaja por la red.");
    }

    [Fact]
    public void Lo_cifrado_con_un_codigo_se_descifra_con_ese_codigo()
    {
        const string code = "427913";
        var challenge = PairingSecret.GenerateChallenge();

        var sealed_ = PairingSecret.Encrypt(code, challenge, Payload);

        Assert.Equal(Payload, PairingSecret.Decrypt(code, challenge, sealed_));
    }

    [Fact]
    public void Con_el_codigo_equivocado_no_se_descifra()
    {
        var challenge = PairingSecret.GenerateChallenge();
        var sealed_ = PairingSecret.Encrypt("427913", challenge, Payload);

        Assert.Null(PairingSecret.Decrypt("000000", challenge, sealed_));
    }

    /// <summary>
    /// AES-GCM autentica además de cifrar. Un byte alterado en la red tiene que hacer fallar el
    /// descifrado — no entregar una configuración corrupta que la caja guardaría como buena.
    /// </summary>
    [Fact]
    public void Un_paquete_alterado_no_se_descifra()
    {
        const string code = "427913";
        var challenge = PairingSecret.GenerateChallenge();
        var sealed_ = PairingSecret.Encrypt(code, challenge, Payload);

        sealed_[^1] ^= 0xFF;

        Assert.Null(PairingSecret.Decrypt(code, challenge, sealed_));
    }

    [Fact]
    public void Un_paquete_truncado_no_tumba_la_app()
    {
        var challenge = PairingSecret.GenerateChallenge();

        Assert.Null(PairingSecret.Decrypt("427913", challenge, []));
        Assert.Null(PairingSecret.Decrypt("427913", challenge, new byte[10]));
        Assert.Null(PairingSecret.Decrypt("427913", challenge, new byte[28]));
    }

    /// <summary>
    /// El mismo texto cifrado dos veces tiene que dar paquetes distintos: si no, se ve desde fuera
    /// cuándo dos cajas recibieron exactamente la misma configuración.
    /// </summary>
    [Fact]
    public void Cifrar_dos_veces_lo_mismo_da_paquetes_distintos()
    {
        const string code = "427913";
        var challenge = PairingSecret.GenerateChallenge();

        var first = PairingSecret.Encrypt(code, challenge, Payload);
        var second = PairingSecret.Encrypt(code, challenge, Payload);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// La llave de la tienda no puede aparecer en claro dentro del paquete. Es la comprobación más
    /// tonta y la que más duele si algún día alguien "optimiza" el cifrado.
    /// </summary>
    [Fact]
    public void La_llave_de_la_tienda_no_viaja_legible()
    {
        const string key = "LLAVE-FALSA-SOLO-PARA-PRUEBAS-01";
        var challenge = PairingSecret.GenerateChallenge();

        var sealed_ = PairingSecret.Encrypt("427913", challenge, $$"""{"SubscriptionKey":"{{key}}"}""");

        Assert.True(
            sealed_.AsSpan().IndexOf(Encoding.UTF8.GetBytes(key)) < 0,
            "La subscription key aparece en claro dentro del paquete cifrado.");
    }
}
