namespace Permoda.Pay.Maui.Configuration;

/// <summary>
/// Datos de autocompletado del entorno sandbox de Ogloba (co-ts). Son credenciales de
/// PRUEBA compartidas por Ogloba para desarrollo/UAT, nunca de producción. Se exponen solo
/// en builds no productivos (ver <see cref="AppEnvironment.Sandbox"/>) para que el cajero
/// pueda rellenar la configuración de un toque mientras validamos el flujo por ADB.
/// </summary>
public sealed record SandboxHint(string SampleStoreId, string Password);

public sealed class AppEnvironment
{
    public const string Development = "Development";
    public const string Uat = "UAT";
    public const string Production = "Production";

#if !ENVIRONMENT_PRODUCTION
    /// <summary>
    /// Passphrase del sandbox de Ogloba (co-ts), para autocompletar la configuración de una
    /// terminal de pruebas de un toque.
    /// <para>
    /// <b>No está en el código: la pone quien compila.</b> Este repositorio es público y ese valor
    /// es una credencial real, compartida por todas las tiendas KOAJ de prueba. Se inyecta al
    /// compilar desde la variable de entorno <c>PERMODA_SANDBOX_PASSWORD</c> y queda como metadato
    /// del ensamblado; el repositorio nunca la ve.
    /// </para>
    /// <para>
    /// Si la variable no está definida, esto devuelve vacío y el autocompletado simplemente no
    /// rellena ese campo — quien monta la terminal lo pega a mano. Nada se rompe: es una comodidad,
    /// no un requisito.
    /// </para>
    /// <para>
    /// Y no puede colarse en producción: el <c>#if</c> excluye este miembro, y el proyecto además
    /// solo emite el metadato fuera de la configuración Release. Antes esto era un <c>const</c>
    /// fuera del <c>#if</c>, y como los <c>const</c> se graban en los metadatos aunque su rama no
    /// se compile, la passphrase viajaba dentro del APK de PRODUCCIÓN — que CloudLicense distribuye
    /// y cualquiera puede descargar.
    /// </para>
    /// </summary>
    private static string SandboxPassword =>
        typeof(AppEnvironment).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, "PermodaSandboxPassword", StringComparison.Ordinal))
            ?.Value
        ?? string.Empty;
#endif

    public AppEnvironment(
        string name,
        string oglobaBaseUrl,
        string oglobaApiVersion,
        string digitalProductCode,
        SandboxHint? sandbox,
        bool usesApiManagement = false)
    {
        UsesApiManagement = usesApiManagement;

        if (string.IsNullOrWhiteSpace(digitalProductCode))
        {
            throw new ArgumentException("The digital product code is required.", nameof(digitalProductCode));
        }

        DigitalProductCode = digitalProductCode;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The environment name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(oglobaBaseUrl))
        {
            throw new ArgumentException("The Ogloba base URL is required.", nameof(oglobaBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(oglobaApiVersion))
        {
            throw new ArgumentException("The Ogloba API version is required.", nameof(oglobaApiVersion));
        }

        Name = name;
        OglobaBaseUrl = oglobaBaseUrl.TrimEnd('/');
        OglobaApiVersion = oglobaApiVersion;
        Sandbox = sandbox;
    }

    public string Name { get; }

    public string OglobaBaseUrl { get; }

    public string OglobaApiVersion { get; }

    /// <summary>Pista de autocompletado sandbox. <c>null</c> en producción.</summary>
    public SandboxHint? Sandbox { get; }

    /// <summary>
    /// El tráfico va por el API Management de Permoda. Cambia las rutas y la autenticación
    /// (<c>Ocp-Apim-Subscription-Key</c> en vez de Basic Auth); el cuerpo no cambia.
    /// </summary>
    public bool UsesApiManagement { get; }

    /// <summary>
    /// Cómo se llama, para el cajero, el secreto que se pide en Configuración. Con el APIM ya no
    /// es una contraseña de Ogloba sino la llave de la tienda, y llamarla "contraseña" haría que
    /// alguien intentara escribir la vieja.
    /// </summary>
    public string CredentialLabel =>
        UsesApiManagement ? "LLAVE DE LA TIENDA (SUBSCRIPTION KEY)" : "CONTRASEÑA OGLOBA";

    /// <summary>
    /// El proceso del bono digital es el MISMO en todos los ambientes, y la entrega al cliente la
    /// hace Ogloba. El módulo no entrega bonos en papel: emite y ahí termina su parte.
    /// <para>
    /// Queda un cabo medido y sin explicar: por el APIM, los recursos de Order management
    /// (<c>orderCreation</c>, <c>orderConfirm</c>, <c>orderStatus</c>, <c>orderCancel</c>,
    /// <c>orderReturn</c>) responden 404 — comprobado el 2026-09-09 con la llave válida de la
    /// 037. Si el flujo de producción es el mismo de sandbox, esos recursos tienen que existir:
    /// hay que confirmarlo con quien administra el APIM antes de emitir un bono digital en
    /// producción.
    /// </para>
    /// </summary>
    public bool UsesOrderManagementForDigitalCards => true;

    /// <summary>
    /// Código de producto (itemCode) para bonos VIRTUALES emitidos por /orderCreation.
    /// <para>
    /// Sandbox: <c>113816</c> (TARJETA OBSEQUIO B2C), verificado contra co-ts con la tienda
    /// K00037 el 2026-08-24. El anterior, <c>113815</c> (BONO REGALO B2B), **rechaza cualquier
    /// monto con errorCode 73** en Order management —incluido el mínimo exacto de su propio
    /// catálogo—, así que la activación virtual nunca funcionó con él. Rango real del producto
    /// nuevo: 30.000 a 500.000.
    /// </para>
    /// <para>
    /// Producción: <c>113811</c> (KOAJ Dotacion B2B). Funciona en sandbox, pero es un producto
    /// de dotación corporativa, no el bono de regalo B2C que se eligió para el cajero: hay que
    /// confirmar contra co-prod cuál es el itemCode equivalente a 113816 antes de salir a
    /// producción.
    /// </para>
    /// Las tarjetas físicas no lo usan: van con su propio PAN (producto 113817).
    /// </summary>
    public string DigitalProductCode { get; }

    public bool IsProduction => Name == Production;

    public bool IsSandbox => !IsProduction;

    /// <summary>Etiqueta legible del ambiente para la UI.</summary>
    public string DisplayName => IsProduction ? "Producción" : "Sandbox de pruebas";

    /// <summary>Host corto (co-ts / co-prod) para mostrar en el distintivo.</summary>
    public string Host
    {
        get
        {
            return Uri.TryCreate(OglobaBaseUrl, UriKind.Absolute, out var uri)
                ? uri.Host
                : OglobaBaseUrl;
        }
    }

    public static AppEnvironment FromBuildConfiguration(string? buildConfiguration)
    {
        // Sandbox CONFIRMADO POR GILBERTO (correo directo, no la colección Postman genérica
        // que apunta a srl-ts/2.20 — ese es el tenant demo público, no el de KOAJ).
        //
        // K00037 es la tienda de pruebas en uso. Verificado en vivo contra co-ts el 2026-08-24:
        // /test, /getProducts y /orderCreation + /orderCancel responden correctamente, o sea que
        // Order management SÍ está habilitado para esa tienda. Lo que fallaba era el producto
        // 113815, no la tienda (ver DigitalProductCode).
#if ENVIRONMENT_DEVELOPMENT
        return new AppEnvironment(
            Development,
            "https://co-ts.ogloba.com/gc-restful-gateway/giftCardService",
            "2.18",
            digitalProductCode: "113816",
            new SandboxHint("K00037", SandboxPassword));
#elif ENVIRONMENT_UAT
        return new AppEnvironment(
            Uat,
            "https://co-ts.ogloba.com/gc-restful-gateway/giftCardService",
            "2.18",
            digitalProductCode: "113816",
            new SandboxHint("K00037", SandboxPassword));
#else
        // PRODUCCIÓN NO VA DIRECTO A OGLOBA. Va por el API Management de Permoda.
        //
        // El host que estaba acá, `co-prod.ogloba.com`, NO RESUELVE EN DNS — comprobado el
        // 2026-09-08 desde una red donde `co-ts.ogloba.com` sí resuelve, así que no era la red.
        // Ese endpoint nunca habría funcionado en una tienda.
        //
        // La ruta real está en la hoja PARAMETROS-OGLOBA del documento de credenciales
        // (`Og_WsUrlAM`), y la conexión quedó certificada el 2026-09-09 contra la tienda 037:
        // POST /ogloba/balanceAM y /ogloba/redemptionAM devolvieron 200 con el código de negocio
        // de Ogloba en español. El cuerpo de las peticiones es idéntico al de sandbox.
        //
        // La autenticación cambia: Ocp-Apim-Subscription-Key por tienda en vez de Basic Auth.
        // Ver OglobaOptions.UsesApiManagement y OglobaGiftCardProvider.ApplyAuthentication.
        //
        // `113811` está CONFIRMADO como el GenCode digital oficial (`Og_WsGenCode` en esa misma
        // hoja). Ya no es el valor dudoso que decía la nota anterior.
        _ = buildConfiguration;
        return new AppEnvironment(
            Production,
            "https://apim-permoda-prod.azure-api.net",
            "2.18",
            digitalProductCode: "113811",
            sandbox: null,
            usesApiManagement: true);
#endif
    }
}
