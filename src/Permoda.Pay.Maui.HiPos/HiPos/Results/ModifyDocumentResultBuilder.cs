using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Permoda.Pay.Maui.HiPos.Results;

/// <summary>
/// Arma el XML <c>ModifyDocumentResult</c> con el que el módulo enriquece el medio de pago
/// Ogloba del documento de venta de HiPOS.
///
/// <para>
/// REGLA CRÍTICA: SOLO se tocan <c>PaymentMeans</c>. Nunca se agregan líneas de producto, datos
/// de empresa, cabecera ni totales. Si el resultado se parece a un documento nuevo, HioPosCloud
/// lo interpreta como tal y lo reenvía a la DIAN — doble envío. El documento sigue siendo de
/// HiPOS; nosotros solo le colgamos el medio de pago.
/// </para>
///
/// <para>
/// De ahí sale la regla fiscal del módulo: acá NO viaja consecutivo, ni rango, ni resolución.
/// El único identificador que llega al módulo fiscal es <c>AuthorizationId</c>, y va saneado
/// por <see cref="DianFieldSanitizer"/>.
/// </para>
///
/// <para>
/// Antes se devolvía el extra <c>ModifyDocumentResult</c> con el número de línea pelado en vez
/// de este XML, y solo cuando HiPOS mandaba <c>PaymentMeanLineNumber</c> — que no lo manda. O
/// sea: no se enviaba nunca, el medio de pago quedaba sin enriquecer y el módulo fiscal
/// terminaba en "no cuenta con folios asociados".
/// </para>
/// </summary>
public sealed class ModifyDocumentResultBuilder
{
    public string Build(
        string paymentMeanId,
        string type,
        string lineNumber,
        string amount,
        string authorizationId,
        string? transactionId,
        IReadOnlyList<(string Key, string Value)>? customFields = null)
    {
        var standard = new StringBuilder()
            .Append(Field("PaymentMeanId", paymentMeanId))
            .Append(Field("Type", type))
            .Append(Field("LineNumber", lineNumber))
            .Append(Field("Amount", amount))
            .Append(Field("AuthorizationId", authorizationId))
            .Append(Field("TransactionId", transactionId))
            .ToString();

        var custom = customFields is null
            ? string.Empty
            : string.Concat(customFields.Select(field => CustomField(field.Key, field.Value)));

        var customBlock = custom.Length == 0
            ? string.Empty
            : $"      <CustomPaymentMeanFields>\n{custom}      </CustomPaymentMeanFields>\n";

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<ModifyDocumentResult>\n"
            + "  <PaymentMeans>\n"
            + "    <PaymentMean>\n"
            + standard
            + customBlock
            + "    </PaymentMean>\n"
            + "  </PaymentMeans>\n"
            + "</ModifyDocumentResult>";
    }

    /// <summary>Campo estándar. Se omite si viene vacío.</summary>
    private static string Field(string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"      <PaymentMeanField Key=\"{Escape(key)}\">{Escape(value)}</PaymentMeanField>\n";

    /// <summary>Campo propio del medio. Se omite si viene vacío.</summary>
    private static string CustomField(string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"        <CustomPaymentMeanField Key=\"{Escape(key)}\">{Escape(value)}</CustomPaymentMeanField>\n";

    /// <summary>
    /// Escapa el XML y aplana saltos de línea: el valor viaja como atributo o texto dentro de un
    /// extra del intent, y un salto de línea crudo rompe el parseo del lado de HiPOS.
    /// </summary>
    private static string Escape(string? input) =>
        string.IsNullOrEmpty(input)
            ? string.Empty
            : input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("\r", string.Empty)
                .Replace("\n", " ");
}
