using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Common.Exceptions;

namespace Ophi.Api.Tests.Unit.Middleware;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenResponseNotStarted_WritesJsonError()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("boom"), NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_WhenResponseStarted_RethrowsOriginalException()
    {
        var context = StartedContext();
        var original = new InvalidOperationException("boom");
        var middleware = new ExceptionMiddleware(_ => throw original, NullLogger<ExceptionMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(context);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
    }

    [Fact]
    public async Task InvokeAsync_WhenCancelledAfterResponseStarted_Swallows()
    {
        // An SSE client disconnecting throws OperationCanceledException after the headers went out.
        var context = StartedContext();
        var middleware = new ExceptionMiddleware(
            _ => throw new OperationCanceledException(), NullLogger<ExceptionMiddleware>.Instance);

        var act = () => middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync();
    }

    private static DefaultHttpContext StartedContext()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        return context;
    }

    // Mirrors Kestrel: once the headers are sent, setting the status code throws.
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode
        {
            get => 200;
            set => throw new InvalidOperationException("StatusCode cannot be set because the response has already started.");
        }

        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
