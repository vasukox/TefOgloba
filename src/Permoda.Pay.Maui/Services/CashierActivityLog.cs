using System.Text.Json;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Una entrada de actividad del cajero. No contiene PAN: solo la tarjeta enmascarada, la
/// referencia, el monto y el resultado, atribuidos al cajero y a la caja.
/// </summary>
/// <param name="SignatureFile">
/// Nombre del PNG con la firma del cliente, cuando la operación la exigió (activación de
/// bonos). Se guarda la RUTA y no la imagen embebida: el JSON de la bitácora se lee entero en
/// memoria en cada escritura, y meterle imágenes en base64 lo haría crecer sin techo.
/// </param>
public sealed record CashierActivityEntry(
    DateTimeOffset TimestampUtc,
    string CashierId,
    string TerminalId,
    string Action,
    string Detail,
    bool Success,
    string? SignatureFile = null);

/// <summary>
/// Registro persistido de la actividad de los cajeros. Se guarda en el almacenamiento privado
/// de la app (no respaldado, ver AndroidManifest) como JSON, acotado a las últimas entradas.
/// </summary>
public sealed class CashierActivityLog
{
    private const int MaxEntries = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public CashierActivityLog()
    {
        var directory = FileSystem.AppDataDirectory;
        _filePath = Path.Combine(directory, "cashier-activity.json");
    }

    /// <summary>
    /// Guarda la firma del cliente en el almacenamiento privado de la app y devuelve el nombre
    /// del archivo, para dejarlo en la entrada de la bitácora. Devuelve <c>null</c> si no hay
    /// firma o si no se pudo escribir: una firma que no se guarda no puede tumbar la activación
    /// —el bono ya se cobró—, pero sí tiene que quedar constancia de que faltó.
    /// </summary>
    public async Task<string?> SaveSignatureAsync(byte[]? png, CancellationToken cancellationToken)
    {
        if (png is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var directory = Path.Combine(FileSystem.AppDataDirectory, "firmas");
            Directory.CreateDirectory(directory);

            var name = $"firma-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png";
            await File.WriteAllBytesAsync(Path.Combine(directory, name), png, cancellationToken);
            return name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task RecordAsync(CashierActivityEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var entries = await ReadAllAsync();
            entries.Add(entry);

            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }

            await WriteAllAsync(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Devuelve las entradas de más reciente a más antigua.</summary>
    public async Task<IReadOnlyList<CashierActivityEntry>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var entries = await ReadAllAsync();
            entries.Reverse();
            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<CashierActivityEntry>> ReadAllAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new List<CashierActivityEntry>();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var entries = await JsonSerializer.DeserializeAsync<List<CashierActivityEntry>>(stream);
            return entries ?? new List<CashierActivityEntry>();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new List<CashierActivityEntry>();
        }
    }

    /// <summary>
    /// Escribe a un temporal y lo renombra encima. El renombrado es atómico: o queda el archivo
    /// viejo entero o el nuevo entero, nunca uno a medias.
    /// <para>
    /// Antes se escribía con <c>File.Create</c>, que TRUNCA antes de escribir. Un corte de luz en
    /// esa ventana —una caja de tienda, con su tomacorriente compartido— dejaba el archivo vacío
    /// y se perdían las 500 entradas anteriores. Justo la trazabilidad de quién hizo qué, que es
    /// para lo único que existe este archivo.
    /// </para>
    /// </summary>
    private async Task WriteAllAsync(List<CashierActivityEntry> entries)
    {
        var temporaryPath = _filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries);
            await stream.FlushAsync();
        }

        // Move con overwrite es el rename del sistema de archivos: instantáneo e indivisible.
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
