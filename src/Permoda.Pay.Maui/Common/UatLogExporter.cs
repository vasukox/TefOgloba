using System.Text.Json;
using System.Text.Json.Serialization;
using Permoda.Pay.Application.Abstractions.Logging;

namespace Permoda.Pay.Maui.Common;

public sealed class UatLogExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Export(
        IEnumerable<TransactionAuditEntry> auditEntries,
        IEnumerable<OglobaTrafficEntry> trafficEntries)
    {
        ArgumentNullException.ThrowIfNull(auditEntries);
        ArgumentNullException.ThrowIfNull(trafficEntries);

        var audit = auditEntries
            .Select(entry => new UatAuditEntry(
                entry.TimestampUtc.ToString("O"),
                entry.TransactionNumber,
                entry.ReferenceNumber,
                entry.Status.ToString(),
                entry.Event.ToString(),
                entry.ErrorCode))
            .ToArray();

        var traffic = trafficEntries
            .Select(entry => new UatAuditTrafficEntry(
                entry.TimestampUtc.ToString("O"),
                entry.TransactionNumber,
                entry.ReferenceNumber,
                entry.Path,
                entry.StoreId,
                entry.RequestBody,
                entry.ResponseBody,
                entry.IsSuccessful,
                entry.ErrorCode))
            .ToArray();

        return JsonSerializer.Serialize(
            new UatExportPayload(
                audit.Length,
                traffic.Length,
                DateTimeOffset.UtcNow.ToString("O"),
                audit,
                traffic),
            SerializerOptions);
    }
}

public sealed record UatExportPayload(
    int AuditCount,
    int TrafficCount,
    string GeneratedAtUtc,
    UatAuditEntry[] AuditEntries,
    UatAuditTrafficEntry[] TrafficEntries);

public sealed record UatAuditEntry(
    string TimestampUtc,
    string TransactionNumber,
    string? ReferenceNumber,
    string Status,
    string Event,
    string? ErrorCode);

public sealed record UatAuditTrafficEntry(
    string TimestampUtc,
    string TransactionNumber,
    string? ReferenceNumber,
    string Path,
    string StoreId,
    string Request,
    string Response,
    bool IsSuccessful,
    string? ErrorCode);
