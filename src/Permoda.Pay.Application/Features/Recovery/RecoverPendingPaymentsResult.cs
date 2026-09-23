using Permoda.Pay.Application.Errors;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Features.Recovery;

public sealed record PaymentRecoveryFailure(
    string TransactionNumber,
    PaymentStatus Status,
    string Code,
    string Description);

public sealed record RecoverPendingPaymentsResult
{
    private RecoverPendingPaymentsResult(
        int total,
        int recovered,
        IReadOnlyCollection<PaymentRecoveryFailure> failures,
        ApplicationError error)
    {
        Total = total;
        Recovered = recovered;
        Failures = failures;
        Error = error;
    }

    public int Total { get; }

    public int Recovered { get; }

    public int Remaining => Failures.Count;

    public IReadOnlyCollection<PaymentRecoveryFailure> Failures { get; }

    public ApplicationError Error { get; }

    public bool IsSuccess => Error == ApplicationError.None;

    public static RecoverPendingPaymentsResult Completed(
        int total,
        int recovered,
        IReadOnlyCollection<PaymentRecoveryFailure> failures) =>
        new(total, recovered, failures, ApplicationError.None);

    public static RecoverPendingPaymentsResult LoadFailed(ApplicationError error) =>
        new(0, 0, [], error);
}
