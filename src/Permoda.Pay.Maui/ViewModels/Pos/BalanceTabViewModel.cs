using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Pestaña "Consultar saldo": input dinámico del serial del bono, botón "Consultar",
/// muestra saldo, estado y fecha de expiración. NO inicia ninguna venta ni redención —
/// solo lee de Ogloba con POST /balance. Es la vía rápida para que el cajero confirme
/// el saldo antes de pasarlo por el flujo de Redimir.
/// </summary>
public sealed partial class BalanceTabViewModel : PosViewModelBase
{
    private readonly PosSession _session;

    public BalanceTabViewModel(
        PosSession session,
        PosHeaderViewModel header)
    {
        _session = session;
        Header = header;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanCheck));
            }
        };
    }

    public PosHeaderViewModel Header { get; }

    [ObservableProperty]
    private string _cardNumber = string.Empty;

    [ObservableProperty]
    private CardBalance? _lastBalance;

    [ObservableProperty]
    private string _lastError = string.Empty;

    public bool HasResult => LastBalance is not null || !string.IsNullOrWhiteSpace(LastError);

    public bool CanCheck => !IsBusy && !string.IsNullOrWhiteSpace(CardNumber);

    public string ResultHeadline => LastBalance is not null ? "Saldo disponible" : "No se pudo consultar";

    public string ResultDetail => LastBalance is not null
        ? $"Estado: {LastBalance.Status}   Vence: {LastBalance.ExpireDate ?? "—"}"
        : LastError;

    /// <summary>true cuando la consulta trajo saldo; false cuando trajo un error.</summary>
    public bool IsSuccessfulLookup => LastBalance is not null;

    /// <summary>Sello del estado que devolvió Ogloba, en mayúsculas para leerse de un vistazo.</summary>
    public string StatusChipText => LastBalance is null
        ? "SIN RESPUESTA"
        : (LastBalance.IsActive ? "ACTIVO" : LastBalance.Status.ToUpperInvariant());

    /// <summary>Verde solo si el bono está realmente utilizable; cualquier otro estado va en rojo.</summary>
    public bool IsCardUsable => LastBalance?.IsActive == true;

    public string ExpiryText => LastBalance?.ExpireDate is { Length: > 0 } expiry
        ? $"Vence {expiry}"
        : "Sin fecha de vencimiento";

    /// <summary>El serial consultado, para que el cajero confirme que leyó el bono correcto.</summary>
    public string LookedUpCardText => (CardNumber ?? string.Empty).Trim();

    public string ResultAmountText
    {
        get
        {
            if (LastBalance is null)
            {
                return string.Empty;
            }

            return FormatPesos(LastBalance.BalanceMinorUnits);
        }
    }

    /// <summary>
    /// Deja la pantalla como recién abierta: sin serial y sin el resultado de la consulta
    /// anterior.
    /// <para>
    /// El ViewModel es singleton y sobrevive a salir de la pantalla, así que al volver el cajero
    /// se encontraba el bono del cliente anterior todavía puesto —con su saldo a la vista—, y
    /// tenía que borrarlo antes de escanear el siguiente. Una consulta terminada no es estado que
    /// deba esperarlo.
    /// </para>
    /// </summary>
    public void ResetForNewLookup()
    {
        CardNumber = string.Empty;
        LastBalance = null;
        LastError = string.Empty;
    }

    partial void OnCardNumberChanged(string value)
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(LookedUpCardText));
    }

    partial void OnLastBalanceChanged(CardBalance? value)
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultHeadline));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(ResultAmountText));
        OnPropertyChanged(nameof(IsSuccessfulLookup));
        OnPropertyChanged(nameof(StatusChipText));
        OnPropertyChanged(nameof(IsCardUsable));
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(LookedUpCardText));
    }

    partial void OnLastErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultHeadline));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(IsSuccessfulLookup));
        OnPropertyChanged(nameof(StatusChipText));
        OnPropertyChanged(nameof(IsCardUsable));
    }

    public async Task EnsureSessionAsync()
    {

        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();
    }

    [RelayCommand]
    private async Task CheckBalanceAsync()
    {

        LastBalance = null;
        LastError = string.Empty;

        await _session.EnsureLoadedAsync(CancellationToken.None);

        if (!_session.IsConfigured)
        {
            LastError = "La terminal no está configurada. Ve al apartado Config.";
            return;
        }

        if (!_session.HasActiveCashier)
        {
            LastError = "No hay cajero en turno. Inicia turno primero.";
            return;
        }

        var raw = (CardNumber ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            LastError = "Digita o escanea un serial del bono.";
            return;
        }

        var looksPhysical = raw.All(char.IsDigit) && raw.Length is >= 10 and <= 16;
        var cardResult = looksPhysical
            ? CardIdentifier.CreatePhysicalCard(raw)
            : CardIdentifier.CreateDigitalGencode(raw);

        if (cardResult.IsFailure)
        {
            LastError = $"'{raw}' no es un serial de bono válido.";
            return;
        }

        SetBusy("Consultando saldo…");

        var result = await _session.GetBalanceAsync(
            cardResult.Value,
            cancellationToken: CancellationToken.None);

        SetIdle("Listo.");

        if (result.IsFailure)
        {
            // Mismo traductor que en Activar y Redimir: el cajero lee el motivo, no el código.
            LastError = Common.OperationMessages
                .ForFailure(result.Failure.Code, result.Failure.Description).Text;
            return;
        }

        LastBalance = result.Value;
    }
}
