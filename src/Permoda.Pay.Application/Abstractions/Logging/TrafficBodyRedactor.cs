using System.Text.RegularExpressions;

namespace Permoda.Pay.Application.Abstractions.Logging;

/// <summary>
/// Tapa los datos sensibles de los cuerpos que se registran del diálogo con Ogloba.
/// <para>
/// El PAN ya se enmascara antes de tocar disco en la base cifrada. En RAM no se enmascaraba
/// nada: la bitácora de tráfico guarda petición y respuesta ÍNTEGRAS —seriales completos,
/// cédulas y nombres— y esa bitácora es exportable a XLSX desde la pantalla de administración.
/// El mismo dato que en la base está protegido salía en claro por ahí.
/// </para>
/// <para>
/// Se aplica <b>solo en producción</b>. En sandbox los cuerpos literales son la evidencia de
/// certificación (docs/PRUEBAS_Y_CERTIFICACION.md §5) y taparlos rompería el procedimiento con
/// Ogloba. Producción no certifica nada: ahí el cuerpo literal solo es riesgo.
/// </para>
/// </summary>
public static partial class TrafficBodyRedactor
{
    /// <summary>
    /// Cualquier corrida larga de dígitos: seriales de bono (16), cédulas, teléfonos. Se deja el
    /// principio y el final —lo que sirve para reconocer de cuál se habla al rastrear un caso— y
    /// se tapa el medio, que es lo que lo vuelve utilizable.
    /// </summary>
    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongDigitRun();

    /// <summary>
    /// Campos de texto libre donde viaja el nombre del cliente. No son números, así que la regla
    /// de dígitos no los alcanza y hay que nombrarlos.
    /// </summary>
    [GeneratedRegex(
        @"""(receiverMobileNo|receiverName|senderName|customerName|note|message|email)""\s*:\s*""[^""]*""",
        RegexOptions.IgnoreCase)]
    private static partial Regex FreeTextFields();

    public static string Redact(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var masked = LongDigitRun().Replace(body, MaskDigits);
        return FreeTextFields().Replace(masked, MaskFreeText);
    }

    /// <summary>
    /// <c>1138170025515937</c> → <c>113817******5937</c>. Misma forma que la máscara de la base
    /// cifrada, para que un serial se vea igual en los dos sitios y nadie dude si es el mismo.
    /// </summary>
    private static string MaskDigits(Match match)
    {
        var digits = match.Value;

        // Con menos de 11 no queda nada que tapar entre el prefijo y el sufijo: se tapa entero.
        if (digits.Length < 11)
        {
            return new string('*', digits.Length);
        }

        return string.Concat(
            digits[..6],
            new string('*', digits.Length - 10),
            digits[^4..]);
    }

    private static string MaskFreeText(Match match)
    {
        var separator = match.Value.IndexOf(':');
        return separator < 0
            ? match.Value
            : string.Concat(match.Value[..separator], ":\"***\"");
    }
}
