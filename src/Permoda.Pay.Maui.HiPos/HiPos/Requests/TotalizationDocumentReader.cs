using System.Xml.Linq;

namespace Permoda.Pay.Maui.HiPos.Requests;

/// <summary>
/// Saca del documento de venta de HiPOS la referencia de la redención que hay que deshacer.
/// <para>
/// Se usa en TOTALIZATION_CANCELED: cuando el cajero cancela la totalización, HiPOS nos pasa el
/// documento y nosotros tenemos que encontrar ahí la línea de pago del bono para devolverle el
/// saldo antes de que el POS suelte la venta.
/// </para>
/// <para>
/// El ejemplo que dio ICG lee la referencia de <c>FreeField1</c> y busca la línea comparando
/// <c>PaymentMeanId</c> contra el texto "OGLOBA". Ninguna de las dos cosas aplica acá:
/// </para>
/// <list type="bullet">
/// <item><c>PaymentMeanId</c> es un NÚMERO (1000037 en la terminal de KOAJ, medido el
/// 2026-09-01). Compararlo contra "OGLOBA" no coincide nunca. El nombre vive en
/// <c>Description</c> y <c>PaymenMeanName</c>.</item>
/// <item>La referencia no va en un campo libre: el módulo la escribe en <c>AuthorizationId</c>,
/// que es donde el módulo fiscal la busca para la DIAN. Ver ModifyDocumentResultBuilder.</item>
/// </list>
/// </summary>
public static class TotalizationDocumentReader
{
    /// <summary>
    /// Nombre con el que HiPOS rotula nuestra forma de pago en el documento. Es el mismo que
    /// devuelve GET_CUSTOM_PARAMS, así que se mueven juntos.
    /// </summary>
    private const string PaymentMeanName = "OGLOBA";

    /// <summary>
    /// <c>PaymenMeanName</c> va SIN la "t" — así lo escribe ICG en su esquema. No es un typo
    /// nuestro y corregirlo haría que el campo no se encuentre.
    /// </summary>
    private static readonly string[] NameKeys = ["Description", "PaymenMeanName", "PaymentMeanName"];

    /// <summary>
    /// Tipos de documento que la API TEF 4.0 §DocumentData enumera en
    /// <c>Header.DocumentTypeId</c>: 1 Tiquet, 2 Factura, 3 Abono tiquet, 4 Abono factura,
    /// 5 Compra, 6 Tiquet no impreso, 7 Invitación.
    /// <para>
    /// El <b>28</b> no está en esa lista y sin embargo es el que llegó: medido en la caja
    /// 037 T.KOAJ CALLE 18 MONTEVIDEO el 2026-09-08, un abono real llegó con
    /// <c>DocumentTypeId=28</c>, serie K50H. Se aceptó como venta normal porque 28 no estaba
    /// acá, y el abono se hizo. Por eso esta lista ya NO es el criterio principal — es el
    /// respaldo. Ver <see cref="IsCreditNote"/>.
    /// </para>
    /// </summary>
    private static readonly string[] CreditNoteDocumentTypes = ["3", "4", "28"];

    private const string DocumentTypeIdKey = "DocumentTypeId";
    private const string NetAmountKey = "NetAmount";

    /// <summary>
    /// El documento devuelve plata: es una NOTA DE CRÉDITO (abono), no una venta.
    /// <para>
    /// Manda el SIGNO del importe, no el tipo de documento. Un abono trae <c>NetAmount</c>
    /// negativo —medido: <c>-119700,0000</c>— porque la plata va hacia el cliente; una venta en
    /// curso, de la que el cajero solo quiere soltar la línea de pago del bono, lo trae
    /// positivo. Las dos llegan como <c>REFUND</c> y hay que separarlas.
    /// </para>
    /// <para>
    /// Se eligió el signo y no la lista de tipos porque la lista nos falló: HiPOS mandó un 28
    /// que el contrato no enumera, no lo reconocimos, y el abono se hizo. La lista sigue como
    /// segunda comprobación, pero el criterio que no depende de conocer de antemano cada número
    /// que ICG decida usar es el importe.
    /// </para>
    /// <para>
    /// Devuelve <c>null</c> cuando no se puede determinar —documento ausente o ilegible—, para
    /// que quien llama decida con conocimiento en vez de recibir un <c>false</c> que parece un
    /// dato y no lo es.
    /// </para>
    /// </summary>
    public static bool? IsCreditNote(string? documentXml)
    {
        var type = ReadHeaderField(documentXml, DocumentTypeIdKey);
        var netAmount = ReadHeaderField(documentXml, NetAmountKey);

        if (type is null && netAmount is null)
        {
            return null;
        }

        // Solo interesa el signo, así que se mira el carácter y no se parsea el número: el
        // documento trae coma decimal ("−119700,0000") y parsearlo obligaría a fijar una cultura
        // para responder algo que ya está en el primer carácter.
        if (netAmount is not null && netAmount.StartsWith('-'))
        {
            return true;
        }

        return type is not null && Array.IndexOf(CreditNoteDocumentTypes, type) >= 0;
    }

    /// <summary>Valor crudo de <c>DocumentTypeId</c>, para el log. <c>null</c> si no viene.</summary>
    public static string? ReadDocumentTypeId(string? documentXml) =>
        ReadHeaderField(documentXml, DocumentTypeIdKey);

    /// <summary>
    /// Valor crudo de un <c>HeaderField</c> por su <c>Key</c>. <c>null</c> si el documento no
    /// llega, no parsea, o no trae ese campo.
    /// </summary>
    private static string? ReadHeaderField(string? documentXml, string key)
    {
        if (string.IsNullOrWhiteSpace(documentXml))
        {
            return null;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(documentXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        foreach (var field in document.Descendants("HeaderField"))
        {
            if (string.Equals(field.Attribute("Key")?.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                var value = field.Value?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>Lo que hay que deshacer: una línea de pago con bono ya cobrada.</summary>
    /// <param name="ReferenceNumber">Referencia de Ogloba, con la que se anula.</param>
    /// <param name="AmountText">Importe tal como lo escribió HiPOS. Solo para el log.</param>
    /// <param name="CardNumber">PAN enmascarado, si el documento lo trae. Solo para el log.</param>
    public sealed record OglobaPaymentLine(
        string ReferenceNumber,
        string? AmountText,
        string? CardNumber);

    /// <summary>
    /// Busca la línea de pago del bono. Devuelve <c>null</c> cuando no hay ninguna — que es el
    /// caso normal: TOTALIZATION_CANCELED llega en TODA totalización cancelada, se haya pagado
    /// con bono o no. No encontrar nada significa "no hay nada que deshacer", no un error.
    /// </summary>
    public static OglobaPaymentLine? FindOglobaPayment(string? documentXml)
    {
        if (string.IsNullOrWhiteSpace(documentXml))
        {
            return null;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(documentXml);
        }
        catch (System.Xml.XmlException)
        {
            // Un documento ilegible no puede tumbar la cancelación: HiPOS espera respuesta igual.
            // Quien llama lo reporta; acá simplemente no hay línea que devolver.
            return null;
        }

        foreach (var mean in document.Descendants("PaymentMean"))
        {
            var fields = ReadFields(mean);

            if (!IsOgloba(fields))
            {
                continue;
            }

            var reference = Value(fields, "AuthorizationId");

            // Sin referencia no se puede anular en Ogloba. Puede pasar si la línea la escribió
            // otro proceso, o si el cobro falló y HiPOS igual dejó la línea a medias.
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            return new OglobaPaymentLine(
                reference.Trim(),
                Value(fields, "Amount"),
                Value(fields, "CardNum"));
        }

        return null;
    }

    private static Dictionary<string, string> ReadFields(XElement paymentMean)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in paymentMean.Elements("PaymentMeanField"))
        {
            var key = field.Attribute("Key")?.Value;

            if (!string.IsNullOrWhiteSpace(key))
            {
                fields[key] = field.Value;
            }
        }

        return fields;
    }

    /// <summary>
    /// Reconoce la línea por el NOMBRE, no por el id. El id lo asigna HioPosCloud y cambia entre
    /// tiendas; el nombre lo pone este módulo en GET_CUSTOM_PARAMS y es estable.
    /// </summary>
    private static bool IsOgloba(Dictionary<string, string> fields)
    {
        foreach (var key in NameKeys)
        {
            if (fields.TryGetValue(key, out var name)
                && name.Contains(PaymentMeanName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
