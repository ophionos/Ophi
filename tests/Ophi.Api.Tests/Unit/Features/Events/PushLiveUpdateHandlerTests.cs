using System.Text.Json;
using FluentAssertions;
using Moq;
using Ophi.Api.Common.Events;
using Ophi.Api.Features.Events;
using Ophi.Domain.Messages.Events;

namespace Ophi.Api.Tests.Unit.Features.Events;

public class PushLiveUpdateHandlerTests
{
    [Fact]
    public async Task HandleAsync_FansOutToTheMessageUser()
    {
        var registry = new Mock<ISseConnectionRegistry>();
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await PushLiveUpdateHandler.HandleAsync(
            new LiveUpdate(userId, LiveUpdate.ScrapeCompleted, productId), registry.Object);

        registry.Verify(r => r.PublishAsync(userId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SerializesThinCamelCaseFrame()
    {
        var registry = new Mock<ISseConnectionRegistry>();
        string? captured = null;
        registry.Setup(r => r.PublishAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((_, payload) => captured = payload)
            .Returns(ValueTask.CompletedTask);
        var productId = Guid.NewGuid();

        await PushLiveUpdateHandler.HandleAsync(
            new LiveUpdate(Guid.NewGuid(), LiveUpdate.ScrapeCompleted, productId), registry.Object);

        captured.Should().NotBeNull();
        using var doc = JsonDocument.Parse(captured!);
        doc.RootElement.GetProperty("type").GetString().Should().Be("scrape-completed");
        doc.RootElement.GetProperty("productId").GetGuid().Should().Be(productId);
    }

    [Fact]
    public async Task HandleAsync_NotificationKind_OmitsProductIdWhenNull()
    {
        var registry = new Mock<ISseConnectionRegistry>();
        string? captured = null;
        registry.Setup(r => r.PublishAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((_, payload) => captured = payload)
            .Returns(ValueTask.CompletedTask);

        await PushLiveUpdateHandler.HandleAsync(
            new LiveUpdate(Guid.NewGuid(), LiveUpdate.Notification), registry.Object);

        using var doc = JsonDocument.Parse(captured!);
        doc.RootElement.GetProperty("type").GetString().Should().Be("notification");
        doc.RootElement.GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
