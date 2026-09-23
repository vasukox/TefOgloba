using CommunityToolkit.Mvvm.ComponentModel;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Maui.ViewModels.Pos;

public enum ActivationFormMode
{
    Physical,
    Virtual
}

public enum ActivationRowStatus
{
    Pending,
    Processing,
    Approved,
    PendingReview,
    Rejected
}

/// <summary>
/// Una fila del formulario de activación: editable mientras está en estado Pending, y
/// muestra el resultado de Ogloba después de procesarla. Reemplaza al antiguo
/// "PendingActivationItem" para que el cajero pueda ver Y editar varios bonos a la vez
/// (un formulario por bono, "+" para agregar otro) en vez de un solo form que empuja a
/// una cola ciega. La transición de "editable" a "resultado" se hace vía el
/// <see cref="Status"/>: mientras esté en Pending, los Entries son editables; al pasar
/// a Processing/Approved/Rejected, la fila se congela.
/// </summary>
public sealed partial class ActivationFormRow : ObservableObject
{
    public ActivationFormRow(ActivationFormMode mode)
    {
        Mode = mode;
    }

    public ActivationFormMode Mode { get; }

    public bool IsPhysical => Mode == ActivationFormMode.Physical;

    public bool IsVirtual => !IsPhysical;

    /// <summary>Etiqueta visible en el header de la fila.</summary>
    public string ModeLabel => Mode switch
    {
        ActivationFormMode.Physical => "Física",
        ActivationFormMode.Virtual => "Virtual",
        _ => string.Empty
    };

    /// <summary>Label del campo serial/correo según el modo (evita mostrar "SERIAL DEL BONO"
    /// cuando el cajero está activando un bono virtual y debe tipear un correo).</summary>
    public string CardFieldLabel => Mode switch
    {
        ActivationFormMode.Physical => "SERIAL DEL BONO",
        ActivationFormMode.Virtual => "CORREO DEL CLIENTE",
        _ => string.Empty
    };

    /// <summary>Placeholder que se muestra en el Entry según el modo.</summary>
    public string CardPlaceholder => Mode switch
    {
        ActivationFormMode.Physical => "Serial de la tarjeta (10-16 dígitos)",
        ActivationFormMode.Virtual => "Correo del cliente (Ogloba envía el bono)",
        _ => string.Empty
    };

    [ObservableProperty]
    private string _cardValue = string.Empty;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsApproved))]
    [NotifyPropertyChangedFor(nameof(IsPendingReview))]
    [NotifyPropertyChangedFor(nameof(IsRejected))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsRemovable))]
    private ActivationRowStatus _status = ActivationRowStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultDetail))]
    private string _resultDetail = string.Empty;

    public bool HasResultDetail => !string.IsNullOrWhiteSpace(ResultDetail);

    public bool IsPending => Status == ActivationRowStatus.Pending;
    public bool IsProcessing => Status == ActivationRowStatus.Processing;
    public bool IsApproved => Status == ActivationRowStatus.Approved;
    public bool IsPendingReview => Status == ActivationRowStatus.PendingReview;
    public bool IsRejected => Status == ActivationRowStatus.Rejected;

    /// <summary>Editable mientras está pendiente; congelado al pasar a Processing.</summary>
    public bool IsEditable => Status == ActivationRowStatus.Pending;

    public bool IsRemovable => Status is ActivationRowStatus.Pending or ActivationRowStatus.Rejected;

    public string StatusLabel => Status switch
    {
        ActivationRowStatus.Pending => "Listo para enviar",
        ActivationRowStatus.Processing => "Procesando…",
        ActivationRowStatus.Approved => "Aprobado",
        ActivationRowStatus.PendingReview => "En proceso",
        ActivationRowStatus.Rejected => "Rechazado",
        _ => string.Empty
    };

    public string Subtitle => IsPhysical ? CardValue : CardValue;

    public string AmountTextDisplay => $"${AmountText} COP";

    /// <summary>Valida la fila. Devuelve el monto en pesos (long) o un error.</summary>
    public Result<long> Validate()
    {
        if (string.IsNullOrWhiteSpace(CardValue))
        {
            return Result<long>.Failure(DomainError.Validation(
                "activation_row.card_required",
                IsPhysical ? "Falta el serial de la tarjeta" : "Falta el correo del cliente"));
        }

        if (IsPhysical)
        {
            var cardResult = CardIdentifier.CreatePhysicalCard(CardValue);
            if (cardResult.IsFailure)
            {
                return Result<long>.Failure(DomainError.Validation(
                    "activation_row.card_invalid",
                    "Serial de tarjeta inválido"));
            }
        }
        else
        {
            // Validación simple de email
            if (!CardValue.Contains('@') || !CardValue.Contains('.'))
            {
                return Result<long>.Failure(DomainError.Validation(
                    "activation_row.email_invalid",
                    "Correo inválido"));
            }
        }

        if (!Common.MoneyInput.TryParsePesos(AmountText, out var amount) || amount <= 0)
        {
            return Result<long>.Failure(DomainError.Validation(
                "activation_row.amount_invalid",
                "Monto inválido (debe ser > 0)"));
        }

        return Result<long>.Success(amount);
    }

    private string MaskedCardNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CardValue))
            {
                return string.Empty;
            }

            var result = CardIdentifier.CreatePhysicalCard(CardValue);
            return result.IsSuccess ? result.Value.MaskedValue : CardValue;
        }
    }
}