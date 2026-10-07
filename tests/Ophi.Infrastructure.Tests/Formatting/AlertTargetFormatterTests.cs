using FluentAssertions;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Formatting;

namespace Ophi.Infrastructure.Tests.Formatting;

/// <summary>
/// <c>Alert.TargetPrice</c> is polymorphic: an amount for Below/Above, a percentage for PercentDrop.
/// Every channel that renders it has to make that distinction, and the ones that forgot printed a
/// 20% drop target as "USD 20.00". This formatter is the single owner of the decision — see
/// docs/agent-notes.md § "one owner, because duplicates drift".
/// </summary>
public class AlertTargetFormatterTests
{
    [Theory]
    [InlineData(AlertCondition.Below)]
    [InlineData(AlertCondition.Above)]
    public void Describe_ForAmountConditions_RendersMoneyWithTheCurrency(AlertCondition condition)
    {
        AlertTargetFormatter.Describe(80m, condition, "USD").Should().Be("USD 80.00");
    }

    [Fact]
    public void Describe_ForPercentDrop_RendersAPercentageAndNeverACurrency()
    {
        // The bug this exists to prevent: a 20% target rendered as "USD 20.00", which reads as an
        // absurdly low price target rather than the percentage the user actually set.
        var result = AlertTargetFormatter.Describe(20m, AlertCondition.PercentDrop, "USD");

        result.Should().Be("20%");
        result.Should().NotContain("USD");
    }

    [Fact]
    public void Describe_ForPercentDrop_DropsFractionalDigits()
    {
        // Matches PriceFormatter.FormatPercent, which the in-app notification text already uses —
        // the two renderings of the same alert must not disagree. The inputs have to actually carry
        // fractional digits: an integer target passes whatever the formatter does with the rest.
        AlertTargetFormatter.Describe(20.4m, AlertCondition.PercentDrop, "EUR").Should().Be("20%");
        AlertTargetFormatter.Describe(20.6m, AlertCondition.PercentDrop, "EUR").Should().Be("21%");
    }

    [Fact]
    public void Describe_WithUnknownCondition_FallsBackToMoney()
    {
        // Null means the message predates the Condition field — only in-flight durable messages
        // across a deploy. Falling back to the previous rendering keeps that window unchanged
        // rather than inventing a percentage.
        AlertTargetFormatter.Describe(80m, null, "GBP").Should().Be("GBP 80.00");
    }

    [Fact]
    public void Describe_UsesInvariantFormatting()
    {
        // Emails and Discord payloads are not user-locale-aware; a comma decimal separator here
        // would render "USD 80,00" for anyone running the worker under a European culture.
        AlertTargetFormatter.Describe(1234.5m, AlertCondition.Below, "USD").Should().Be("USD 1234.50");
    }
}
