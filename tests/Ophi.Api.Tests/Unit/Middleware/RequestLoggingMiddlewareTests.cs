using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Common.Middleware;

namespace Ophi.Api.Tests.Unit.Middleware;

public class RequestLoggingMiddlewareTests
{
    private static DefaultHttpContext CreateContext(string method = "GET", string path = "/api/v1/test", int? userId = null)
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = method,
                Path = path
            },
            Response =
            {
                Body = new MemoryStream()
            }
        };

        if (userId.HasValue)
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return context;
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        var nextCalled = false;
        var middleware = new RequestLoggingMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<RequestLoggingMiddleware>.Instance);

        await middleware.InvokeAsync(CreateContext());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_DoesNotModifyResponseStatusCode()
    {
        var middleware = new RequestLoggingMiddleware(
            ctx => { ctx.Response.StatusCode = 201; return Task.CompletedTask; },
            NullLogger<RequestLoggingMiddleware>.Instance);
        var context = CreateContext("POST");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task InvokeAsync_LogsEvenWhenNextThrows()
    {
        // Arrange — next throws but middleware must not swallow the exception
        var middleware = new RequestLoggingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<RequestLoggingMiddleware>.Instance);
        var context = CreateContext();

        // Act & Assert — exception propagates, but the finally block ran (no secondary exception)
        var act = () => middleware.InvokeAsync(context);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task InvokeAsync_HandlesAnonymousUser()
    {
        var middleware = new RequestLoggingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestLoggingMiddleware>.Instance);
        // No user set on context — should not throw
        var context = CreateContext();

        var act = () => middleware.InvokeAsync(context);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_HandlesAuthenticatedUser()
    {
        var middleware = new RequestLoggingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestLoggingMiddleware>.Instance);
        var context = CreateContext(userId: 42);

        var act = () => middleware.InvokeAsync(context);
        await act.Should().NotThrowAsync();
    }
}
