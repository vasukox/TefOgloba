namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Una línea del resumen que ve el cajero al terminar un lote: qué bono, a quién y en qué quedó.
/// <para>
/// Existe porque el resultado de un lote es la información que el cajero necesita repetirle al
/// cliente en voz alta —"tu bono salió a este correo", "este quedó rechazado"— y eso no cabe en
/// un aviso que se desvanece. Es un registro inmutable de lo que pasó, no estado editable.
/// </para>
/// </summary>
/// <param name="Target">Destino: el correo del cliente, o el serial de la tarjeta física.</param>
/// <param name="Detail">Referencia de Ogloba y monto, o el motivo del rechazo en lenguaje claro.</param>
/// <param name="Technical">
/// Código y mensaje original de Ogloba, para soporte. Se dibuja en letra chica y gris debajo del
/// motivo: el cajero no lo necesita para operar, pero sin él un caso raro es irrastreable.
/// </param>
public sealed record OperationResultLine(
    string Target,
    string Detail,
    OperationResultKind Kind,
    string? Technical = null)
{
    public bool HasTechnical => !string.IsNullOrWhiteSpace(Technical);

    public bool IsApproved => Kind == OperationResultKind.Approved;

    public bool IsPending => Kind == OperationResultKind.Pending;

    public bool IsRejected => Kind == OperationResultKind.Rejected;

    /// <summary>Etiqueta corta del estado, para leerse de un vistazo a un metro de distancia.</summary>
    public string StatusLabel => Kind switch
    {
        OperationResultKind.Approved => "LISTO",
        OperationResultKind.Pending => "EN PROCESO",
        OperationResultKind.Rejected => "RECHAZADO",
        _ => string.Empty
    };
}

public enum OperationResultKind
{
    Approved,
    Pending,
    Rejected
}
