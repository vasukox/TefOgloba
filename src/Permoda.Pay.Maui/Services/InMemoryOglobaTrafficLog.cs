using Permoda.Pay.Application.Abstractions.Logging;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Implementación en RAM del <see cref="IOglobaTrafficLog"/>. Mismo patrón de eviction
/// FIFO que <see cref="InMemoryTransactionAudit"/>: cap a N entradas para que la caja no
/// crezca sin límite durante un turno largo. Lo que se sale del cap se pierde — el log
/// completo de Ogloba queda en `logcat` bajo el tag `TefOgloba.Ogloba`, que es de donde se
/// capturan los cuerpos literales para la evidencia de certificación
/// (docs/PRUEBAS_Y_CERTIFICACION.md §5).
/// </summary>
public sealed class InMemoryOglobaTrafficLog : IOglobaTrafficLog
{
    /// <summary>
    /// Tope en SANDBOX. Ahí la bitácora es la evidencia de certificación y conviene que quepa
    /// una jornada larga de pruebas entera.
    /// </summary>
    public const int MaxRetainedEntries = 10_000;

    /// <summary>
    /// Tope en PRODUCCIÓN. Cada entrada guarda petición y respuesta completas: medidas, 2.569
    /// bytes de media, o sea <b>24,5 MB</b> al llegar a 10.000 — doce veces lo que decía la
    /// estimación que había acá. En una caja eso es memoria que le quitamos al POS a cambio de
    /// un historial que en producción nadie lee: lo que se rastrea sale del logcat y de la
    /// bitácora del cajero. Mil entradas (~2,5 MB) cubren de sobra un turno.
    /// </summary>
    public const int MaxRetainedEntriesInProduction = 1_000;

    private readonly Configuration.AppEnvironment _environment;
    private readonly LinkedList<OglobaTrafficEntry> _entries = new();
    private readonly object _lock = new();

    public InMemoryOglobaTrafficLog(Configuration.AppEnvironment environment) =>
        _environment = environment;

    private int Cap => _environment.IsProduction
        ? MaxRetainedEntriesInProduction
        : MaxRetainedEntries;

    public IReadOnlyCollection<OglobaTrafficEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public Task RecordAsync(OglobaTrafficEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // En producción se tapa ANTES de guardar, no al mostrar. El dato sensible no llega a
        // estar en memoria en claro, así que tampoco puede salir por el export a XLSX ni por un
        // volcado del proceso. En sandbox se guarda literal: es la evidencia de certificación.
        var stored = _environment.IsProduction
            ? entry with
            {
                RequestBody = TrafficBodyRedactor.Redact(entry.RequestBody),
                ResponseBody = TrafficBodyRedactor.Redact(entry.ResponseBody)
            }
            : entry;

        lock (_lock)
        {
            _entries.AddLast(stored);
            while (_entries.Count > Cap)
            {
                _entries.RemoveFirst();
            }
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}