using Permoda.Pay.Maui.ViewModels.Setup;

namespace Permoda.Pay.Maui.Views.Setup;

public partial class CashierSetupPage : ContentPage
{
    private readonly CashierSetupViewModel _viewModel;

    public CashierSetupPage(CashierSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Mismo motivo que en el selector: <c>async void</c> se traga las excepciones y deja la
    /// pantalla dibujada a medias. Capturadas, quedan en el log y a la vista.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("TefOgloba", $"[CashierSetupPage] Refresh falló: {exception}");
            _viewModel.Message = $"No se pudo leer la lista de cajeros. {exception.Message}";
        }
    }
}
