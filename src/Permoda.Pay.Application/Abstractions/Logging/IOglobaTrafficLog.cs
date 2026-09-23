using System.Text.Json.Serialization;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Logging;

/// <summary>
/// Una llamada individual a Ogloba capturada con sus bodies request/response.
/// El audit de transacciones (<see cref="ITransactionAudit"/>) sigue siendo la fuente
/// de verdad para "qué pasó en la transacción"; este log es la fuente para "qué se
/// mandó y qué respondió Ogloba exactamente" — lo que el XLSX UAT interno exige
/// (pegar el JSON literal en las columnas REQUEST / RESPONSE).
/// </summary>
/// <param name="TransactionNumber">transactionNumber propio cuando aplique (Step 1, Step 2,
/// Step 3, /balance, /reversal). Vacío para endpoints sin transactionNumber (catalog).</param>
/// <param name="ReferenceNumber">referenceNumber Ogloba una vez conocido (Step 1 OK, Step 2, etc.).</param>
/// <param name="Path">Endpoint Ogloba (e.g. `redemption`, `confirmTransaction`, `balance`).</param>
/// <param name="StoreId">Tienda contra la que se autenticó.</param>
/// <param name="RequestBody">JSON del request enviado (sin password Basic Auth ni headers).</param>
/// <param name="ResponseBody">JSON crudo del response recibido. Vacío si la llamada falló
/// antes de recibir respuesta (timeout, red).</param>
/// <param name="IsSuccessful">true si el response fue 2xx e isSuccessful=true. false en cualquier
/// otro caso (error HTTP, isSuccessful=false, o timeout).</param>
public sealed record OglobaTrafficEntry(
    DateTimeOffset TimestampUtc,
    string TransactionNumber,
    string? ReferenceNumber,
    string Path,
    string StoreId,
    string RequestBody,
    string ResponseBody,
    bool IsSuccessful,
    string? ErrorCode);

public sealed record OglobaTrafficEntryExport(
    [property: JsonPropertyName("timestampUtc")] string TimestampUtc,
    [property: JsonPropertyName("transactionNumber")] string TransactionNumber,
    [property: JsonPropertyName("referenceNumber")] string? ReferenceNumber,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("storeId")] string StoreId,
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("isSuccessful")] bool IsSuccessful,
    [property: JsonPropertyName("errorCode")] string? ErrorCode);

/// <summary>
/// Puerto para registrar cada request/response a Ogloba. La implementación en MAUI
/// retiene las últimas N entradas para que AdminLogExportPage las incluya en el JSON UAT.
/// </summary>
public interface IOglobaTrafficLog
{
    Task RecordAsync(OglobaTrafficEntry entry, CancellationToken cancellationToken);

    IReadOnlyCollection<OglobaTrafficEntry> Entries { get; }

    void Clear();
}