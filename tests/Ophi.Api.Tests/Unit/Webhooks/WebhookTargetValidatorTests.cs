using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;
using Ophi.Api.Features.Webhooks;
using Ophi.Infrastructure.Net;

namespace Ophi.Api.Tests.Unit.Webhooks;

public class WebhookTargetValidatorTests
{
    private static readonly WebhookAddressPolicy NoAllowlist = WebhookAddressPolicy.FromConfiguration(null);
    private static readonly WebhookAddressPolicy LanAllowed = WebhookAddressPolicy.FromConfiguration("192.168.1.0/24");

    private static CreateWebhookTarget.Command Create(string url) =>
        new("Gotify", url, ["alert_fired"], true) { UserId = Guid.NewGuid() };

    private static UpdateWebhookTarget.Command Update(string url) =>
        new(Guid.NewGuid(), "Gotify", url, ["alert_fired"], true) { UserId = Guid.NewGuid() };

    [Theory]
    [InlineData("http://192.168.1.10/message")]
    [InlineData("http://gotify.local/message")]
    public void Validate_PrivateDestinationWithoutAllowlist_Fails(string url)
    {
        new CreateWebhookTarget.Validator(NoAllowlist).TestValidate(Create(url))
            .ShouldHaveValidationErrorFor(x => x.Url);
        new UpdateWebhookTarget.Validator(NoAllowlist).TestValidate(Update(url))
            .ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("http://192.168.1.10/message")]
    [InlineData("http://[::ffff:192.168.1.10]/message")]
    [InlineData("http://gotify.local/message")] // checked on the resolved address at connect time
    public void Validate_DestinationAllowedByAllowlist_Passes(string url)
    {
        new CreateWebhookTarget.Validator(LanAllowed).TestValidate(Create(url))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateWebhookTarget.Validator(LanAllowed).TestValidate(Update(url))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("http://192.168.2.10/message")]
    [InlineData("http://127.0.0.1/message")]
    [InlineData("http://localhost/message")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public void Validate_DestinationOutsideAllowlist_Fails(string url)
    {
        new CreateWebhookTarget.Validator(LanAllowed).TestValidate(Create(url))
            .ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void ProductUrlRule_IgnoresTheWebhookAllowlist()
    {
        // The allowlist is for webhooks only; a product URL on the same network stays refused.
        var command = new AddProduct.Command("http://192.168.1.10/item") { UserId = Guid.NewGuid() };

        new AddProduct.Validator().TestValidate(command).ShouldHaveValidationErrorFor(x => x.Url);
    }
}
