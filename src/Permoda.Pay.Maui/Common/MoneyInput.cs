using System.Globalization;

namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Único punto de verdad para leer y mostrar importes digitados por el cajero.
/// <para>
/// El campo muestra el monto con separador de miles (<c>50.000</c>) para que sea legible de un
/// vistazo, pero el valor que viaja a Ogloba tiene que ser el entero limpio. Tener dos criterios
/// —uno en la vista y otro en cada ViewModel— es exactamente como se cuela un error de escala en
/// un módulo que mueve dinero, así que ambos lados pasan por aquí.
/// </para>
/// <para>
/// Los importes son en PESOS COLOMBIANOS ENTEROS: el peso no se subdivide en la práctica y
/// Ogloba los recibe tal cual (ver docs/FLUJOS_DE_OPERACION.md §1). Por eso se rechaza cualquier parte decimal en
/// vez de redondearla en silencio.
/// </para>
/// </summary>
public static class MoneyInput
{
    private static readonly CultureInfo Colombia = new("es-CO");

    /// <summary>
    /// Lee lo que digitó el cajero, tolerando los separadores de miles que el propio campo
    /// inserta. Devuelve <c>false</c> si no hay un entero positivo válido.
    /// </summary>
    public static bool TryParsePesos(string? text, out long pesos)
    {
        pesos = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = Sanitize(text);

        if (digits.Length == 0)
        {
            return false;
        }

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out pesos);
    }

    /// <summary>Deja solo los dígitos: descarta separadores, espacios y el signo de moneda.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().TrimStart('0') is { Length: > 0 } trimmed
            ? trimmed
            : (builder.Length > 0 ? "0" : string.Empty);
    }

    /// <summary>Formatea para mostrar en el campo: <c>50000</c> → <c>50.000</c>.</summary>
    public static string Format(string? text) =>
        TryParsePesos(text, out var pesos) ? pesos.ToString("N0", Colombia) : string.Empty;

    /// <summary>Formatea un importe ya conocido: <c>50000</c> → <c>$50.000 COP</c>.</summary>
    public static string FormatWithCurrency(long pesos) => $"${pesos.ToString("N0", Colombia)} COP";
}
