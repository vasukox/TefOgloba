namespace Permoda.Pay.Maui.HiPos.Results;

public enum ReceiptLineType
{
    Text,
    QrCode,
    CutPaper
}

public sealed record ReceiptLine(string? Text, bool Bold = false, ReceiptLineType Type = ReceiptLineType.Text);

/// <summary>
/// Arma el XML &lt;Receipt&gt; que HiPOS traduce a comandos ESC/POS de la impresora térmica
/// (docs/INTEGRACION_HIPOS.md §8, "Comprobantes" — el lenguaje de impresión
/// de HiPOS"). El esquema real es &lt;Receipt numCols="42"&gt;&lt;ReceiptLine type="TEXT"&gt;...
/// &lt;/ReceiptLine&gt;...&lt;ReceiptLine type="CUT_PAPER"/&gt;&lt;/Receipt&gt; — sin envoltorio
/// adicional de MerchantReceipt/CustomerReceipt dentro del XML (ese nombre solo es el de la
/// extra del Intent que lo contiene, ver HiPosResponseComposer).
/// </summary>
public static class ReceiptBuilder
{
    private const int PrinterColumns = 42;

    public static string BuildMerchantReceipt(
        string storeId,
        string terminalId,
        string referenceNumber,
        string maskedCardNumber,
        long amountMinorUnits,
        string currency,
        long? remainingBalanceMinorUnits) =>
        FormatXml(
            BuildCommonLines(
                "COPIA COMERCIO",
                storeId,
                terminalId,
                referenceNumber,
                maskedCardNumber,
                amountMinorUnits,
                currency,
                remainingBalanceMinorUnits,
                // El QR del bono es para que el cliente lo escanee: no va en la copia comercio.
                eGiftCardUrl: null));

    /// <param name="cardDisplayValue">
    /// El serial que ve el CLIENTE. Para bonos activados (Ogloba devuelve un cardNumber real,
    /// ver ProcessSaleResult.CardNumber) debe ir COMPLETO, sin enmascarar: en un bono virtual esa
    /// es la única prueba física que se lleva el cliente para poder redimirlo después — no hay
    /// tarjeta plástica de respaldo como en el caso físico. Enmascararlo aquí lo dejaría inservible.
    /// </param>
    /// <param name="eGiftCardUrl">Si Ogloba lo devuelve, se imprime como QR además del texto.</param>
    public static string BuildCustomerReceipt(
        string storeId,
        string terminalId,
        string referenceNumber,
        string cardDisplayValue,
        long amountMinorUnits,
        string currency,
        long? remainingBalanceMinorUnits,
        string? eGiftCardUrl) =>
        FormatXml(
            BuildCommonLines(
                "COPIA CLIENTE",
                storeId,
                terminalId,
                referenceNumber,
                cardDisplayValue,
                amountMinorUnits,
                currency,
                remainingBalanceMinorUnits,
                eGiftCardUrl));

    private static List<ReceiptLine> BuildCommonLines(
        string title,
        string storeId,
        string terminalId,
        string referenceNumber,
        string cardDisplayValue,
        long amountMinorUnits,
        string currency,
        long? remainingBalanceMinorUnits,
        string? eGiftCardUrl)
    {
        var lines = new List<ReceiptLine>
        {
            new(title, Bold: true),
            new(""),
            new($"Tienda: {storeId}"),
            new($"Terminal: {terminalId}"),
            new($"Tarjeta: {cardDisplayValue}"),
            new(""),
            new($"Monto: {FormatAmount(amountMinorUnits, currency)}", Bold: true),
            new($"Autorización: {referenceNumber}"),
            new($"Saldo restante: {FormatAmount(remainingBalanceMinorUnits ?? 0, currency)}"),
            new(""),
            new("Operación procesada por KOAJ · Permoda")
        };

        if (!string.IsNullOrWhiteSpace(eGiftCardUrl))
        {
            lines.Add(new(""));
            lines.Add(new("Escanea para ver tu bono:"));
            lines.Add(new(eGiftCardUrl, Type: ReceiptLineType.QrCode));
        }

        lines.Add(new(null, Type: ReceiptLineType.CutPaper));

        return lines;
    }

    private static string FormatAmount(long minorUnits, string currency) =>
        $"{currency} {minorUnits:N0}";

    private static string FormatXml(IReadOnlyList<ReceiptLine> lines)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<Receipt numCols=\"").Append(PrinterColumns).AppendLine("\">");

        foreach (var line in lines)
        {
            var typeAttribute = line.Type switch
            {
                ReceiptLineType.QrCode => "QR_CODE",
                ReceiptLineType.CutPaper => "CUT_PAPER",
                _ => "TEXT"
            };

            if (line.Type == ReceiptLineType.CutPaper)
            {
                builder.Append("  <ReceiptLine type=\"").Append(typeAttribute).AppendLine("\"/>");
                continue;
            }

            builder.Append("  <ReceiptLine type=\"").Append(typeAttribute).AppendLine("\">");

            if (line.Bold)
            {
                builder.Append("    <Formats><Format from=\"0\" to=\"")
                    .Append(PrinterColumns)
                    .AppendLine("\">BOLD</Format></Formats>");
            }

            builder.Append("    <Text>")
                .Append(System.Security.SecurityElement.Escape(line.Text ?? string.Empty))
                .AppendLine("</Text>");

            builder.AppendLine("  </ReceiptLine>");
        }

        builder.Append("</Receipt>");

        return builder.ToString();
    }
}
