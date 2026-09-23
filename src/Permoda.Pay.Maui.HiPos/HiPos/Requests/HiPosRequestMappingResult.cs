namespace Permoda.Pay.Maui.HiPos.Requests;

public sealed record HiPosRequestMappingResult
{
    private HiPosRequestMappingResult(
        HiPosInitializationConfiguration? configuration,
        HiPosTransactionRequest? transaction,
        string? errorCode,
        string? errorMessage)
    {
        Configuration = configuration;
        Transaction = transaction;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public HiPosInitializationConfiguration? Configuration { get; }

    public HiPosTransactionRequest? Transaction { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccess => ErrorCode is null;

    public static HiPosRequestMappingResult Init(
        HiPosInitializationConfiguration configuration) =>
        new(configuration, null, null, null);

    public static HiPosRequestMappingResult ForTransaction(
        HiPosTransactionRequest request) =>
        new(null, request, null, null);

    public static HiPosRequestMappingResult Failed(string code, string message) =>
        new(null, null, code, message);
}
