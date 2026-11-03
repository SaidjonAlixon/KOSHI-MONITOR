using KOSHI.Contracts;

namespace KOSHI.Application.Common;

/// <summary>Expected business error mapped by the API to an RFC 7807 ProblemDetails response.</summary>
public sealed class AppException : Exception
{
    public AppException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors;
    }

    public AppException() : this(500, ErrorCodes.InternalError, "Unexpected error") { }

    public AppException(string message) : this(400, ErrorCodes.ValidationFailed, message) { }

    public AppException(string message, Exception inner) : base(message, inner)
    {
        StatusCode = 500;
        Code = ErrorCodes.InternalError;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IDictionary<string, string[]>? Errors { get; }
}

public static class Errors
{
    public static AppException NotFound(string entity) =>
        new(404, ErrorCodes.NotFound, $"{entity} not found.");

    public static AppException Conflict(string message) =>
        new(409, ErrorCodes.Conflict, message);

    public static AppException Validation(string field, string message) =>
        new(400, ErrorCodes.ValidationFailed, message, new Dictionary<string, string[]> { [field] = [message] });

    public static AppException Validation(IDictionary<string, string[]> errors) =>
        new(400, ErrorCodes.ValidationFailed, "One or more validation errors occurred.", errors);

    public static AppException Forbidden(string message = "You do not have permission to perform this action.") =>
        new(403, ErrorCodes.Forbidden, message);

    public static AppException Unauthorized(string code, string message) =>
        new(401, code, message);

    public static AppException DeviceBlocked() =>
        new(403, ErrorCodes.DeviceBlocked, "Device is blocked by an administrator. Telemetry is not accepted.");

    public static AppException CredentialRevoked() =>
        new(401, ErrorCodes.CredentialRevoked, "Device credential has been revoked.");
}
