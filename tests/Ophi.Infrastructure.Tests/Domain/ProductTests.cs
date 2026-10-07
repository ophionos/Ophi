using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void MarkActive_FlipsStatusFromPendingToActive()
    {
        var product = new Product { Status = ProductStatus.Pending };

        product.MarkActive();

        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void MarkActive_IsIdempotent()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.MarkActive();

        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void MarkAsError_FlipsToError()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.MarkAsError();

        product.Status.Should().Be(ProductStatus.Error);
    }

    [Fact]
    public void Pause_FlipsToPaused()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.Pause();

        product.Status.Should().Be(ProductStatus.Paused);
    }

    [Fact]
    public void Resume_FlipsBackToActive()
    {
        var product = new Product { Status = ProductStatus.Paused };

        product.Resume();

        product.Status.Should().Be(ProductStatus.Active);
    }
}
