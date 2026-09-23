using Android.Util;
using Permoda.Pay.Maui.HiPos.Results;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Un comprobante de activación ya emitido, guardado en la terminal.
/// </summary>
/// <param name="FileName">Nombre del archivo dentro de <c>comprobantes/</c>.</param>
/// <param name="ReceiptXml">
/// El XML <c>&lt;Receipt&gt;</c> tal como lo imprimiría HiPOS. Se guarda el formato del
/// contrato y no texto plano a propósito: el día que exista el camino a la impresora, lo que hay
/// en disco ya es lo que se manda, sin volver a armarlo ni arriesgar que salga distinto de lo
/// que el cajero vio.
/// </param>
public sealed record ActivationReceipt(
    string FileName,
    DateTimeOffset IssuedAtUtc,
    string Reference,
    string CardDisplayValue,
    long AmountPesos,
    string ReceiptXml);

/// <summary>
/// Guarda el comprobante de CADA activación en el almacenamiento privado de la app.
/// <para>
/// Existe porque la activación no pasa por HiPOS —es un flujo manual con su propia
/// autenticación de cajero— y HiPOS solo imprime como parte de una <c>TRANSACTION</c>. Sin este
/// almacén, el comprobante de una activación no existía en ninguna parte: ni en papel ni en
/// disco. Si el cliente volvía preguntando por su bono, no había nada que reimprimir ni que
/// mostrarle.
/// </para>
/// <para>
/// El contenido se arma con <see cref="ReceiptBuilder"/>, el mismo que ya usa el cobro por
/// HiPOS. Una sola forma de redactar un comprobante: dos serían dos formas de que salga
/// distinto.
/// </para>
/// </summary>
public sealed class ActivationReceiptStore
{
    private const string LogTag = "TefOgloba";
    private const string DirectoryName = "comprobantes";

    /// <summary>
    /// Cuántos se conservan. Un comprobante ronda el kilobyte, así que 2.000 son ~2 MB — meses
    /// de activaciones en una caja normal. El tope existe para que el almacenamiento no crezca
    /// sin techo durante la vida de la terminal, no porque estorben.
    /// </summary>
    private const int MaxRetained = 2_000;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private static string Directory =>
        Path.Combine(FileSystem.AppDataDirectory, DirectoryName);

    /// <summary>
    /// Arma y guarda el comprobante de una activación aprobada.
    /// <para>
    /// Devuelve el nombre del archivo, o <c>null</c> si no se pudo escribir. <b>Nunca lanza</b>:
    /// el bono ya se activó y el cliente ya pagó — que falle guardar el papel no puede tumbar
    /// una operación que en Ogloba ya ocurrió. El fallo queda en el log, que es donde alguien
    /// puede verlo sin que el cajero pierda la venta.
    /// </para>
    /// </summary>
    /// <param name="cardDisplayValue">
    /// El serial que ve el CLIENTE, <b>completo y sin enmascarar</b>. En un bono virtual es la
    /// única prueba que se lleva para poder redimirlo: no hay plástico de respaldo, y
    /// enmascararlo aquí dejaría el comprobante inservible.
    /// </param>
    public async Task<string?> SaveAsync(
        string storeId,
        string terminalId,
        string reference,
        string cardDisplayValue,
        long amountPesos,
        string currency,
        long? remainingBalancePesos,
        string? eGiftCardUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var xml = ReceiptBuilder.BuildCustomerReceipt(
                storeId,
                terminalId,
                reference,
                cardDisplayValue,
                amountPesos,
                currency,
                remainingBalancePesos,
                eGiftCardUrl);

            await _gate.WaitAsync(cancellationToken);

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                // El nombre lleva la fecha y la referencia: se ordena solo y se encuentra por el
                // dato que trae el cliente cuando reclama.
                var safeReference = Sanitize(reference);
                var name = $"{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-{safeReference}.xml";

                await File.WriteAllTextAsync(
                    Path.Combine(Directory, name), xml, cancellationToken);

                TrimOldest();

                Log.Info(LogTag, $"[ActivationReceipt] Comprobante guardado: {name}");
                return name;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[ActivationReceipt] No se pudo guardar el comprobante: {exception}");
            return null;
        }
    }

    /// <summary>Comprobantes guardados, del más reciente al más antiguo.</summary>
    public async Task<IReadOnlyList<ActivationReceipt>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return [];
            }

            var receipts = new List<ActivationReceipt>();

            foreach (var path in System.IO.Directory.GetFiles(Directory, "*.xml")
                         .OrderByDescending(path => path))
            {
                try
                {
                    var info = new FileInfo(path);
                    receipts.Add(new ActivationReceipt(
                        info.Name,
                        info.LastWriteTimeUtc,
                        ReferenceFromName(info.Name),
                        string.Empty,
                        0,
                        await File.ReadAllTextAsync(path, cancellationToken)));
                }
                catch (IOException)
                {
                    // Un archivo ilegible no puede ocultar los demás.
                }
            }

            return receipts;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>El XML de un comprobante concreto, para reimprimirlo. <c>null</c> si no está.</summary>
    public async Task<string?> ReadAsync(string fileName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var path = Path.Combine(Directory, Path.GetFileName(fileName));
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deja solo los <see cref="MaxRetained"/> más recientes. Se llama con el gate tomado.</summary>
    private static void TrimOldest()
    {
        try
        {
            var files = System.IO.Directory.GetFiles(Directory, "*.xml");

            if (files.Length <= MaxRetained)
            {
                return;
            }

            foreach (var path in files.OrderBy(path => path).Take(files.Length - MaxRetained))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            // Que no se pueda podar no puede impedir guardar el comprobante nuevo.
            Log.Warn(LogTag, $"[ActivationReceipt] No se pudieron podar los antiguos: {exception.Message}");
        }
    }

    private static string ReferenceFromName(string fileName)
    {
        // "20260908-084412123-00136544716V.xml" → "00136544716V"
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var lastDash = withoutExtension.LastIndexOf('-');
        return lastDash >= 0 && lastDash < withoutExtension.Length - 1
            ? withoutExtension[(lastDash + 1)..]
            : withoutExtension;
    }

    /// <summary>Deja solo lo que puede ir en un nombre de archivo, sin sorpresas de plataforma.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "sin-referencia";
        }

        var clean = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(clean) ? "sin-referencia" : clean;
    }
}
