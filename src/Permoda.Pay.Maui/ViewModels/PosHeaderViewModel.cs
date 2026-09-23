using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels;

/// <summary>
/// Encabezado común (Inicio / Activar / Redimir): tienda, nombre, cajero y estado de
/// conectividad con Ogloba. Es singleton para que el estado se comparta entre pantallas.
/// </summary>
public sealed partial class PosHeaderViewModel : ObservableObject
{
    private readonly PosSession _session;

    public PosHeaderViewModel(PosSession session)
    {
        _session = session;
    }

    [ObservableProperty]
    private string _storeId = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStoreName))]
    private string _storeName = string.Empty;

    public bool HasStoreName => !string.IsNullOrWhiteSpace(StoreName);

    [ObservableProperty]
    private string _cashier = "—";

    [ObservableProperty]
    private bool _isSandbox;

    /// <summary>Host corto para el distintivo del flyout (ej: "co-ts" / "co-prod").</summary>
    public string EnvironmentHost
    {
        get
        {
            var host = _session.Environment.Host;
            return host;
        }
    }

    /// <summary>Etiqueta completa del ambiente para mostrar.</summary>
    public string EnvironmentName => _session.Environment.DisplayName;

    // "unknown" | "checking" | "online" | "offline"
    [ObservableProperty]
    private string _connectivityState = "unknown";

    [ObservableProperty]
    private string _connectivityText = "○ Verificar Ogloba";

    // docs/OGLOBA_API_REFERENCE.md §3.17 (getBuInfo): saldo disponible y neto del comercio.
    // Se muestra solo si Ogloba lo devuelve; si no, queda null y el chip se oculta.
    [ObservableProperty]
    private long? _businessUnitBalanceMinorUnits;

    public bool HasBusinessUnitBalance => BusinessUnitBalanceMinorUnits.HasValue;

    public string BusinessUnitBalanceDisplay => BusinessUnitBalanceMinorUnits.HasValue
        ? $"$ {BusinessUnitBalanceMinorUnits.Value:N0} COP"
        : string.Empty;

    /// <summary>
    /// Título del chip de la barra superior.
    /// <para>
    /// En un cobro ordenado por HiPOS muestra la CAJA, no el cajero: en ese flujo el turno del
    /// módulo no existe —quien se identificó fue el cajero del POS— así que poner un nombre ahí
    /// era mostrar un dato que en esa pantalla no significa nada, y encima invitaba a tocarlo.
    /// En la apertura manual sigue siendo el cajero, que es con quien se firma la operación.
    /// </para>
    /// </summary>
    public string ChipTitle
    {
        get
        {
            if (!Services.HiPosFlow.IsActive)
            {
                return Cashier;
            }

            var terminal = _session.Configuration?.TerminalId;
            return string.IsNullOrWhiteSpace(terminal) ? "Caja" : terminal;
        }
    }

    /// <summary>Segunda línea del chip: rol y caja, o solo "Caja" cuando el cobro viene de HiPOS.</summary>
    public string ChipSubtitle
    {
        get
        {
            var terminal = _session.Configuration?.TerminalId;

            if (Services.HiPosFlow.IsActive)
            {
                return "Caja";
            }

            return string.IsNullOrWhiteSpace(terminal) ? "Cajero" : $"Cajero · {terminal}";
        }
    }

    /// <summary>Iniciales para el avatar de la barra superior, del mismo dato que el título.</summary>
    public string Initials
    {
        get
        {
            var source = (ChipTitle ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(source) || source == "—")
            {
                return "··";
            }

            var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return words.Length >= 2
                ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
                : source[..Math.Min(2, source.Length)].ToUpperInvariant();
        }
    }

    /// <summary>Segunda línea del chip de usuario: rol y caja en la que opera.</summary>
    public string RoleLine => ChipSubtitle;

    /// <summary>Estado de Ogloba en versión corta, para la esquina de las pantallas.</summary>
    public string ShortStatusText => ConnectivityState switch
    {
        "online" => "En línea",
        "offline" => "Sin conexión",
        "checking" => "Verificando…",
        _ => "Sin verificar"
    };

    /// <summary>Mismo estado, redactado para el pie de página.</summary>
    public string SyncText => ConnectivityState switch
    {
        "online" => "Sincronizado",
        "offline" => "Sin conexión",
        "checking" => "Sincronizando…",
        _ => "Sin verificar"
    };

    /// <summary>Pie de página: versión de la app, caja y tienda configuradas.</summary>
    public string TerminalFooter
    {
        get
        {
            var parts = new List<string> { $"v{AppInfo.Current.VersionString}" };
            var terminal = _session.Configuration?.TerminalId;
            var store = _session.StoreName;

            if (string.IsNullOrWhiteSpace(store))
            {
                store = _session.Configuration?.StoreId;
            }

            if (!string.IsNullOrWhiteSpace(terminal))
            {
                parts.Add(terminal);
            }

            if (!string.IsNullOrWhiteSpace(store))
            {
                parts.Add(store);
            }

            return string.Join(" · ", parts);
        }
    }

    partial void OnCashierChanged(string value)
    {
        OnPropertyChanged(nameof(ChipTitle));
        OnPropertyChanged(nameof(ChipSubtitle));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(RoleLine));
    }

    partial void OnConnectivityStateChanged(string value)
    {
        OnPropertyChanged(nameof(ShortStatusText));
        OnPropertyChanged(nameof(SyncText));
    }

    public void Refresh()
    {
        IsSandbox = _session.Environment.IsSandbox;
        StoreId = string.IsNullOrWhiteSpace(_session.Configuration?.StoreId)
            ? "—"
            : _session.Configuration!.StoreId;
        StoreName = _session.StoreName;
        // El del turno abierto y, si no hay, el último que operó: en un cobro de HiPOS no se abre
        // turno, y el chip de la barra tiene que mostrar igual con quién se está firmando.
        Cashier = _session.OperatingCashierId ?? "—";

        // El chip cambia de contenido según si el cobro lo ordenó HiPOS, y eso solo se sabe en
        // tiempo de ejecución: hay que notificarlo en cada refresco, no solo al cambiar de cajero.
        OnPropertyChanged(nameof(ChipTitle));
        OnPropertyChanged(nameof(ChipSubtitle));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(RoleLine));
        OnPropertyChanged(nameof(TerminalFooter));
    }

    partial void OnBusinessUnitBalanceMinorUnitsChanged(long? value) =>
        OnPropertyChanged(nameof(HasBusinessUnitBalance));

    [RelayCommand]
    private async Task CheckConnectivityAsync()
    {
        ConnectivityState = "checking";
        ConnectivityText = "⟳ Verificando…";

        var online = await _session.CheckConnectivityAsync(CancellationToken.None);

        ConnectivityState = online ? "online" : "offline";
        ConnectivityText = online ? "✓ Ogloba en línea" : "⚠ Sin conexión";

        if (online)
        {
            // Aprovechamos la conexión verificada para traer también el saldo del comercio
            // (docs/OGLOBA_API_REFERENCE.md §3.17). Failure silenciosa — el chip simplemente
            // no aparece si Ogloba no devuelve saldo (p. ej. en tiendas nuevas sin crédito).
            await LoadBusinessUnitBalanceAsync();
        }
    }

    public async Task LoadBusinessUnitBalanceAsync()
    {
        try
        {
            BusinessUnitBalanceMinorUnits = await _session.GetBusinessUnitBalanceAsync(CancellationToken.None);
        }
        catch
        {
            // Silencioso: el saldo es informativo, no debe romper la UI.
        }
    }

    [RelayCommand]
    private async Task ChangeCashierAsync()
    {
        _session.LogoutCashier();

        // Al selector de cajero, que es el único camino que EXIGE contraseña. Antes iba a
        // AppRoutes.Login, una pantalla que dejaba entrar solo con elegir el nombre: cualquiera
        // podía tocar el chip, cambiarse al cajero de al lado y operar firmando con su nombre.
        //
        // Se vuelve a Inicio antes de apilar el selector porque es una ruta global y no puede ser
        // la única página de la pila; además, así lo que asoma al cerrarla es el dashboard.
        await Shell.Current.GoToAsync(AppRoutes.Home);
        await Shell.Current.GoToAsync(AppRoutes.CashierLogin);
    }
}
