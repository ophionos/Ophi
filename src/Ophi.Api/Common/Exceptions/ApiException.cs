namespace Ophi.Api.Common.Exceptions;

public class ApiException(string message, int statusCode = 400, string errorCode = "BadRequest") : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// A 409 for a URL the user already tracks. Carries the product that holds it, so a client can link
/// to it. <see cref="ProductId"/> is null when the collision surfaced only as a unique-index race.
/// </summary>
public class ConflictException(string message, Guid? productId = null, Guid? productUrlId = null)
    : ApiException(message, 409, "Conflict")
{
    public Guid? ProductId { get; } = productId;
    public Guid? ProductUrlId { get; } = productUrlId;
}

public class NotFoundException(string message) : ApiException(message, 404, "NotFound");

public class UnauthorizedException(string message = "Authentication required") : ApiException(message, 401, "Unauthorized");

public class ForbiddenException(string message = "Access denied") : ApiException(message, 403, "Forbidden");
