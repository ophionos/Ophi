using System.Text;
using FluentAssertions;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Products;

public class ImportProductsCsvParserTests
{
    private static Stream ToStream(string csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public async Task ParseCsvAsync_SimpleRow_ReturnsFields()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url,name,target_price\nhttps://amazon.com/dp/A,Widget,9.99"),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Url.Should().Be("https://amazon.com/dp/A");
        rows[0].Name.Should().Be("Widget");
        rows[0].TargetPrice.Should().Be(9.99m);
        rows[0].LineNumber.Should().Be(2);
    }

    [Fact]
    public async Task ParseCsvAsync_QuotedFieldWithComma_PreservesComma()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url,name\nhttps://example.com/x,\"Hello, World\""),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Name.Should().Be("Hello, World");
    }

    [Fact]
    public async Task ParseCsvAsync_QuotedFieldWithEscapedQuote_Preserves()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url,name\nhttps://example.com/y,\"say \"\"hi\"\"\""),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Name.Should().Be("say \"hi\"");
    }

    [Fact]
    public async Task ParseCsvAsync_MissingOptionalColumns_ReturnsNulls()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url\nhttps://example.com/z"),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Name.Should().BeNull();
        rows[0].TargetPrice.Should().BeNull();
        rows[0].Tags.Should().BeNull();
    }

    [Fact]
    public async Task ParseCsvAsync_HeaderIsCaseInsensitive()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("URL,Name,Target_Price\nhttps://example.com/a,Widget,5.00"),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Url.Should().Be("https://example.com/a");
        rows[0].Name.Should().Be("Widget");
        rows[0].TargetPrice.Should().Be(5.00m);
    }

    [Fact]
    public async Task ParseCsvAsync_EmptyStream_Throws()
    {
        var act = async () => await ImportProducts.ParseCsvAsync(ToStream(""), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseCsvAsync_HeaderWithoutUrlColumn_Throws()
    {
        var act = async () => await ImportProducts.ParseCsvAsync(
            ToStream("name,price\nWidget,10"),
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseCsvAsync_InvalidTargetPrice_ReturnsNull()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url,target_price\nhttps://example.com/b,not-a-number"),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].TargetPrice.Should().BeNull();
    }

    [Fact]
    public async Task ParseCsvAsync_MultipleRows_AllReturned()
    {
        var csv = """
            url,name
            https://example.com/1,Item One
            https://example.com/2,Item Two
            https://example.com/3,Item Three
            """;
        var rows = await ImportProducts.ParseCsvAsync(ToStream(csv), CancellationToken.None);

        rows.Should().HaveCount(3);
        rows.Select(r => r.Name).Should().Equal("Item One", "Item Two", "Item Three");
        rows.Select(r => r.LineNumber).Should().Equal(2, 3, 4);
    }

    [Fact]
    public async Task ParseCsvAsync_TagsField_Preserved()
    {
        var rows = await ImportProducts.ParseCsvAsync(
            ToStream("url,tags\nhttps://example.com/c,\"electronics, books\""),
            CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Tags.Should().Be("electronics, books");
    }
}
