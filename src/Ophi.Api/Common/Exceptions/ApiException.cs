namespace Ophi.Api.Common.Exceptions;

public class ApiException(string message, int statusCode = 400, string errorCode = "BadRequest") : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public class NotFoundException(string message) : ApiException(message, 404, "NotFound");

public class UnauthorizedException(string message = "Authentication required") : ApiException(message, 401, "Unauthorized");

public class ForbiddenException(string message = "Access denied") : ApiException(message, 403, "Forbidden");
