using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Features.Provisioning;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Maui.Configuration;
using DomainStoreId = Permoda.Pay.Domain.Payments.StoreId;
using DomainTerminalId = Permoda.Pay.Domain.Payments.TerminalId;
using DomainCashierId = Permoda.Pay.Domain.Payments.CashierId;
using DomainTransactionNumber = Permoda.Pay.Domain.Payments.TransactionNumber;

namespace Permoda.Pay.Maui.Services;

/// <summary>
/// Estado de la terminal para toda la app: configuración de tienda (admin), PIN de
/// administrador, registro de cajeros y sesión de cajero activa (login por turno). Es la
/// única fuente de verdad que consumen las pantallas.
/// </summary>
public sealed class PosSession
{
    public const string SetupCompletedPreferenceKey = "tefogloba.setup.completed";
    public const string AdminPinSetPreferenceKey = "tefogloba.admin.pin.set";
    public const string ActiveCashierPreferenceKey = "tefogloba.cashier.active";

    /// <summary>
    /// Último cajero que abrió turno. Se guarda aparte del turno activo porque el turno se cierra
    /// (al terminar una activación, al volver al selector) y los cobros de HiPOS —que no piden
    /// cajero— necesitan igual un identificador para firmar la operación contra Ogloba.
    /// </summary>
    public const string LastCashierPreferenceKey = "tefogloba.cashier.last";

    private const string AdminPinStorageKey = "tefogloba.admin.pin.hash";
    private const string CashiersStoragePrefix = "tefogloba.cashiers.";

    private readonly SecureStorageStoreConfigurationProvider _configProvider;
    private readonly SecureStorageOglobaCredentialProvider _credentials;
    private readonly IGiftCardProvider _provider;
    private readonly ITransactionNumberGenerator _transactionNumbers;
    private bool _loaded;

    public PosSession(
        SecureStorageStoreConfigurationProvider configProvider,
        SecureStorageOglobaCredentialProvider credentials,
        IGiftCardProvider provider,
        ITransactionNumberGenerator transactionNumbers,
        AppEnvironment environment)
    {
        _configProvider = configProvider;
        _credentials = credentials;
        _provider = provider;
        _transactionNumbers = transactionNumbers;
        Environment = environment;
    }

    public AppEnvironment Environment { get; }

    public StoreConfiguration? Configuration { get; private set; }

    public bool IsConfigured => Configuration is { IsComplete: true };

    public bool IsAdminPinSet => Preferences.Default.Get(AdminPinSetPreferenceKey, false);

    /// <summary>La terminal quedó lista para operar (tienda configurada + PIN admin creado).</summary>
    public bool IsAdminReady => IsConfigured && IsAdminPinSet;

    public static bool SetupCompleted => Preferences.Default.Get(SetupCompletedPreferenceKey, false);

    public string StoreName => Configuration?.StoreName ?? string.Empty;

    // ----- Cajero (sesión de turno) -----

    public string? ActiveCashierId
    {
        get
        {
            var value = Preferences.Default.Get(ActiveCashierPreferenceKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public bool HasActiveCashier => !string.IsNullOrWhiteSpace(ActiveCashierId);

    /// <summary>
    /// Último cajero que abrió turno en esta caja. Sobrevive al cierre de turno a propósito.
    /// </summary>
    public string? LastCashierId
    {
        get
        {
            var value = Preferences.Default.Get(LastCashierPreferenceKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Con qué cajero se firma la operación que se está haciendo.
    /// <para>
    /// Es el del turno abierto y, si no hay, el último que operó en esta caja. El respaldo existe
    /// por los cobros que ordena HiPOS: ahí el módulo NO pide cajero —el cajero ya se identificó
    /// en el POS para poder facturar— y el turno puede estar cerrado porque lo cerró una
    /// activación manual anterior. Sin respaldo, Ogloba rechazaba el cobro con
    /// <c>cashier_id.required</c> apenas se escaneaba el bono.
    /// </para>
    /// <para>
    /// Es el cajero de ESTA caja, no uno cualquiera: se registra al hacer login con contraseña.
    /// </para>
    /// </summary>
    public string? OperatingCashierId => ActiveCashierId ?? LastCashierId;

    public bool HasOperatingCashier => !string.IsNullOrWhiteSpace(OperatingCashierId);

    // ----- Carga -----

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        Configuration = await _configProvider.LoadAsync(cancellationToken);
        _loaded = true;
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        _loaded = false;
        return EnsureLoadedAsync(cancellationToken);
    }

    // ----- Configuración (admin) -----

    /// <summary>
    /// Persiste la configuración de la terminal y, opcionalmente, la contraseña Ogloba.
    /// Tras guardar intenta traer el nombre del comercio desde Ogloba (best-effort).
    /// </summary>
    public async Task<PortResult<StoreConfiguration>> SaveConfigurationAsync(
        StoreConfiguration configuration,
        string? oglobaPassword,
        CancellationToken cancellationToken)
    {
        var storeIdResult = DomainStoreId.Create(configuration.StoreId);

        if (storeIdResult.IsFailure)
        {
            return PortResult<StoreConfiguration>.Failed(new PortFailure(
                storeIdResult.Error.Code,
                storeIdResult.Error.Description,
                PortFailureType.Technical));
        }

        await _configProvider.PersistAsync(configuration, cancellationToken);

        if (!string.IsNullOrWhiteSpace(oglobaPassword))
        {
            var credentialResult = await _credentials.PersistAsync(
                storeIdResult.Value,
                oglobaPassword.Trim(),
                cancellationToken);

            if (credentialResult.IsFailure)
            {
                return PortResult<StoreConfiguration>.Failed(credentialResult.Failure);
            }
        }

        Configuration = configuration;
        _loaded = true;
        Preferences.Default.Set(SetupCompletedPreferenceKey, true);

        await RefreshBusinessInfoAsync(cancellationToken);

        return PortResult<StoreConfiguration>.Success(Configuration);
    }

    /// <summary>Consulta GET /getBuInfo y cachea el nombre del comercio.</summary>
    public async Task RefreshBusinessInfoAsync(CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return;
        }

        var storeIdResult = DomainStoreId.Create(Configuration.StoreId);

        if (storeIdResult.IsFailure)
        {
            return;
        }

        var infoResult = await _provider.GetBusinessUnitInfoAsync(storeIdResult.Value, cancellationToken);

        if (infoResult.IsSuccess && !string.IsNullOrWhiteSpace(infoResult.Value.Name))
        {
            var updated = Configuration with { StoreName = infoResult.Value.Name };
            await _configProvider.PersistAsync(updated, cancellationToken);
            Configuration = updated;
        }
    }

    /// <summary>Devuelve el saldo del comercio (en centavos, per docs �3.17).
    /// Failure silenciosa � el chip "Saldo comercio" en PosHeaderView simplemente no aparece.</summary>
    public async Task<long?> GetBusinessUnitBalanceAsync(CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return null;
        }

        var storeIdResult = DomainStoreId.Create(Configuration.StoreId);
        if (storeIdResult.IsFailure)
        {
            return null;
        }

        var result = await _provider.GetBusinessUnitInfoAsync(storeIdResult.Value, cancellationToken);
        return result.IsSuccess ? result.Value.BalanceMinorUnits : null;
    }


    /// <summary>Consulta el saldo/estado de una tarjeta (POST /balance).</summary>
    /// <param name="pinCode">PIN del bono (opcional, 3 intentos incorrectos pueden bloquear).</param>
    /// <remarks>
    /// Genera un <see cref="DomainTransactionNumber"/> propio en lugar de pedirlo al
    /// caller: el balance query no se persiste, solo sirve para idempotencia en Ogloba.
    /// </remarks>
    public async Task<PortResult<CardBalance>> GetBalanceAsync(
        CardIdentifier card,
        string? pinCode = null,
        CancellationToken cancellationToken = default)
    {
        if (Configuration is null)
        {
            return PortResult<CardBalance>.Failed(new PortFailure(
                "session.not_configured", "La terminal no está configurada.", PortFailureType.Technical));
        }

        var storeIdResult = DomainStoreId.Create(Configuration.StoreId);
        var terminalIdResult = DomainTerminalId.Create(Configuration.TerminalId);
        // OperatingCashierId y no ActiveCashierId: en un cobro de HiPOS no hay turno abierto —el
        // módulo no lo pide— y con el id vacío Ogloba respondía cashier_id.required al consultar
        // el saldo, antes de poder cobrar nada.
        var cashierIdResult = DomainCashierId.Create(OperatingCashierId ?? string.Empty);

        if (storeIdResult.IsFailure)
        {
            return PortResult<CardBalance>.Failed(new PortFailure(
                storeIdResult.Error.Code, storeIdResult.Error.Description, PortFailureType.Technical));
        }

        if (terminalIdResult.IsFailure)
        {
            return PortResult<CardBalance>.Failed(new PortFailure(
                terminalIdResult.Error.Code, terminalIdResult.Error.Description, PortFailureType.Technical));
        }

        if (cashierIdResult.IsFailure)
        {
            return PortResult<CardBalance>.Failed(new PortFailure(
                cashierIdResult.Error.Code, cashierIdResult.Error.Description, PortFailureType.Technical));
        }

        return await _provider.GetBalanceAsync(
            new BalanceQuery(
                storeIdResult.Value,
                terminalIdResult.Value,
                cashierIdResult.Value,
                _transactionNumbers.Generate(),
                card,
                pinCode),
            cancellationToken);
    }

    /// <summary>
    /// Catálogo de productos de la tienda (docs §3.16). Se cachea en memoria al llamar a
    /// <see cref="LoadProductsAsync"/>; la UI lo lee via <see cref="Products"/> para gatekear
    /// opciones (ej. "Activar Virtual" solo si hay un producto con allowedActivate=1).
    /// </summary>
    public IReadOnlyList<GiftCardProduct> Products { get; private set; } = [];

    public async Task<PortResult<IReadOnlyList<GiftCardProduct>>> LoadProductsAsync(
        CancellationToken cancellationToken = default)
    {
        if (Configuration is null)
        {
            return PortResult<IReadOnlyList<GiftCardProduct>>.Failed(new PortFailure(
                "session.not_configured", "La terminal no está configurada.", PortFailureType.Technical));
        }

        var storeIdResult = DomainStoreId.Create(Configuration.StoreId);
        if (storeIdResult.IsFailure)
        {
            return PortResult<IReadOnlyList<GiftCardProduct>>.Failed(new PortFailure(
                storeIdResult.Error.Code, storeIdResult.Error.Description, PortFailureType.Technical));
        }

        var result = await _provider.GetProductsAsync(storeIdResult.Value, itemCode: null, cancellationToken);

        if (result.IsSuccess)
        {
            Products = result.Value;
        }

        return result;
    }

    /// <summary>Comprueba la conectividad con Ogloba (GET /test).</summary>
    public async Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return false;
        }

        var storeIdResult = DomainStoreId.Create(Configuration.StoreId);

        if (storeIdResult.IsFailure)
        {
            return false;
        }

        var result = await _provider.CheckConnectivityAsync(storeIdResult.Value, cancellationToken);
        return result.IsSuccess;
    }

    // ----- PIN administrador -----

    public async Task SetAdminPinAsync(string pin)
    {
        await SecureStorage.Default.SetAsync(AdminPinStorageKey, HashPin(pin));
        Preferences.Default.Set(AdminPinSetPreferenceKey, true);
    }

    public async Task<bool> VerifyAdminPinAsync(string pin)
    {
        var stored = await SecureStorage.Default.GetAsync(AdminPinStorageKey);
        return !string.IsNullOrEmpty(stored) &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(stored),
                   Encoding.UTF8.GetBytes(HashPin(pin)));
    }

    private static string HashPin(string pin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin.Trim())));

    // ----- Registro de cajeros -----

    public async Task<IReadOnlyList<string>> GetCashiersAsync(CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return Array.Empty<string>();
        }

        var json = await SecureStorage.Default.GetAsync(CashiersStoragePrefix + Configuration.StoreId);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task RegisterCashierAsync(string cashierId, CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return;
        }

        var id = cashierId.Trim();
        var cashiers = (await GetCashiersAsync(cancellationToken)).ToList();

        if (!cashiers.Any(c => string.Equals(c, id, StringComparison.OrdinalIgnoreCase)))
        {
            cashiers.Add(id);
            await SecureStorage.Default.SetAsync(
                CashiersStoragePrefix + Configuration.StoreId,
                JsonSerializer.Serialize(cashiers));
        }
    }

    /// <summary>Registra (si es nuevo) e inicia sesión del cajero para el turno.</summary>
    public async Task LoginCashierAsync(string cashierId, CancellationToken cancellationToken)
    {
        await RegisterCashierAsync(cashierId, cancellationToken);

        var trimmed = cashierId.Trim();
        Preferences.Default.Set(ActiveCashierPreferenceKey, trimmed);

        // El "último" queda registrado aunque después se cierre el turno: es con quien se firman
        // los cobros que ordena HiPOS, que no vuelven a pedir cajero.
        Preferences.Default.Set(LastCashierPreferenceKey, trimmed);
    }

    /// <summary>Quita un cajero de la caja, su contraseña y su marca de desactivado.</summary>
    public async Task RemoveCashierAsync(string cashierId, CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return;
        }

        var cashiers = (await GetCashiersAsync(cancellationToken))
            .Where(c => !string.Equals(c, cashierId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        await SecureStorage.Default.SetAsync(
            CashiersStoragePrefix + Configuration.StoreId,
            JsonSerializer.Serialize(cashiers));

        SecureStorage.Default.Remove(CashierPasswordKey(cashierId));

        // Si estaba desactivado, la marca se va con él: si mañana se registra otra vez un cajero
        // con el mismo nombre, entraría heredando una desactivación que nadie recuerda.
        await SetCashierEnabledAsync(cashierId, true, cancellationToken);
    }

    // ----- Activo / inactivo -----
    //
    // Se guarda como una lista APARTE de los desactivados, no cambiando el formato de la lista de
    // cajeros. Dos razones:
    //
    //   · Las cajas ya configuradas tienen guardado un array de strings. Cambiarlo a objetos
    //     obligaría a migrar el almacenamiento seguro de 512 tiendas, y una migración fallida
    //     deja la caja sin cajeros y sin poder operar.
    //   · Lo normal es que TODOS estén activos, así que esta lista casi siempre está vacía.
    //
    // Ausencia = activo. Una caja que nunca desactivó a nadie no tiene ni siquiera la clave.

    private const string DisabledCashiersStoragePrefix = "tefogloba.cashiers.disabled.";

    public async Task<IReadOnlyList<string>> GetDisabledCashiersAsync(CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return [];
        }

        var raw = await SecureStorage.Default.GetAsync(
            DisabledCashiersStoragePrefix + Configuration.StoreId);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            // Un JSON corrupto no puede dejar a toda la caja bloqueada: se asume que nadie está
            // desactivado, que es el estado seguro para poder seguir operando.
            return [];
        }
    }

    /// <summary>
    /// Un cajero desactivado sigue registrado y conserva su contraseña, pero no puede entrar.
    /// Es lo que se usa cuando alguien sale de vacaciones o cambia de tienda: borrarlo perdería
    /// su historial y obligaría a recrearlo.
    /// </summary>
    public async Task<bool> IsCashierEnabledAsync(string cashierId, CancellationToken cancellationToken)
    {
        var disabled = await GetDisabledCashiersAsync(cancellationToken);

        return !disabled.Any(c => string.Equals(c, cashierId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetCashierEnabledAsync(
        string cashierId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (Configuration is null)
        {
            return;
        }

        var id = cashierId.Trim();
        var disabled = (await GetDisabledCashiersAsync(cancellationToken)).ToList();
        var wasDisabled = disabled.Any(c => string.Equals(c, id, StringComparison.OrdinalIgnoreCase));

        if (enabled == !wasDisabled)
        {
            return;
        }

        if (enabled)
        {
            disabled.RemoveAll(c => string.Equals(c, id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            disabled.Add(id);

            // Al desactivar se le cierra el turno EN EL ACTO si es quien está operando. Dejarlo
            // dentro hasta que cierre sesión haría que "desactivar" no significara nada mientras
            // la caja siguiera abierta con él.
            if (string.Equals(ActiveCashierId, id, StringComparison.OrdinalIgnoreCase))
            {
                LogoutCashier();
            }
        }

        await SecureStorage.Default.SetAsync(
            DisabledCashiersStoragePrefix + Configuration.StoreId,
            JsonSerializer.Serialize(disabled));
    }

    // ----- Contraseña por cajero -----
    //
    // La clave se guarda hasheada, nunca en claro, en el almacenamiento seguro del dispositivo
    // y por tienda: un cajero de la 037 no entra en la 041 con la misma terminal.

    private const string CashierPasswordStoragePrefix = "cashier-password:";

    private string CashierPasswordKey(string cashierId) =>
        $"{CashierPasswordStoragePrefix}{Configuration?.StoreId}:{cashierId.Trim().ToLowerInvariant()}";

    /// <summary>
    /// <c>true</c> si el cajero ya tiene contraseña. Cuando es <c>false</c>, la pantalla le pide
    /// crearla — es la única forma de arrancar en una terminal recién configurada sin dejar una
    /// clave por defecto, que sería peor que no tener clave.
    /// </summary>
    public async Task<bool> HasCashierPasswordAsync(string cashierId)
    {
        var stored = await SecureStorage.Default.GetAsync(CashierPasswordKey(cashierId));
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task SetCashierPasswordAsync(string cashierId, string password) =>
        await SecureStorage.Default.SetAsync(CashierPasswordKey(cashierId), HashPin(password));

    public async Task<bool> VerifyCashierPasswordAsync(string cashierId, string password)
    {
        var stored = await SecureStorage.Default.GetAsync(CashierPasswordKey(cashierId));

        return !string.IsNullOrWhiteSpace(stored)
            && string.Equals(stored, HashPin(password), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica que el cashierId que HiPOS manda esté en la lista
    /// de cajeros registrados para esta tienda. Si la lista está vacía (tienda recién
    /// configurada, ningún cajero ha hecho login), retorna true para no bloquear — pero
    /// loguea el evento. Si la lista NO está vacía y el cashierId no está, retorna false.
    /// </summary>
    public async Task<bool> IsCashierRegisteredAsync(
        string? cashierId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cashierId))
        {
#if ANDROID
            Android.Util.Log.Warn("TefOgloba.HiPos",
                "IsCashierRegisteredAsync: cashierId is null/empty → reject");
#endif
            return false;
        }

        var registered = await GetCashiersAsync(cancellationToken);
#if ANDROID
        // Se registra CUÁNTOS hay, no QUIÉNES. La lista completa a logcat publica la nómina de la
        // tienda a cualquier app con permiso de lectura de logs — y es justo lo que la pantalla de
        // ingreso evita a propósito al pedir el usuario escrito en vez de mostrarlo en una lista.
        // No tenía sentido protegerlo en la UI y regalarlo por el log.
        //
        // Para diagnosticar alcanza con el conteo: lo que se necesita saber es si la lista estaba
        // vacía o si el cajero que mandó HiPOS no figura, y las dos cosas se ven igual de bien.
        Android.Util.Log.Info("TefOgloba.HiPos",
            "IsCashierRegisteredAsync: validando contra {0} cajeros registrados",
            registered.Count);
#endif
        if (registered.Count == 0)
        {
            // Lista vacía → tienda nueva, no podemos validar. Asumimos válido pero logueamos.
#if ANDROID
            Android.Util.Log.Warn("TefOgloba.HiPos",
                "IsCashierRegisteredAsync: empty cashier list → assuming valid");
#endif
            return true;
        }

        var isMatch = registered.Any(c => string.Equals(c, cashierId.Trim(), StringComparison.OrdinalIgnoreCase));
#if ANDROID
        Android.Util.Log.Info("TefOgloba.HiPos",
            "IsCashierRegisteredAsync: result={0} for cashierId='{1}'", isMatch, cashierId);
#endif
        return isMatch;
    }

    public void LogoutCashier() => Preferences.Default.Remove(ActiveCashierPreferenceKey);

    // ----- Replicación entre cajas de la misma tienda -----
    //
    // Estos dos métodos viven acá y no en un servicio aparte porque las llaves del almacenamiento
    // seguro son privadas de esta clase. Sacarlos afuera obligaría a hacerlas públicas, y entonces
    // cualquier pantalla podría escribir el hash del PIN de administrador.

    /// <summary>
    /// Arma el sobre que esta caja le entrega a las demás de la tienda.
    /// <para>
    /// Se llama en CADA entrega, no una vez al abrir la ventana: entre la primera caja y la
    /// tercera el administrador pudo dar de alta otro cajero.
    /// </para>
    /// <para>
    /// <c>AssignedTerminalIds</c> lleva solo la caja propia. Esta caja no sabe qué otras existen en
    /// la tienda —nadie lleva ese registro— y lo que evita colisiones es que
    /// <see cref="Permoda.Pay.Application.Features.Provisioning.PairingHost"/> va reservando lo que
    /// entrega dentro de la ventana.
    /// </para>
    /// </summary>
    public async Task<TerminalConfigurationEnvelope?> ExportForReplicationAsync(
        CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (Configuration is not { IsComplete: true } configuration)
        {
            return null;
        }

        var credential = await _credentials.GetAsync(
            DomainStoreId.Create(configuration.StoreId).Value, cancellationToken);

        if (credential.IsFailure)
        {
            return null;
        }

        var names = await GetCashiersAsync(cancellationToken);
        var disabled = await GetDisabledCashiersAsync(cancellationToken);
        var cashiers = new List<ReplicatedCashier>(names.Count);

        foreach (var name in names)
        {
            // Se copia el HASH, nunca una contraseña en claro — que además no existe: se guarda
            // hasheada desde que se crea. El cajero entra en la caja nueva con la de siempre.
            var hash = await SecureStorage.Default.GetAsync(CashierPasswordKey(name)) ?? string.Empty;

            cashiers.Add(new ReplicatedCashier(
                name,
                hash,
                IsEnabled: !disabled.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase))));
        }

        return new TerminalConfigurationEnvelope(
            configuration.StoreId,
            credential.Value.Password,
            configuration.BaseUrl,
            configuration.ApiVersion,
            configuration.StoreName,
            await SecureStorage.Default.GetAsync(AdminPinStorageKey) ?? string.Empty,
            cashiers,
            AssignedTerminalIds: [configuration.TerminalId]);
    }

    /// <summary>
    /// Escribe en ESTA caja la configuración recibida de otra.
    /// <para>
    /// El <paramref name="terminalId"/> viene aparte y no del sobre a propósito: es lo único que
    /// NO se replica. Dos cajas con el mismo identificador firmarían igual sus operaciones contra
    /// Ogloba y la bitácora dejaría de decir dónde ocurrió cada cobro.
    /// </para>
    /// <para>
    /// El orden importa: la configuración de tienda va PRIMERO porque las llaves de los cajeros y
    /// del padrón se construyen con el StoreId. Escribirlos antes los dejaría colgados del StoreId
    /// viejo —o de ninguno— y la caja quedaría sin cajeros sin que nada lo avisara.
    /// </para>
    /// </summary>
    public async Task<bool> ImportFromReplicationAsync(
        TerminalConfigurationEnvelope envelope,
        string terminalId,
        CancellationToken cancellationToken)
    {
        var storeIdResult = DomainStoreId.Create(envelope.StoreId);

        if (storeIdResult.IsFailure || string.IsNullOrWhiteSpace(terminalId))
        {
            return false;
        }

        var configuration = new StoreConfiguration(
            envelope.StoreId,
            terminalId.Trim(),
            envelope.BaseUrl,
            envelope.ApiVersion,
            envelope.StoreName);

        await _configProvider.PersistAsync(configuration, cancellationToken);
        Configuration = configuration;
        _loaded = true;

        var credential = await _credentials.PersistAsync(
            storeIdResult.Value, envelope.SubscriptionKey, cancellationToken);

        if (credential.IsFailure)
        {
            return false;
        }

        // El padrón y los desactivados se escriben completos, no se mezclan con lo que hubiera:
        // una caja que recibe configuración es una caja que se está montando, y arrastrar cajeros
        // de una instalación anterior es cómo se cuelan usuarios que nadie dio de alta acá.
        var names = envelope.Cashiers.Select(cashier => cashier.Id).ToList();

        await SecureStorage.Default.SetAsync(
            CashiersStoragePrefix + envelope.StoreId,
            JsonSerializer.Serialize(names));

        await SecureStorage.Default.SetAsync(
            DisabledCashiersStoragePrefix + envelope.StoreId,
            JsonSerializer.Serialize(
                envelope.Cashiers.Where(c => !c.IsEnabled).Select(c => c.Id).ToList()));

        foreach (var cashier in envelope.Cashiers)
        {
            if (!string.IsNullOrWhiteSpace(cashier.PasswordHash))
            {
                await SecureStorage.Default.SetAsync(
                    CashierPasswordKey(cashier.Id), cashier.PasswordHash);
            }
        }

        if (!string.IsNullOrWhiteSpace(envelope.AdminPinHash))
        {
            await SecureStorage.Default.SetAsync(AdminPinStorageKey, envelope.AdminPinHash);
            Preferences.Default.Set(AdminPinSetPreferenceKey, true);
        }

        Preferences.Default.Set(SetupCompletedPreferenceKey, true);

        // El turno NO se hereda. La caja nueva arranca sin nadie dentro: quien opere tiene que
        // entrar con su usuario, o la primera venta quedaría firmada por el cajero que estaba
        // en la otra caja.
        LogoutCashier();
        Preferences.Default.Remove(LastCashierPreferenceKey);

        return true;
    }
}
