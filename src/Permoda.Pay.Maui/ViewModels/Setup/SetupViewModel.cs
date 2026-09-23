using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Configuration;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.ViewModels.Setup;

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly PosSession _session;

    public SetupViewModel(PosSession session)
    {
        _session = session;

        var environment = _session.Environment;
        EnvironmentName = environment.DisplayName;
        EnvironmentHost = environment.Host;
        BaseUrl = environment.OglobaBaseUrl;
        ApiVersion = environment.OglobaApiVersion;
        IsSandbox = environment.IsSandbox;
        CanAutofillSandbox = environment.Sandbox is not null;
    }

    // Identidad de la caja
    [ObservableProperty] private string _storeId = string.Empty;
    [ObservableProperty] private string _terminalId = string.Empty;
    [ObservableProperty] private string _oglobaPassword = string.Empty;
    [ObservableProperty] private string _storeName = string.Empty;
    [ObservableProperty] private string _activeCashier = "—";

    // PIN administrador
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _confirmPin = string.Empty;
    [ObservableProperty] private string _pinEntry = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConfig))]
    [NotifyPropertyChangedFor(nameof(ShowUnlock))]
    private bool _isFirstRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConfig))]
    [NotifyPropertyChangedFor(nameof(ShowUnlock))]
    private bool _isUnlocked;

    public bool ShowConfig => IsUnlocked;
    public bool ShowUnlock => !IsFirstRun && !IsUnlocked;
    public bool IsAlreadyConfigured => !IsFirstRun;

    // Ambiente (solo lectura)
    [ObservableProperty] private string _environmentName = string.Empty;
    [ObservableProperty] private string _environmentHost = string.Empty;
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _apiVersion = string.Empty;
    [ObservableProperty] private bool _isSandbox;

    public bool IsProduction => !IsSandbox;

    [ObservableProperty] private bool _canAutofillSandbox;
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Mensaje en línea de la pantalla de configuración. Reemplaza a los toasts: en una caja el
    /// cajero suele estar mirando el campo que acaba de llenar, no la esquina de la pantalla, y
    /// un aviso que se desvanece se pierde justo cuando explica por qué no se guardó.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public async Task RefreshAsync()
    {
        await _session.EnsureLoadedAsync(CancellationToken.None);

        IsFirstRun = !_session.IsAdminPinSet;
        PinEntry = string.Empty;

        if (_session.Configuration is { } configuration)
        {
            StoreId = configuration.StoreId;
            TerminalId = configuration.TerminalId;
            StoreName = configuration.StoreName;
        }

        ActiveCashier = _session.ActiveCashierId ?? "—";

        // Mientras el alta no esté terminada, Configuración NO pide el PIN. Antes se pedía en
        // cuanto el PIN existía, así que el administrador creaba el PIN, volvía para seguir y la
        // pantalla le exigía la clave que acababa de definir — quedaba trabado a mitad de su
        // propia configuración.
        //
        // El PIN protege la configuración de una caja YA montada; no tiene nada que proteger en
        // una que todavía no puede operar.
        IsUnlocked = IsFirstRun || !_session.IsConfigured;

        // Copiar de otra caja se ofrece SOLO en una caja sin configurar. En una que ya opera sería
        // una forma de pisarle la tienda y los cajeros sin querer — y encima la llave, que es lo
        // último que uno quiere poder cambiar por accidente desde una pantalla.
        CanJoinReplication = !_session.IsConfigured;

        // Repartir exige TODO lo que se copia: tienda, credenciales, PIN y al menos un cajero.
        // Ofrecerlo antes dejaría cajas replicando media configuración, y la caja receptora no
        // tendría cómo saber que lo que recibió venía incompleto — se vería configurada y
        // fallaría en la primera venta, lejos de acá y con un error de Ogloba que no apunta a
        // esta pantalla.
        CanReplicate =
            _session.IsConfigured
            && _session.IsAdminPinSet
            && (await _session.GetCashiersAsync(CancellationToken.None)).Count > 0;
    }

    /// <summary>Esta caja está completa y puede repartir su configuración al resto de la tienda.</summary>
    [ObservableProperty]
    private bool _canReplicate;

    [RelayCommand]
    private async Task ReplicateAsync() =>
        await Shell.Current.GoToAsync(Common.AppRoutes.ReplicationHost);

    /// <summary>Esta caja está en blanco y puede copiar la configuración de otra de la tienda.</summary>
    [ObservableProperty]
    private bool _canJoinReplication;

    /// <summary>
    /// Salida para quien entró acá sin tener el PIN.
    /// <para>
    /// Existe porque esta pantalla apaga el menú lateral hasta que el PIN acierte, y sin una
    /// salida quedaría atrapado quien la abrió por error. El destino NO es fijo: con turno
    /// abierto se vuelve a Inicio, y sin turno al ingreso de cajero. Mandar siempre a Inicio
    /// sería cambiar un agujero por otro — desde el ingreso se entraría acá y se saldría a
    /// Inicio, operando sin haber abierto turno.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task GoBackAsync() =>
        await Shell.Current.GoToAsync(
            _session.HasActiveCashier ? Common.AppRoutes.Home : Common.AppRoutes.CashierLogin);

    [RelayCommand]
    private async Task JoinReplicationAsync() =>
        await Shell.Current.GoToAsync(Common.AppRoutes.ReplicationJoin);

    [RelayCommand]
    private async Task UnlockAsync()
    {


        if (await _session.VerifyAdminPinAsync(PinEntry))
        {
            IsUnlocked = true;
            PinEntry = string.Empty;
        }
        else
        {
            StatusMessage = "PIN incorrecto. Verifica el PIN de administrador e inténtalo de nuevo.";
        }
    }

    [RelayCommand]
    private void FillSandbox()
    {
        var hint = _session.Environment.Sandbox;

        if (hint is null)
        {
            return;
        }

        StoreId = hint.SampleStoreId;
        OglobaPassword = hint.Password;

        if (string.IsNullOrWhiteSpace(TerminalId))
        {
            TerminalId = "CAJA-01";
        }

        StatusMessage = $"Datos de prueba cargados ({EnvironmentHost}). Revisa la caja antes de guardar.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {


        if (string.IsNullOrWhiteSpace(StoreId) || string.IsNullOrWhiteSpace(TerminalId))
        {
            StatusMessage = "Faltan datos: la tienda y la caja son obligatorias.";
            return;
        }

        if (IsFirstRun)
        {
            if (NewPin.Trim().Length < 4)
            {
                StatusMessage = "El PIN de administrador debe tener al menos 4 dígitos.";
                return;
            }

            if (NewPin.Trim() != ConfirmPin.Trim())
            {
                StatusMessage = "El PIN y su confirmación no coinciden.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OglobaPassword))
            {
                StatusMessage = "Ingresa la contraseña Ogloba de la tienda.";
                return;
            }
        }

        IsBusy = true;

        var configuration = new StoreConfiguration(
            StoreId.Trim(),
            TerminalId.Trim(),
            BaseUrl.Trim(),
            ApiVersion.Trim(),
            StoreName.Trim());

        var result = await _session.SaveConfigurationAsync(
            configuration,
            string.IsNullOrWhiteSpace(OglobaPassword) ? null : OglobaPassword,
            CancellationToken.None);

        if (result.IsFailure)
        {
            IsBusy = false;
            StatusMessage = $"No se pudo guardar. {result.Failure.Code}: {result.Failure.Description}";
            return;
        }

        if (IsFirstRun)
        {
            await _session.SetAdminPinAsync(NewPin);
        }

        IsBusy = false;
        OglobaPassword = string.Empty;
        NewPin = string.Empty;
        ConfirmPin = string.Empty;
        StoreName = _session.StoreName;
        IsFirstRun = !_session.IsAdminPinSet;

        StatusMessage = $"Configuración guardada. Tienda {configuration.StoreId} lista en {EnvironmentHost}.";

        // Siguiente paso del alta: registrar los cajeros. Es su propia pantalla porque son dos
        // tareas distintas — la caja se define una vez, los cajeros entran y salen.
        await Shell.Current.GoToAsync(AppRoutes.CashierSetup);
    }
}
