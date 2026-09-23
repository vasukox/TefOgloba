using System.Globalization;
using System.Text;

namespace Permoda.Pay.Application;

/// <summary>
/// Concatena la cédula y el nombre del cliente en el formato "CC-NombreApellido" que viaja
/// a Ogloba dentro del campo <c>note</c> (activación física, vía /activation) o <c>message</c>
/// (activación virtual, vía /orderCreation).
/// <example>
/// "1011086580" + "Andrés Felipe Díaz Bernal" → "1011086580-AndresFelipeDiazBernal"
/// </example>
/// <para>
/// Reglas:
/// <list type="bullet">
/// <item>Tildes removidas (<c>Andrés</c> → <c>Andres</c>).</item>
/// <item>Mayúscula solo en la primera letra de cada palabra (PascalCase sin tildes).</item>
/// <item>Sin espacios entre palabras (<c>AndresFelipeDiazBernal</c>).</item>
/// <item>Si no hay nombre, devuelve solo el CC. Si no hay CC, devuelve solo el nombre.</item>
/// <item>Si ambos están vacíos, devuelve cadena vacía.</item>
/// </list>
/// </para>
/// </summary>
public static class CustomerMobileNoBuilder
{
    /// <summary>
    /// Longitud máxima objetivo del string resultante. El campo <c>message</c> de /orderCreation
    /// admite hasta 100 chars y <c>note</c> de /activation hasta 200; el límite conservador
    /// garantiza que cabe en ambos. El nombre se trunca si fuera necesario, conservando el CC
    /// completo y el separador.
    /// </summary>
    public const int MaxLength = 100;

    public static string Build(string? documentNumber, string? fullName)
    {
        var cc = (documentNumber ?? string.Empty).Trim();
        var name = ToPascalCaseNoAccents(fullName ?? string.Empty);

        if (cc.Length == 0 && name.Length == 0)
        {
            return string.Empty;
        }

        if (cc.Length == 0)
        {
            return Truncate(name, MaxLength);
        }

        if (name.Length == 0)
        {
            return Truncate(cc, MaxLength);
        }

        var combined = $"{cc}-{name}";
        return Truncate(combined, MaxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string ToPascalCaseNoAccents(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var upperNext = true;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            // Quita diacríticos (tilde de la 'á', diéresis de la 'ü', etc.).
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(ch))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                upperNext = false;
            }
            else
            {
                // Cualquier separador (espacio, guión, etc.) marca el inicio de la siguiente palabra.
                upperNext = true;
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
