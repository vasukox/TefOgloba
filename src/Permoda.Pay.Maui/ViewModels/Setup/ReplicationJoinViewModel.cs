using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application.Features.Provisioning;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Setup;

/// <summary>
/// La caja NUEVA copiando la configuración de una que ya está montada.
/// <para>
/// El flujo tiene dos pasos y no uno: primero se trae la configuración y se MUESTRA de qué tienda
/// es y con qué nombre va a quedar la caja; solo después se guarda. En un centro comercial puede
/// haber otra KOAJ en la misma red, y guardar sin confirmar deja una caja operando contra la
/// tienda equivocada — un error que nadie nota hasta que la contabilidad no cuadra.
/// </para>
/// </summary>
public sealed partial class ReplicationJoinViewModel : ObservableObject
{
    private readonly PosSession _session;

    private TerminalConfigurationEnvelope? _received;

    public ReplicationJoinViewModel(PosSession session) => _session = session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetch))]
    private string _code = string.Empty;

    /// <summary>
    /// La dirección de la otra caja. Se deja vacía para que la busque sola; se escribe cuando la
    /// red no deja difundir, que es un caso real y no una rareza.
    /// </summary>
    [ObservableProperty]
    private string _hostAddress = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetch))]
    private bool _isBusy;

    public bool CanFetch => !IsBusy && Code.Trim().Length == PairingSecret.CodeLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>Lo que se trajo, a la espera de que el operador lo confirme.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _previewStore = string.Empty;

    public bool HasPreview => PreviewStore.Length > 0;

    [ObservableProperty]
    private string _previewCashiers = string.Empty;

    /// <summary>
    /// El nombre que va a tomar esta caja. Se PROPONE y queda editable: el criterio de numeración
    /// es de la tienda y puede no ser CAJA-NN.
    /// </summary>
    [ObservableProperty]
    private string _terminalId = string.Empty;

    [RelayCommand]
    private async Task FetchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Message = string.Empty;
        PreviewStore = string.Empty;
        _received = null;

        try
        {
            var host = HostAddress.Trim();

            if (host.Length == 0)
            {
                Message = "Buscando la otra caja…";
                var found = await PairingDiscovery.FindAsync(CancellationToken.None);

                if (found is null)
                {
                    Message =
                        "No se encontró ninguna caja repartiendo. Si la otra caja ya muestra el "
                        + "código, escribe abajo la dirección que aparece en su pantalla.";
                    return;
                }

                host = found.Value.Host;
                HostAddress = host;
            }

            var result = await PairingClient.FetchAsync(host, Code.Trim(), CancellationToken.None);

            if (!result.IsSuccess)
            {
                Message = Describe(result);
                return;
            }

            _received = result.Envelope;
            PreviewStore = result.Envelope!.StoreName.Length > 0
                ? $"{result.Envelope.StoreId} · {result.Envelope.StoreName}"
                : result.Envelope.StoreId;

            PreviewCashiers = result.Envelope.Cashiers.Count switch
            {
                0 => "Sin cajeros registrados",
                1 => "1 cajero",
                var n => $"{n} cajeros"
            };

            TerminalId = result.Envelope.SuggestNextTerminalId();
            Message = string.Empty;
        }
        catch (Exception exception)
        {
            Message = $"No se pudo copiar la configuración. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cada motivo se dice en los términos del que está instalando, no en los del protocolo. Un
    /// "unintelligible" en pantalla no le dice a nadie qué hacer a continuación.
    /// </summary>
    private static string Describe(PairingResult result) => result.Outcome switch
    {
        PairingOutcome.InvalidCode =>
            $"El código no es el que muestra la otra caja. Quedan {result.AttemptsRemaining} intentos.",
        PairingOutcome.NoAttemptsLeft =>
            "Se agotaron los intentos. En la otra caja, genera un código nuevo.",
        PairingOutcome.WindowClosed =>
            "La ventana de la otra caja se cerró. Genera un código nuevo allá.",
        PairingOutcome.Unreachable =>
            "No se pudo llegar a esa caja. Revisa la dirección y que siga mostrando el código.",
        _ =>
            "La otra caja respondió algo que esta no entiende. Suele ser que tienen versiones "
            + "distintas del módulo: actualiza las dos."
    };

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_received is null || IsBusy)
        {
            return;
        }

        var name = TerminalId.Trim();

        if (name.Length == 0)
        {
            Message = "Escribe el nombre de esta caja.";
            return;
        }

        // Se avisa ANTES de guardar. Descubrirlo después son dos cajas firmando igual sus
        // operaciones contra Ogloba, y nadie mirando.
        if (_received.IsTerminalIdTaken(name))
        {
            Message = $"{name} ya lo usa otra caja de la tienda. Elige otro nombre.";
            return;
        }

        IsBusy = true;

        try
        {
            var saved = await _session.ImportFromReplicationAsync(
                _received, name, CancellationToken.None);

            if (!saved)
            {
                Message = "No se pudo guardar la configuración recibida.";
                return;
            }

            await Shell.Current.GoToAsync(AppRoutes.CashierLogin);
        }
        catch (Exception exception)
        {
            Message = $"No se pudo guardar la configuración. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
