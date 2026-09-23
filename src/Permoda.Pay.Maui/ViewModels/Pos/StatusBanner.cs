using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Permoda.Pay.Maui.ViewModels.Pos;

public enum StatusBannerLevel
{
    Success,
    Error,
    Info
}

public sealed partial class StatusBanner : ObservableObject
{
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private StatusBannerLevel _level;

    public void ShowSuccess(string title, string message)
    {
        Level = StatusBannerLevel.Success;
        Title = title;
        Message = message;
        IsVisible = true;
    }

    public void ShowError(string title, string message)
    {
        Level = StatusBannerLevel.Error;
        Title = title;
        Message = message;
        IsVisible = true;
    }

    public void ShowInfo(string title, string message)
    {
        Level = StatusBannerLevel.Info;
        Title = title;
        Message = message;
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
        Title = string.Empty;
        Message = string.Empty;
    }
}

public sealed record TestCard(string Number, string ProductCode, string Kind);

public partial class PosViewModelBase : ObservableObject
{
    // Sin catálogo hardcodeado: el cajero digita o escanea el serial del bono en cada
    // operación. Esto evita dejar PANs completos en código (regla HiOSTORE "no third-party
    // brands" + alineado con "PAN enmascarado en storage", sin tener nunca el serial crudo
    // persistido ni quemado en el APK).
    public ObservableCollection<TestCard> Cards { get; } = new();

    [ObservableProperty]
    private TestCard? _selectedCard;

    [ObservableProperty]
    private string _statusMessage = "Listo.";

    [ObservableProperty]
    private bool _isBusy;

    protected static string DefaultCurrency => "COP";

    // Ogloba usa el importe tal cual (1:1 con lo que ve el cajero). El monto que se ingresa
    // es el valor final en COP; no se multiplica ni divide.
    protected static string FormatPesos(long amount) => $"${amount:N0} COP";

    protected void SetBusy(string message)
    {
        IsBusy = true;
        StatusMessage = message;
    }

    protected void SetIdle(string message)
    {
        IsBusy = false;
        StatusMessage = message;
    }

    /// <summary>Abre la configuración de la terminal desde el pie de página.</summary>
    [RelayCommand]
    private Task OpenSetupAsync() => Shell.Current.GoToAsync(Common.AppRoutes.Setup);
}