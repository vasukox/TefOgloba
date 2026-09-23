using Android.Util;
using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

public partial class BalanceTabPage : ContentPage
{
    private const string LogTag = "TefOgloba";

    private readonly BalanceTabViewModel _viewModel;

    public BalanceTabPage(BalanceTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Entrar a Consultar es empezar una consulta nueva. El ViewModel es singleton y conserva
        // lo anterior, así que al volver aparecía el bono del cliente pasado con su saldo puesto.
        _viewModel.ResetForNewLookup();

        _ = SafeEnsureSessionAsync();

        // El lector wedge escribe como teclado: sin foco en el campo, escanear no hacía nada y
        // el cajero tenía que tocar el serial primero. Se enfoca al entrar pero SIN levantar el
        // teclado en pantalla, que en la terminal se come media pantalla.
        // Reintentado hasta que el campo exista: un Arm() único se perdía cuando MAUI todavía no
        // había creado el EditText, y el primer escaneo caía en el vacío.
        _ = Common.ScannerFocus.ArmWhenReadyAsync(CardEntry);

        // Desuscribir primero: OnAppearing corre cada vez que se vuelve a la pantalla y la
        // instancia es la misma, así que sin esto se acumularían manejadores.
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDisappearing();
    }

    /// <summary>
    /// Al terminar una consulta el cursor vuelve al campo del serial.
    /// <para>
    /// El lector termina cada escaneo con un Enter. Si el foco quedó en un botón —consultar deja
    /// el foco en el botón que se accionó—, ese Enter lo ACCIONA: el segundo escaneo seguido
    /// pulsaba "Volver al menú" y sacaba al cajero de la pantalla en mitad de la consulta.
    /// Devolviendo el cursor al campo, el Enter siempre cae donde tiene que caer.
    /// </para>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BalanceTabViewModel.IsBusy) || _viewModel.IsBusy)
        {
            return;
        }

        Dispatcher.Dispatch(() => Common.ScannerFocus.Arm(CardEntry));
    }

    private async Task SafeEnsureSessionAsync()
    {
        try
        {
            await _viewModel.EnsureSessionAsync();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[BalanceTabPage] EnsureSession failed: {exception}");
        }
    }

    private void OnCardCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Dispatch(() =>
        {
            if (BindingContext is BalanceTabViewModel vm)
            {
                if (vm.CheckBalanceCommand.CanExecute(null))
                {
                    vm.CheckBalanceCommand.Execute(null);
                }
            }
        });
    }
}