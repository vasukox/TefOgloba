using System.Net;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Infrastructure.Ogloba.Contracts;

namespace Permoda.Pay.Infrastructure.Ogloba.Errors;

internal static class OglobaFailureMapper
{
    public static PortFailure FromHttpStatus(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new PortFailure(
                "ogloba.authentication_failed",
                "Ogloba rejected the store credentials.",
                PortFailureType.Authentication);
        }

        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
        {
            return Timeout();
        }

        if ((int)statusCode >= 500)
        {
            return Indeterminate(
                "ogloba.server_error",
                $"Ogloba returned HTTP {(int)statusCode}.");
        }

        return new PortFailure(
            "ogloba.http_error",
            $"Ogloba returned HTTP {(int)statusCode}.",
            PortFailureType.Technical);
    }

    /// <summary>
    /// La operación no está publicada en la pasarela del ambiente activo.
    /// <para>
    /// No es un rechazo de Ogloba ni un fallo de red: es que ese camino no existe todavía. Se
    /// devuelve como fallo normal —y no como excepción— porque ocurre en el hilo de un cobro:
    /// una excepción acá tumbaría la operación con el cliente enfrente, en vez de mostrarle al
    /// cajero un motivo y dejarlo seguir por otro medio.
    /// </para>
    /// </summary>
    public static PortFailure OperationNotPublished(string operation) =>
        new(
            "ogloba.operation_not_published",
            $"La operación '{operation}' no está disponible en este ambiente.",
            PortFailureType.Technical);

    public static PortFailure FromRejectedResponse(OglobaResponse response) =>
        FromRejectedResponse(response.GetErrorCode(), response.ErrorMessage);

    // /orderCreation y /orderConfirm devuelven el mismo par (errorCode/errorMessage) en
    // tipos de respuesta distintos a OglobaResponse — se comparte la lógica de mapeo por valores.
    public static PortFailure FromRejectedResponse(string? errorCode, string? errorMessage) =>
        new(
            errorCode ?? "ogloba.rejected",
            string.IsNullOrWhiteSpace(errorMessage)
                ? "Ogloba rejected the operation."
                : errorMessage,
            PortFailureType.Rejected);

    public static PortFailure Timeout() =>
        new(
            "ogloba.timeout",
            "Ogloba did not respond before the configured timeout.",
            PortFailureType.Timeout);

    public static PortFailure Network() =>
        Indeterminate(
            "ogloba.network_error",
            "The Ogloba response could not be determined due to a network failure.");

    public static PortFailure MalformedResponse() =>
        Indeterminate(
            "ogloba.malformed_response",
            "Ogloba returned an invalid or incomplete response.");

    private static PortFailure Indeterminate(string code, string description) =>
        new(code, description, PortFailureType.Indeterminate);
}
