using CommunityToolkit.Mvvm.ComponentModel;
using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Estado de un ítem en el carrito de redención. Compartido con cualquier otra cola
/// que tengamos en el POS (activación física/virtual, etc.).
/// </summary>
public enum QueueItemStatus
{
    Pending,
    Processing,
    Approved,
    PendingReview,
    Rejected
}

public sealed partial class PendingRedemptionItem : ObservableObject
{
    public PendingRedemptionItem(TestCard card, long amountPesos)
    {
        Card = card;
        AmountPesos = amountPesos;
    }

    public TestCard Card { get; }

    public long AmountPesos { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsApproved))]
    [NotifyPropertyChangedFor(nameof(IsRejected))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsRemovable))]
    private QueueItemStatus _status = QueueItemStatus.Pending;

    public bool IsPending => Status == QueueItemStatus.Pending;
    public bool IsProcessing => Status == QueueItemStatus.Processing;
    public bool IsApproved => Status == QueueItemStatus.Approved;
    public bool IsRejected => Status == QueueItemStatus.Rejected;

    public bool IsRemovable => Status is QueueItemStatus.Pending or QueueItemStatus.Rejected;

    public string StatusLabel => Status switch
    {
        QueueItemStatus.Pending => "En espera",
        QueueItemStatus.Processing => "Procesando…",
        QueueItemStatus.Approved => "Aprobado",
        QueueItemStatus.Rejected => "Rechazado",
        _ => string.Empty
    };

    public string Subtitle => Card.Number;
    public string AmountText => $"${AmountPesos:N0} COP";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultDetail))]
    private string _resultDetail = string.Empty;

    public bool HasResultDetail => !string.IsNullOrWhiteSpace(ResultDetail);
}