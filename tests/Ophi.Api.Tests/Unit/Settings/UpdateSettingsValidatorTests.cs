using FluentValidation.TestHelper;
using Ophi.Api.Features.Settings;

namespace Ophi.Api.Tests.Unit.Settings;

public class UpdateSettingsValidatorTests
{
    private readonly UpdateSettings.Validator _validator = new();

    [Fact]
    public void Validate_WithValidInterval_Passes()
    {
        var command = new UpdateSettings.Command(null, 60) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithMinBound_Passes()
    {
        var command = new UpdateSettings.Command(null, 15) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithMaxBound_Passes()
    {
        var command = new UpdateSettings.Command(null, 1440) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithIntervalBelowMin_Fails()
    {
        var command = new UpdateSettings.Command(null, 14) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithIntervalAboveMax_Fails()
    {
        var command = new UpdateSettings.Command(null, 1441) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DefaultCheckIntervalMinutes);
    }

    [Fact]
    public void Validate_WithZeroInterval_Passes()
    {
        var command = new UpdateSettings.Command(null, 0) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithNullInterval_Passes()
    {
        var command = new UpdateSettings.Command(true, null) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithOnlyAffiliates_Passes()
    {
        var command = new UpdateSettings.Command(false, null) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithPageFetchDelayZero_Passes()
    {
        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: 0) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithPageFetchDelayMax_Passes()
    {
        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: 30) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithPageFetchDelayAboveMax_Fails()
    {
        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: 31) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PageFetchDelaySeconds);
    }

    [Fact]
    public void Validate_WithPageFetchDelayNegative_Fails()
    {
        var command = new UpdateSettings.Command(null, null, PageFetchDelaySeconds: -1) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PageFetchDelaySeconds);
    }

    [Fact]
    public void Validate_WithNullPageFetchDelay_Passes()
    {
        var command = new UpdateSettings.Command(null, null) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithScrapeCacheTtlZero_Passes()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 0) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithScrapeCacheTtlValid_Passes()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 30) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithScrapeCacheTtlMax_Passes()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 1440) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithScrapeCacheTtlAboveMax_Fails()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: 1441) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ScrapeCacheTtlMinutes);
    }

    [Fact]
    public void Validate_WithScrapeCacheTtlNegative_Fails()
    {
        var command = new UpdateSettings.Command(null, null, ScrapeCacheTtlMinutes: -1) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ScrapeCacheTtlMinutes);
    }

    [Fact]
    public void Validate_WithNullScrapeCacheTtl_Passes()
    {
        var command = new UpdateSettings.Command(null, null) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidDiscordWebhookUrl_Passes()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "https://discord.com/api/webhooks/123456/abcdef") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithDiscordAppWebhookUrl_Passes()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "https://discordapp.com/api/webhooks/123456/abcdef") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidDiscordWebhookUrl_Fails()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "https://evil.com/steal-data") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DiscordWebhookUrl);
    }

    [Fact]
    public void Validate_WithNonHttpsDiscordUrl_Fails()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "http://discord.com/api/webhooks/123/abc") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DiscordWebhookUrl);
    }

    [Fact]
    public void Validate_WithEmptyDiscordWebhookUrl_Passes()
    {
        var command = new UpdateSettings.Command(null, null, DiscordWebhookUrl: "") { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithNullDiscordWebhookUrl_Passes()
    {
        var command = new UpdateSettings.Command(null, null) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidAnomalyThreshold_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 50) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAnomalyThresholdMin_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 10) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAnomalyThresholdMax_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 500) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAnomalyThresholdBelowMin_Fails()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 9) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AnomalyThresholdPercent);
    }

    [Fact]
    public void Validate_WithAnomalyThresholdAboveMax_Fails()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 501) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AnomalyThresholdPercent);
    }

    [Fact]
    public void Validate_WithAnomalyThresholdZero_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AnomalyThresholdPercent: 0) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidAutoPauseAfterFailures_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AutoPauseAfterFailures: 5) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAutoPauseMin_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AutoPauseAfterFailures: 1) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAutoPauseMax_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AutoPauseAfterFailures: 50) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAutoPauseAboveMax_Fails()
    {
        var command = new UpdateSettings.Command(null, null, AutoPauseAfterFailures: 51) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AutoPauseAfterFailures);
    }

    [Fact]
    public void Validate_WithAutoPauseZero_Passes()
    {
        var command = new UpdateSettings.Command(null, null, AutoPauseAfterFailures: 0) { UserId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
