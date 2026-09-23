using System.Text;
using Permoda.Pay.Application.Abstractions.Logging;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Deja en disco cada fallo no controlado, para que exista después de que el proceso muera.
/// <para>
/// <b>El problema que resuelve.</b> Hasta ahora, un fallo se escribía a <c>logcat</c> y ahí
/// terminaba. <c>logcat</c> es un buffer circular: se pierde al reiniciar la terminal y lo
/// sobreescribe cualquier app ruidosa. Con 512 tiendas, eso significaba que si el módulo se caía
/// un martes en la 214, <b>nadie se enteraba nunca</b>. El cajero perdía la venta y, con suerte,
/// llamaba a decir «la app no sirve» — que no es un reporte, es un síntoma.
/// </para>
/// <para>
/// <b>Por qué a disco y no a un servicio.</b> Un servicio de telemetría exige conectividad que
/// estas cajas no siempre tienen, y manda datos de venta fuera de la red de Permoda. El archivo
/// local no depende de nada, sobrevive al reinicio, y sale por el exportador que ya existe: el
/// soporte pide el archivo y ve exactamente qué pasó. Si algún día se quiere telemetría remota,
/// este archivo es lo que se le da de comer.
/// </para>
/// <para>
/// <b>Nada de lo que escribe puede fallar ruidosamente.</b> Esto corre desde el manejador de
/// excepciones no controladas: si lanzara, convertiría un fallo en dos y se llevaría el rastro del
/// primero.
/// </para>
/// </summary>
public sealed class CrashJournal
{
    /// <summary>
    /// Cuántos fallos se conservan. Los viejos se van; lo que sirve para diagnosticar es lo
    /// reciente, y un archivo que crece sin tope termina llenando una terminal de tienda.
    /// </summary>
    public const int MaxRetainedEntries = 200;

    private const string FileName = "fallos.log";
    private const string Separator = "────────────────────────────────────────";

    private readonly string _filePath;
    private readonly Lock _gate = new();

    public CrashJournal()
    {
        var directory = FileSystem.AppDataDirectory ?? Path.GetTempPath();
        _filePath = Path.Combine(directory, FileName);
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Anota un fallo. Síncrono y bajo candado a propósito: cuando se llama desde
    /// <c>AppDomain.UnhandledException</c> el proceso se está muriendo, y una escritura asíncrona
    /// no alcanza a terminar — que es exactamente el caso que más importa registrar.
    /// </summary>
    public void Record(string origin, Exception exception, bool isTerminating = false)
    {
        try
        {
            var entry = new StringBuilder()
                .AppendLine(Separator)
                .Append("FECHA    : ").AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append("ORIGEN   : ").AppendLine(origin)
                .Append("MORTAL   : ").AppendLine(isTerminating ? "sí — el proceso se cerró" : "no")
                .Append("VERSIÓN  : ").AppendLine(SafeVersion())
                .Append("TIPO     : ").AppendLine(exception.GetType().FullName)
                .Append("MENSAJE  : ").AppendLine(Redact(exception.Message))
                .AppendLine("DETALLE  :")
                .AppendLine(Redact(exception.ToString()))
                .ToString();

            lock (_gate)
            {
                File.AppendAllText(_filePath, entry, Encoding.UTF8);
                TrimLocked();
            }
        }
        catch
        {
            // Registrar un fallo NUNCA puede provocar otro. Si no se puede escribir —disco lleno,
            // permisos, el proceso a medio morir— se pierde esta anotación y ya. Lo único peor que
            // un fallo sin rastro es un fallo que además tumba lo que quedaba en pie.
        }
    }

    /// <summary>Lo anotado, para la pantalla de diagnóstico y para el exportador.</summary>
    public string Read()
    {
        try
        {
            lock (_gate)
            {
                return File.Exists(_filePath) ? File.ReadAllText(_filePath) : string.Empty;
            }
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Cuántos fallos hay anotados. Es lo que se muestra en Configuración: un número distinto de
    /// cero es la señal de que algo pasó en esta caja aunque el cajero no lo haya reportado.
    /// </summary>
    public int Count()
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_filePath))
                {
                    return 0;
                }

                return File.ReadLines(_filePath)
                    .Count(line => line.StartsWith(Separator, StringComparison.Ordinal));
            }
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
        }
        catch (Exception)
        {
            // Si no se puede borrar, el tope de entradas se encarga igual.
        }
    }

    /// <summary>
    /// Recorta a las últimas <see cref="MaxRetainedEntries"/> anotaciones.
    /// <para>
    /// Escribe a un temporal y renombra. <c>File.WriteAllText</c> directo trunca primero y
    /// rellena después: un corte de energía en ese instante deja el archivo vacío, y se perdería
    /// justo el historial de una caja que está fallando — que es cuando más se necesita. Es el
    /// mismo problema que ya se corrigió en la bitácora de cajeros.
    /// </para>
    /// </summary>
    private void TrimLocked()
    {
        var text = File.ReadAllText(_filePath);
        var blocks = text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);

        if (blocks.Length <= MaxRetainedEntries)
        {
            return;
        }

        var kept = string.Concat(
            blocks[^MaxRetainedEntries..].Select(block => Separator + block));

        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, kept, Encoding.UTF8);
        File.Move(temporary, _filePath, overwrite: true);
    }

    /// <summary>
    /// Un mensaje de excepción puede arrastrar el cuerpo de una petición, y ahí van seriales y la
    /// llave de la tienda. Se tapa con el MISMO redactor de la bitácora de tráfico, para que un
    /// serial se vea igual en todos lados.
    /// </summary>
    private static string Redact(string? text) => TrafficBodyRedactor.Redact(text);

    private static string SafeVersion()
    {
        try
        {
            return $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
        }
        catch (Exception)
        {
            return "desconocida";
        }
    }
}
