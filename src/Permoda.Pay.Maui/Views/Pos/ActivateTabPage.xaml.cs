using Android.Util;
using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

public partial class ActivateTabPage : ContentPage
{
    private const string LogTag = "TefOgloba";

    private readonly ActivateTabViewModel _viewModel;

    public ActivateTabPage(ActivateTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SafeEnsureSessionAsync();

        // El lector wedge escribe como teclado: sin foco en el campo, escanear no hacía nada.
        // Se enfoca al entrar pero SIN levantar el teclado en pantalla, que en la terminal se
        // come cerca de la mitad del alto. Si el cajero toca el campo, Android lo muestra.
        //
        // Reintentado: un Arm() único se perdía si MAUI no había creado el EditText todavía, y
        // el primer escaneo caía en el vacío. Ver ScannerFocus.ArmWhenReadyAsync.
        _ = Common.ScannerFocus.ArmWhenReadyAsync(DraftCardEntry);

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDisappearing();
    }

    /// <summary>
    /// Devuelve el cursor al campo cuando termina una operación o cuando el campo se vacía al
    /// mandar el bono a la lista. Sin esto, el Enter con que el lector cierra cada escaneo cae
    /// sobre el último botón accionado y lo vuelve a disparar.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var finishedWorking = e.PropertyName == nameof(ActivateTabViewModel.IsBusy) && !_viewModel.IsBusy;
        var fieldCleared = e.PropertyName == nameof(ActivateTabViewModel.DraftCardValue)
            && string.IsNullOrEmpty(_viewModel.DraftCardValue);

        if (finishedWorking || fieldCleared)
        {
            Dispatcher.Dispatch(() => Common.ScannerFocus.Arm(DraftCardEntry));
        }
    }

    // Botón "Escanear": el lector wedge escribe como teclado, así que basta con dejar el
    // cursor en el campo del serial.
    private void OnScanTapped(object? sender, TappedEventArgs e) =>
        Common.ScannerFocus.Arm(DraftCardEntry);

    // Enter del lector (o del teclado) al terminar de capturar el serial.
    private void OnDraftCardCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Dispatch(() =>
        {
            if (BindingContext is ActivateTabViewModel vm)
            {
                vm.HandleScannedSerial(DraftCardEntry.Text ?? string.Empty);
            }
        });
    }

    private async Task SafeEnsureSessionAsync()
    {
        try
        {
            await _viewModel.EnsureSessionAsync();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[ActivateTabPage] EnsureSession failed: {exception}");
        }
    }
}