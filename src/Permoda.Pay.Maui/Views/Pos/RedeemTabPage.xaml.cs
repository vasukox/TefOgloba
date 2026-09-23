using Android.Util;
using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

public partial class RedeemTabPage : ContentPage
{
    private const string LogTag = "TefOgloba";

    private readonly RedeemTabViewModel _viewModel;

    public RedeemTabPage(RedeemTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Última cobertura pintada. Sirve para no relanzar la animación cuando el valor no cambió:
    /// <c>NotifyCoverageChanged</c> se dispara con cada tecla del monto, y reanimar sobre una
    /// animación en curso la deja temblando.
    /// </summary>
    private double _paintedCoverage = -1;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SafeEnsureSessionAsync();

        // La barra arranca donde toque, sin animar: animar desde cero al entrar haría que una
        // venta ya cubierta se dibujara llenándose sola, como si acabara de pasar algo.
        _paintedCoverage = _viewModel.CoverageRatio;
        CoverageBar.Progress = _paintedCoverage;

        // El lector wedge escribe como teclado: sin foco en el campo, escanear no hace nada.
        //
        // Se REINTENTA hasta que el campo exista de verdad. Un Arm() único acá se perdía cuando
        // MAUI todavía no había creado el EditText —lo normal en la primera navegación—, y el
        // primer escaneo caía en el vacío: el cajero tenía que tocar el campo y volver a pasar
        // la tarjeta.
        _ = Common.ScannerFocus.ArmWhenReadyAsync(BarcodeEntry);

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        BarcodeEntry.Unfocused -= OnBarcodeUnfocused;
        BarcodeEntry.Unfocused += OnBarcodeUnfocused;
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        BarcodeEntry.Unfocused -= OnBarcodeUnfocused;
        base.OnDisappearing();
    }

    /// <summary>
    /// El campo del serial recupera el cursor cuando lo pierde sin que nadie más lo haya pedido.
    /// <para>
    /// El lector wedge escribe como teclado: sin cursor en ese campo, lo escaneado cae fuera y el
    /// Enter final acciona lo que tenga el foco. El síntoma en mostrador no se parece a la causa
    /// —"la pantalla se pone opaca"— porque lo que se ve es el velo del panel de resultado, no un
    /// campo vacío. Y el cajero tenía que tocar el serial para que el siguiente escaneo cayera
    /// donde debía.
    /// </para>
    /// <para>
    /// Se espera un instante antes de reclamarlo: si el cajero tocó el campo del monto, el foco
    /// ya es de él y quitárselo sería peor que el problema que se está arreglando.
    /// </para>
    /// </summary>
    private void OnBarcodeUnfocused(object? sender, FocusEventArgs e) =>
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            // El monto es el único otro campo de la pantalla. Si lo tiene él, fue a propósito.
            if (AmountEntry.IsFocused || BarcodeEntry.IsFocused)
            {
                return;
            }

            // Con el panel de resultado abierto el foco no es de nadie de atrás: reclamarlo
            // dejaría el cursor bajo el velo, invisible, y el Enter del siguiente escaneo caería
            // en una pantalla que el cajero todavía no ha cerrado.
            if (_viewModel.HasResult || _viewModel.IsBusy)
            {
                return;
            }

            Common.ScannerFocus.Arm(BarcodeEntry);
        });

    /// <summary>
    /// Devuelve el cursor al campo del serial cuando termina una operación o cuando el campo se
    /// vacía al mandar el bono a la lista.
    /// <para>
    /// Cada escaneo termina en Enter. Si el foco quedó en un botón —agregar a la lista lo deja
    /// ahí—, el Enter del escaneo siguiente ACCIONA ese botón en vez de capturar el serial.
    /// </para>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RedeemTabViewModel.CoverageRatio))
        {
            AnimateCoverage();
        }

        var finishedWorking = e.PropertyName == nameof(RedeemTabViewModel.IsBusy) && !_viewModel.IsBusy;
        var fieldCleared = e.PropertyName == nameof(RedeemTabViewModel.ScannedBarcode)
            && string.IsNullOrEmpty(_viewModel.ScannedBarcode);

        // Al cerrar el panel de resultado el cursor no vuelve solo, y el cajero encadena
        // operaciones: la siguiente empieza con un escaneo que caería fuera del campo.
        var resultDismissed = e.PropertyName == nameof(RedeemTabViewModel.HasResult)
            && !_viewModel.HasResult;

        if (finishedWorking || fieldCleared || resultDismissed)
        {
            Dispatcher.Dispatch(() => Common.ScannerFocus.Arm(BarcodeEntry));
        }
    }

    /// <summary>
    /// Lleva la barra hasta la cobertura actual creciendo, no saltando.
    /// <para>
    /// El movimiento es el que comunica: un salto no dice si la barra subió o bajó, y con dos
    /// bonos seguidos el cajero no alcanza a ver que el segundo aportó. 320 ms con
    /// <c>CubicOut</c> —rápido al principio y frenando al final— se lee como algo que llegó a su
    /// sitio, no como una animación esperando a terminar.
    /// </para>
    /// </summary>
    private void AnimateCoverage()
    {
        var target = _viewModel.CoverageRatio;

        // Un cambio imperceptible no merece animación, y reanimar sobre lo mismo tiembla.
        if (Math.Abs(target - _paintedCoverage) < 0.001)
        {
            return;
        }

        _paintedCoverage = target;

        Dispatcher.Dispatch(() =>
        {
            // Sin await ni excepciones que observar: si la página se va a mitad, MAUI cancela la
            // animación por su cuenta. Envuelto igual, porque una animación NUNCA puede tumbar
            // un cobro.
            try
            {
                _ = CoverageBar.ProgressTo(target, 320, Easing.CubicOut);
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, $"[RedeemTabPage] Coverage animation skipped: {exception.Message}");
            }
        });
    }

    // Botón "Escanear": el lector wedge escribe como teclado, así que basta con dejar el
    // cursor en el campo del serial.
    private void OnScanTapped(object? sender, TappedEventArgs e) =>
        Common.ScannerFocus.Arm(BarcodeEntry);

    // Enter del lector (o del teclado) al terminar de capturar el serial. NO se limpia el
    // campo: el serial escaneado tiene que quedarse a la vista para que el cajero digite el
    // monto y lo agregue a la lista. Limpiarlo aquí hacía que el escaneo pareciera no
    // registrarse — el lector disparaba, el serial aparecía un instante y desaparecía.
    private void OnBarcodeCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Dispatch(() =>
        {
            if (BindingContext is RedeemTabViewModel vm)
            {
                _ = SafeHandleScanAsync(vm);
            }
        });
    }

    // El escaneo ahora consulta el saldo del bono para resolver el monto, así que es asíncrono.
    // El try/catch es obligatorio: sin él, una falla de red al consultar quedaría como excepción
    // no observada y tumbaría el proceso a mitad de un cobro.
    private static async Task SafeHandleScanAsync(RedeemTabViewModel viewModel)
    {
        try
        {
            await viewModel.HandleScannedBarcodeAsync();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[RedeemTabPage] Scan handling failed: {exception}");
        }
    }

    private async Task SafeEnsureSessionAsync()
    {
        try
        {
            await _viewModel.EnsureSessionAsync();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[RedeemTabPage] EnsureSession failed: {exception}");
        }
    }
}