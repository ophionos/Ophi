namespace Ophi.Api.Common.Middleware;

public class CsrfMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!SafeMethods.Contains(context.Request.Method) &&
            !context.Request.Headers.ContainsKey("X-Requested-With"))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"ForbiddenRequest","message":"Missing required request header."}""");
            return;
        }

        await next(context);
    }
}
