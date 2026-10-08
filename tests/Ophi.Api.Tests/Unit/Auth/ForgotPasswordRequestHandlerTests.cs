using FluentAssertions;
using Moq;
using Ophi.Api.Features.Auth;
using Wolverine;

namespace Ophi.Api.Tests.Unit.Auth;

public class ForgotPasswordRequestHandlerTests
{
    private readonly Mock<IMessageBus> _busMock = new();

    [Theory]
    [InlineData("known@example.com")]
    [InlineData("nobody@example.com")]
    public async Task Handle_AnyEmail_PublishesSendResetEmailWithoutLookingUpTheAccount(string email)
    {
        // The request path does the same work for every email (no DB read, no token write, no send),
        // so its response time cannot reveal whether an account exists.
        var handler = new ForgotPassword.Handler(_busMock.Object);

        var response = await handler.Handle(new ForgotPassword.Command(email));

        response.Should().NotBeNull();
        _busMock.Verify(
            x => x.PublishAsync(new ForgotPassword.SendResetEmail(email), It.IsAny<DeliveryOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UppercaseEmail_PublishesLowercase()
    {
        var handler = new ForgotPassword.Handler(_busMock.Object);

        await handler.Handle(new ForgotPassword.Command("User@Example.COM"));

        _busMock.Verify(
            x => x.PublishAsync(new ForgotPassword.SendResetEmail("user@example.com"), It.IsAny<DeliveryOptions?>()),
            Times.Once);
    }
}
