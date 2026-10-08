using System.Text.Json;
using FluentValidation;

namespace Ophi.Api.Common.Exceptions;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await HandleExceptionAsync(context, ex);
        }
        catch (OperationCanceledException)
        {
            // The client went away mid-stream (an SSE disconnect, an aborted export). Nothing to send.
            logger.LogDebug("Request cancelled after the response started");
        }
        catch (Exception ex)
        {
            // The status line is already on the wire, so an error body would throw and hide this
            // exception. Log it here and rethrow so the server aborts the connection.
            logger.LogError(ex, "Unhandled exception after the response started on {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            throw;
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            logger.LogDebug("Request cancelled");
            context.Response.StatusCode = 499;
            return;
        }

        context.Response.ContentType = "application/json";

        ErrorResponse response;
        switch (exception)
        {
            case ValidationException validationEx:
                response = new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = "One or more validation errors occurred",
                    Details = validationEx.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
                };
                break;
            case ApiException apiEx:
                response = new ErrorResponse
                {
                    Error = apiEx.ErrorCode,
                    Message = apiEx.Message
                };
                break;
            default:
            {
                var traceId = Guid.NewGuid().ToString();
                logger.LogError(exception, "Unhandled exception on {Method} {Path} [TraceId={TraceId}]",
                    context.Request.Method, context.Request.Path.Value, traceId);
                response = new ErrorResponse
                {
                    Error = "InternalError",
                    Message = "An unexpected error occurred",
                    TraceId = traceId
                };
                break;
            }
        }

        context.Response.StatusCode = exception switch
        {
            ValidationException => 400,
            ApiException apiEx => apiEx.StatusCode,
            _ => 500
        };

        var json = JsonSerializer.Serialize(response, SerializerOptions);

        await context.Response.WriteAsync(json);
    }
}

public class ErrorResponse
{
    public string Error { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public object? Details { get; init; }
    public string? TraceId { get; init; }
}
