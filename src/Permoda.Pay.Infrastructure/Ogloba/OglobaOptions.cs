namespace Permoda.Pay.Infrastructure.Ogloba;

/// <summary>
/// A dónde y cómo se habla con Ogloba, según el ambiente.
/// <para>
/// <b>Producción NO va directo a Ogloba.</b> Va por el API Management de Permoda. El host que la
/// configuración daba por oficial, <c>co-prod.ogloba.com</c>, <b>no resuelve en DNS</b>
/// —comprobado el 2026-09-08 desde una red donde <c>co-ts.ogloba.com</c> sí resuelve—, así que
/// nunca habría funcionado en una tienda. Las rutas reales salen de la hoja PARAMETROS-OGLOBA del
/// documento de credenciales, y la conexión quedó certificada el 2026-09-09 contra la tienda 037.
/// </para>
/// <para>
/// Lo que cambia por el APIM es el CAMINO y la AUTENTICACIÓN —recursos <c>*AM</c> y cabecera
/// <c>Ocp-Apim-Subscription-Key</c> en vez de Basic Auth—. El cuerpo de las peticiones es idéntico
/// al de sandbox, y por eso el resto del proveedor no se entera de en qué ambiente está.
/// </para>
/// <para>
/// <b>Nota de reconstrucción (2026-09-23).</b> Este archivo se perdió el 2026-09-22 por un revert
/// del árbol de trabajo sobre código que nunca se había commiteado. Se reconstruyó a partir de sus
/// pruebas —que sí sobrevivieron— y de la superficie que exige <c>OglobaGiftCardProvider</c>. Los
/// valores de negocio (las once rutas, los topes de tiempo) vienen de la hoja oficial y están
/// fijados por esas pruebas, no por memoria.
/// </para>
/// </summary>
public sealed class OglobaOptions
{
    // Locked en `2.18` por correo de Gilberto Fernández (gif@ogloba.com, 11-abr-2026):
    // la API que KOAJ debe consumir en sandbox `co-ts.ogloba.com`. NO bumpear a `2.20`
    // (la del Postman Collection público es de otro tenant demo, NO de KOAJ) sin validar
    // K00036 con /activation, /redemption, /balance y /getProducts.
    public const string DefaultApiVersion = "2.18";

    // Manual Tef Ogloba v4: la redención dispone de ~90 segundos. Damos ese margen.
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Cuánto se espera por el API Management, y por qué es MENOS que contra Ogloba directo.
    /// <para>
    /// La hoja de parámetros declara <c>Og_WsTimeOut = 50.000 ms</c>. Si la pasarela corta a los
    /// 50 segundos y nosotros seguimos esperando hasta los 90, lo que vuelve es el error del
    /// gateway — que el módulo lee como rechazo. Pero la redención pudo haberse aplicado en Ogloba
    /// del otro lado, y habríamos dado por fallido un cobro que sí ocurrió: al cliente se le
    /// descontó el saldo y la venta dice que no se cobró.
    /// </para>
    /// <para>
    /// Cortando nosotros primero, el corte entra por el camino de <i>resultado incierto</i>, que es
    /// el que dispara la recuperación al arrancar. «No sé qué pasó, lo averiguo» es recuperable;
    /// «falló» cuando en realidad se aplicó, no lo es.
    /// </para>
    /// </summary>
    public static readonly TimeSpan ApiManagementRequestTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Tope de las operaciones de SOLO LECTURA (conectividad, catálogo, saldo del comercio).
    /// Ninguna mueve plata, así que no merecen la espera larga de un cobro: un catálogo que no
    /// responde en 20 segundos no va a responder, y mientras tanto el cajero mira una pantalla
    /// quieta.
    /// </summary>
    public static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Las once operaciones que el APIM publica, con el nombre EXACTO de la hoja oficial.
    /// <para>
    /// Es un diccionario cerrado a propósito, y no una regla del tipo «agregar AM al final».
    /// Una regla inventaría rutas para operaciones que la pasarela no publica, y el módulo saldría
    /// a la red para volver con un 404 que se lee como «respuesta malformada» — un error que no le
    /// dice nada a nadie. Con la lista explícita, lo que no está falla ACÁ y con su nombre.
    /// </para>
    /// <para>
    /// Los cinco <c>order*</c> NO están: son la activación de bonos virtuales por lotes y el APIM
    /// no los publica (comprobado el 2026-09-09 con la llave válida de la 037). La activación
    /// digital en tienda no los necesita — va por <c>activation</c> con <c>gencode</c> y
    /// <c>email</c>, confirmado por Ogloba.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> ApiManagementResources =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["activation"] = "ogloba/activationAM",
            ["redemption"] = "ogloba/redemptionAM",
            ["confirmTransaction"] = "ogloba/confirmTransactionAM",
            ["balance"] = "ogloba/balanceAM",
            ["voidTransaction"] = "ogloba/voidTransactionAM",
            ["cancelTransaction"] = "ogloba/cancelTransactionAM",
            ["reload"] = "ogloba/reloadAM",
            ["reversal"] = "ogloba/reversalAM",
            ["verify"] = "ogloba/verifyAM",
            ["reconciliation"] = "ogloba/reconciliationAM",
            ["queryTransactionsHistory"] = "ogloba/queryTransactionsHistoryAM"
        };

    public OglobaOptions(
        Uri baseAddress,
        string apiVersion = DefaultApiVersion,
        TimeSpan? requestTimeout = null,
        bool usesApiManagement = false)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        // El APIM tampoco admite tráfico en claro. La validación no se relajó al agregar el modo
        // nuevo: una URL sin TLS sigue siendo un error de programación, no una opción.
        if (!baseAddress.IsAbsoluteUri || baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Ogloba requires an absolute HTTPS base address.", nameof(baseAddress));
        }

        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            throw new ArgumentException("The Ogloba API version is required.", nameof(apiVersion));
        }

        UsesApiManagement = usesApiManagement;

        // Un tope explícito manda sobre el del ambiente: es lo que usan las pruebas. Sin él, el
        // ambiente decide — 45 s por la pasarela, 90 s contra Ogloba directo.
        var effectiveTimeout = requestTimeout
            ?? (usesApiManagement ? ApiManagementRequestTimeout : DefaultRequestTimeout);

        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        var normalizedAddress = baseAddress.AbsoluteUri.EndsWith('/')
            ? baseAddress
            : new Uri($"{baseAddress.AbsoluteUri}/", UriKind.Absolute);

        BaseAddress = normalizedAddress;
        ApiVersion = apiVersion.Trim();
        RequestTimeout = effectiveTimeout;

        // Una lectura NUNCA espera más que un cobro. Se toma el menor de los dos porque con el
        // APIM el tope general baja a 45 s, y con un tope explícito corto (las pruebas usan 5 s)
        // un valor fijo de 20 s rompería la invariante sin que nadie lo notara.
        QueryTimeout = effectiveTimeout < DefaultQueryTimeout ? effectiveTimeout : DefaultQueryTimeout;
    }

    public Uri BaseAddress { get; }

    public string ApiVersion { get; }

    public TimeSpan RequestTimeout { get; }

    /// <summary>Tope de las operaciones de solo lectura. Nunca mayor que <see cref="RequestTimeout"/>.</summary>
    public TimeSpan QueryTimeout { get; }

    /// <summary>
    /// El tráfico va por el API Management de Permoda. Cambia las rutas y la autenticación
    /// (<c>Ocp-Apim-Subscription-Key</c> en vez de Basic Auth); el cuerpo no cambia.
    /// <para>
    /// Por omisión es <c>false</c>: nadie usa el APIM salvo el ambiente que lo pide
    /// explícitamente. Un valor por defecto al revés haría que una prueba mal escrita saliera
    /// contra producción.
    /// </para>
    /// </summary>
    public bool UsesApiManagement { get; }

    /// <summary>
    /// La ruta de una operación. En sandbox la operación ES la ruta; por el APIM se traduce a su
    /// recurso <c>*AM</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// La operación no está publicada en el APIM. Prefiere <see cref="TryResolvePath"/> en
    /// cualquier camino que corra durante un cobro: una excepción ahí tumba la app delante del
    /// cliente, y ya pasó una vez con el primer bono digital en producción.
    /// </exception>
    public string ResolvePath(string operation) =>
        TryResolvePath(operation, out var path)
            ? path!
            : throw new NotSupportedException(
                $"La operación '{operation}' no está publicada en el API Management.");

    /// <summary>
    /// Igual que <see cref="ResolvePath"/> pero sin lanzar: devuelve <c>false</c> cuando la
    /// operación no está publicada.
    /// <para>
    /// Existe porque el proveedor tiene que poder responder «esto todavía no está disponible» como
    /// un fallo normal —con mensaje para el cajero— en vez de morirse. Salir a la red y volver con
    /// un 404 sería peor: el módulo lo lee como respuesta malformada y el cajero ve un error que
    /// no dice nada.
    /// </para>
    /// </summary>
    public bool TryResolvePath(string operation, out string? path)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            path = null;
            return false;
        }

        var trimmed = operation.Trim();

        if (!UsesApiManagement)
        {
            path = trimmed;
            return true;
        }

        if (ApiManagementResources.TryGetValue(trimmed, out var resource))
        {
            path = resource;
            return true;
        }

        path = null;
        return false;
    }
}
