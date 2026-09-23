using System.Text.Json;
using System.Text.Json.Serialization;

namespace Permoda.Pay.Application.Features.Provisioning;

/// <summary>
/// Un cajero tal como viaja entre cajas: su nombre y el HASH de su contraseña.
/// <para>
/// La contraseña en claro no existe ni siquiera en la caja emisora —se guarda hasheada desde que
/// se crea—, así que acá no hay nada que "proteger de más": se copia el mismo hash y el cajero
/// entra en la caja nueva con la contraseña de siempre.
/// </para>
/// </summary>
/// <param name="Id">Nombre de usuario del cajero, tal cual está dado de alta.</param>
/// <param name="PasswordHash">
/// Hash de su contraseña. Puede venir vacío: un cajero dado de alta sin contraseña todavía es un
/// estado válido, y la pantalla de ingreso le pide crearla la primera vez.
/// </param>
/// <param name="IsEnabled">
/// <c>false</c> si está desactivado. Se replica a propósito: si no, un cajero que el
/// administrador sacó de circulación volvería a entrar por la caja de al lado.
/// </param>
public sealed record ReplicatedCashier(
    string Id,
    string PasswordHash,
    bool IsEnabled);

/// <summary>
/// Todo lo que una caja le pasa a otra de la MISMA tienda.
/// <para>
/// La regla que decide qué entra acá: <b>si es de la tienda, se copia; si es de la caja, no</b>.
/// Por eso no está <c>TerminalId</c> — dos cajas con el mismo identificador firmarían igual sus
/// operaciones y la bitácora de Ogloba dejaría de distinguirlas, que es justo lo que se usa para
/// saber dónde ocurrió un cobro.
/// </para>
/// <para>
/// <c>AssignedTerminalIds</c> es la excepción aparente y no lo es: no se copia para adoptarlo,
/// se manda para que la caja receptora sepa cuáles están tomados y proponga el siguiente libre.
/// </para>
/// </summary>
public sealed record TerminalConfigurationEnvelope(
    string StoreId,
    string SubscriptionKey,
    string BaseUrl,
    string ApiVersion,
    string StoreName,
    string AdminPinHash,
    IReadOnlyList<ReplicatedCashier> Cashiers,
    IReadOnlyList<string> AssignedTerminalIds)
{
    /// <summary>
    /// Versión del formato. Existe porque las 512 tiendas no se actualizan el mismo día: una caja
    /// con el APK nuevo va a encontrarse cajas con el viejo, y al revés. Sin este número, esa
    /// diferencia se manifiesta como un JSON que deserializa a medias y una caja configurada con
    /// campos vacíos —que es peor que no configurarse, porque parece que funcionó.
    /// </summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Nombres de caja ya tomados, en minúsculas, para comparar sin sorpresas de mayúsculas.
    /// </summary>
    private IEnumerable<string> NormalizedAssignedIds =>
        AssignedTerminalIds.Select(id => id.Trim().ToLowerInvariant());

    /// <summary>
    /// El siguiente <c>CAJA-NN</c> libre, para proponérselo al operador.
    /// <para>
    /// Se PROPONE, no se impone: el criterio de numeración es de la tienda y puede no seguir esta
    /// forma. Si el operador escribe otra cosa, manda él. Lo único que esto evita es el error
    /// silencioso de dejar dos cajas con el mismo nombre.
    /// </para>
    /// </summary>
    public string SuggestNextTerminalId()
    {
        var taken = NormalizedAssignedIds.ToHashSet();

        // Arranca en 2: la caja 1 es la que está repartiendo, así que su número ya está tomado
        // por definición. El tope de 99 no es una restricción real —ninguna tienda KOAJ tiene
        // tantas cajas—, es un freno para que un dato corrupto no convierta esto en un bucle.
        for (var number = 2; number <= 99; number++)
        {
            var candidate = $"CAJA-{number:D2}";

            if (!taken.Contains(candidate.ToLowerInvariant()))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// <c>true</c> si ese nombre de caja ya lo usa otra. La caja receptora tiene que poder
    /// avisarlo ANTES de guardar: descubrirlo después son dos cajas firmando igual y nadie
    /// mirando.
    /// </summary>
    public bool IsTerminalIdTaken(string? terminalId) =>
        !string.IsNullOrWhiteSpace(terminalId)
        && NormalizedAssignedIds.Contains(terminalId.Trim().ToLowerInvariant());

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reconstruye el sobre. Devuelve <c>null</c> ante cualquier problema en vez de lanzar: lo que
    /// llega acá viene de la red, y un error de formato tiene que terminar en "no se pudo copiar,
    /// configura a mano" y no en un crash a mitad de la instalación de una tienda.
    /// </summary>
    public static TerminalConfigurationEnvelope? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<TerminalConfigurationEnvelope>(
                json, SerializerOptions);

            // Un sobre de una versión que no conocemos se RECHAZA, no se interpreta a medias.
            if (envelope is null || envelope.Version != CurrentVersion)
            {
                return null;
            }

            // Sin estos cuatro la caja no puede transaccionar, y una configuración incompleta que
            // se guarda "a ver si sirve" falla después, lejos, y con un mensaje de Ogloba que no
            // apunta acá.
            return string.IsNullOrWhiteSpace(envelope.StoreId)
                   || string.IsNullOrWhiteSpace(envelope.SubscriptionKey)
                   || string.IsNullOrWhiteSpace(envelope.BaseUrl)
                   || string.IsNullOrWhiteSpace(envelope.ApiVersion)
                ? null
                : envelope;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
