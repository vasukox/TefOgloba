namespace Permoda.Pay.Domain.Payments;

// KOAJ no maneja cancelaciones de bonos (comercial ni técnica). El único "rollback" soportado
// es el reversal técnico por timeout/falla (Created → ReversalPending → Reversed). Una vez
// confirmada la transacción, no se ofrece /void — eso queda fuera del alcance del producto.
public enum PaymentStatus
{
    Created = 1,
    Requested = 2,
    ConfirmationPending = 3,
    Confirmed = 4,
    ReversalPending = 5,
    Reversed = 6,
    RequestRejected = 7,
    Step2Failed = 8
}
