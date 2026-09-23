using System.Globalization;
using System.Text;

namespace Permoda.Pay.Maui.HiPos.Results;

/// <summary>
/// Normaliza los campos del módulo TEF que terminan viajando al módulo fiscal de ICG y de ahí a
/// la DIAN.
/// <para>
/// Regla de fondo: <b>el módulo TEF nunca genera numeración fiscal</b>. El consecutivo, el rango
/// y la resolución DIAN son de HioPos y de <c>icg.hioposapifiscal</c>. No los pedimos, no los
/// inventamos y no los devolvemos — si el módulo externo intenta aportar numeración, choca con
/// el rango del POS y sale el error de "faltan los datos de rango de numeración de factura".
/// Nuestro rol es devolver el resultado del cobro y el texto del comprobante, y dejar que
/// HioPos arme el documento fiscal con su propia numeración.
/// </para>
/// <para>
/// Lo único que sí hay que normalizar es <c>AuthorizationId</c>, que es lo que efectivamente
/// llega al fiscal. Tiene reglas del manual de ICG que, si no se cumplen, hacen que el fiscal
/// rechace el documento.
/// </para>
/// <para>
/// Vive en un solo lugar y con tests a propósito: son reglas de un tercero con consecuencias
/// fiscales, y tenerlas duplicadas garantiza que tarde o temprano las copias divergen.
/// </para>
/// </summary>
public static class DianFieldSanitizer
{
    /// <summary>Tope del campo en el módulo fiscal: <c>varchar(40)</c>.</summary>
    public const int AuthorizationIdMaxLength = 40;

    /// <summary>
    /// Deja el identificador de la autorización en el formato que acepta el fiscal:
    /// <list type="bullet">
    /// <item>sin guiones, espacios ni ningún carácter no alfanumérico — la referencia de Ogloba
    /// puede traer separadores, y un GUID crudo es rechazo directo;</item>
    /// <item>truncado a 40 caracteres;</item>
    /// <item>nunca vacío: si no hay identificador se cae al consecutivo con padding a 6
    /// dígitos, porque un <c>AuthorizationId</c> en blanco también lo rechaza el fiscal.</item>
    /// </list>
    /// </summary>
    /// <param name="identifier">Referencia de la operación (referenceNumber / orderNumber de Ogloba).</param>
    /// <param name="fallbackNumber">
    /// Consecutivo de respaldo — el <c>TransactionId</c> que mandó HiPOS, por ejemplo. Solo se
    /// usa si no hay identificador. NO es numeración fiscal: es un identificador de la
    /// operación del TEF para que el campo no viaje vacío.
    /// </param>
    public static string AuthorizationId(string? identifier, long? fallbackNumber = null)
    {
        var cleaned = KeepAlphanumeric(identifier);

        if (cleaned.Length > 0)
        {
            return Truncate(cleaned);
        }

        var number = fallbackNumber is > 0 ? fallbackNumber.Value : 0;
        return number.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string KeepAlphanumeric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string value) =>
        value.Length <= AuthorizationIdMaxLength
            ? value
            : value[..AuthorizationIdMaxLength];
}
