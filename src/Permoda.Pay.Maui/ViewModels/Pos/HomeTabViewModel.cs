using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui.ViewModels.Pos;

public sealed partial class HomeTabViewModel : ObservableObject
{
    private readonly PosSession _session;
    private readonly CashierActivityLog _activityLog;

    public HomeTabViewModel(
        PosHeaderViewModel header,
        PosSession session,
        CashierActivityLog activityLog)
    {
        Header = header;
        _session = session;
        _activityLog = activityLog;

        // Refresca el saludo cuando cambie el cajero en turno o el entorno.
        Header.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosHeaderViewModel.Cashier))
            {
                OnPropertyChanged(nameof(Greeting));
            }
            else if (e.PropertyName is nameof(PosHeaderViewModel.StoreId)
                or nameof(PosHeaderViewModel.EnvironmentHost)
                or nameof(PosHeaderViewModel.EnvironmentName))
            {
                OnPropertyChanged(nameof(EndpointDisplay));
                OnPropertyChanged(nameof(TerminalDisplay));
            }
        };
    }

    public PosHeaderViewModel Header { get; }

    /// <summary>Saludo de la cabecera: "Hola, María" según el cajero en turno.</summary>
    public string Greeting
    {
        get
        {
            var cashier = Header?.Cashier ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cashier) || cashier == "—")
            {
                return "Hola";
            }
            return $"Hola, {cashier}";
        }
    }

    /// <summary>Endpoint activo de Ogloba (ej: "co-ts.ogloba.com").</summary>
    public string EndpointDisplay
    {
        get
        {
            var host = Header?.EnvironmentHost ?? string.Empty;
            var api = Header?.EnvironmentName ?? string.Empty;
            return string.IsNullOrEmpty(host) ? api : $"{api} · {host}";
        }
    }

    /// <summary>Tienda/caja configurados en la terminal.</summary>
    public string TerminalDisplay
    {
        get
        {
            var storeId = Header?.StoreId ?? string.Empty;
            return string.IsNullOrEmpty(storeId) || storeId == "—"
                ? "Sin tienda configurada"
                : $"Tienda {storeId}";
        }
    }

    [ObservableProperty]
    private int _operationsCount;

    /// <summary>
    /// Módulo resaltado en el dashboard: "activate", "redeem" o "balance". Vacío por defecto —
    /// las tarjetas nacen blancas y solo la seleccionada se pinta de azul.
    /// </summary>
    [ObservableProperty]
    private string _selectedModule = string.Empty;

    partial void OnOperationsCountChanged(int value) => OnPropertyChanged(nameof(ShiftSummary));

    /// <summary>Resumen del turno: operaciones registradas + hora y fecha actuales.</summary>
    public string ShiftSummary
    {
        get
        {
            var culture = new CultureInfo("es-CO");
            var now = DateTime.Now;
            var day = Capitalize(culture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek), culture);
            var month = Capitalize(
                culture.DateTimeFormat.GetAbbreviatedMonthName(now.Month).TrimEnd('.'),
                culture);
            var plural = OperationsCount == 1 ? "operación" : "operaciones";

            return $"Tienes {OperationsCount} {plural} en este turno · {now:HH:mm} · {day} {now.Day} {month}";
        }
    }

    // Las iniciales del cajero, la línea de rol y el pie de terminal viven en
    // PosHeaderViewModel: los consumen TopBarView y AppFooterView, que son compartidos.

    private static string Capitalize(string value, CultureInfo culture) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpper(value[0], culture) + value[1..];

    public ObservableCollection<string> RecentActivity { get; } = new();

    public bool HasActivity => RecentActivity.Count > 0;

    public async Task RefreshAsync()
    {
        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();

        var entries = await _activityLog.GetAllAsync(CancellationToken.None);
        OperationsCount = entries.Count;

        RecentActivity.Clear();
        foreach (var entry in entries.Take(10))
        {
            var mark = entry.Success ? "✓" : "✗";
            RecentActivity.Add(
                $"{entry.TimestampUtc.LocalDateTime:HH:mm} · {entry.CashierId} · {entry.Action} {mark}");
        }

        // Propiedades calculadas (no ObservableProperty): hay que avisarles al recargar.
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(ShiftSummary));

        // Cachear el catálogo de la tienda al inicio para que la UI
        // pueda gatekear "Activar Virtual" según `allowedActivate` del producto.
        // Failure silenciosa — la UI funciona con cache vacío si Ogloba no responde.
        await Header.LoadBusinessUnitBalanceAsync();
        await _session.LoadProductsAsync(CancellationToken.None);

        // Verificar conectividad con Ogloba al iniciar para que el chip de estado
        // muestre "en línea" / "sin conexión" apenas arranca la app.
        if (Header.CheckConnectivityCommand.CanExecute(null))
        {
            await Header.CheckConnectivityCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Resalta el módulo elegido y navega. La tarjeta queda en azul mientras dura la selección,
    /// de forma que al volver al inicio el cajero ve dónde estuvo.
    /// </summary>
    [RelayCommand]
    private Task SelectModuleAsync(string module)
    {
        SelectedModule = module ?? string.Empty;

        return module switch
        {
            "activate" => GoActivateAsync(),
            "redeem" => GoRedeemAsync(),
            "balance" => GoBalanceAsync(),
            _ => Task.CompletedTask
        };
    }

    // Activar pasa PRIMERO por elegir físico o virtual: la pantalla de activación se dedica a un
    // solo tipo y no tiene que mostrar campos de los dos. Ver ActivateModeViewModel.
    [RelayCommand]
    private Task GoActivateAsync() => Shell.Current.GoToAsync(AppRoutes.ActivateMode);

    [RelayCommand]
    private Task GoRedeemAsync() => Shell.Current.GoToAsync("//redeem");

    [RelayCommand]
    private Task GoBalanceAsync() => Shell.Current.GoToAsync("//balance");

    [RelayCommand]
    private Task GoAuditAsync() => Shell.Current.GoToAsync("//audit");

    [RelayCommand]
    private Task OpenSetupAsync() => Shell.Current.GoToAsync(AppRoutes.Setup);
}
