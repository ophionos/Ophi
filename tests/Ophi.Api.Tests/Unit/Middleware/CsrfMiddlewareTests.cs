using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Ophi.Api.Common.Middleware;

namespace Ophi.Api.Tests.Unit.Middleware;

public class CsrfMiddlewareTests
{
    private static CsrfMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new CsrfMiddleware(next);
    }

    private static DefaultHttpContext CreateContext(string method, string? xRequestedWith = null)
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = method
            },
            Response =
            {
                Body = new MemoryStream()
            }
        };
        if (xRequestedWith != null)
            context.Request.Headers["X-Requested-With"] = xRequestedWith;
        return context;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task InvokeAsync_SafeMethod_CallsNext(string method)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(method);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task InvokeAsync_MutatingMethodWithoutHeader_Returns403(string method)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(method);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task InvokeAsync_MutatingMethodWithHeader_CallsNext(string method)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(method, "XMLHttpRequest");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_PostWithoutHeader_WritesJsonErrorBody()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("POST");

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("ForbiddenRequest");
    }
}
