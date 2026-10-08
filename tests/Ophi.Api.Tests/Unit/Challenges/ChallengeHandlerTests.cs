using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Challenges;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Challenges;

public class ChallengeHandlerTests : IDisposable
{
    private const string Blocked = "Blocked by anti-bot protection";
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IChallengeSessionManager> _sessions = new();
    private readonly Mock<IChallengeSession> _session = new();
    private readonly Mock<IStoreClearanceStore> _clearances = new();
    private readonly Guid _userId = Guid.NewGuid();

    public ChallengeHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContext.Users.Add(new User { Id = _userId, Email = "test@example.com", Name = "Test", PasswordHash = "hash" });
        _dbContext.SaveChanges();

        _sessions.Setup(x => x.IsEnabled).Returns(true);
        _session.Setup(x => x.Host).Returns("shop.example");
        _session.Setup(x => x.UserAgent).Returns("Session UA");
        _sessions.Setup(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session.Object);
    }

    private StartChallenge.Handler StartHandler() =>
        new(_dbContext, _sessions.Object, NullLogger<StartChallenge.Handler>.Instance);

    private GetChallenge.Handler GetHandler() =>
        new(_dbContext, _sessions.Object, _clearances.Object, NullLogger<GetChallenge.Handler>.Instance);

    private ProductUrl AddUrl(string url, string? lastError, Guid? ownerId = null)
    {
        var owner = ownerId ?? _userId;
        if (ownerId != null && !_dbContext.Users.Any(u => u.Id == owner))
            _dbContext.Users.Add(new User { Id = owner, Email = $"{owner}@example.com", Name = "Other", PasswordHash = "hash" });
        var product = new Product { Id = Guid.NewGuid(), UserId = owner, Name = "P", Currency = "USD", Status = ProductStatus.Active };
        var productUrl = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = url, Currency = "USD", LastError = lastError };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.SaveChanges();
        return productUrl;
    }

    [Fact]
    public async Task Start_BlockedUrlOfTheUser_OpensASessionOnTheUrl()
    {
        var url = AddUrl("https://shop.example/p/1", Blocked);

        var response = await StartHandler().Handle(new StartChallenge.Command(url.ProductId, url.Id, _userId),
            TestContext.Current.CancellationToken);

        response.Host.Should().Be("shop.example");
        _sessions.Verify(x => x.StartAsync(_userId, "https://shop.example/p/1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_UrlOfAnotherUser_ThrowsNotFound()
    {
        var url = AddUrl("https://shop.example/p/1", Blocked, ownerId: Guid.NewGuid());

        var act = () => StartHandler().Handle(new StartChallenge.Command(url.ProductId, url.Id, _userId),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        _sessions.Verify(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Page not found (HTTP 404)")]
    [InlineData("Access blocked (HTTP 403)")]
    public async Task Start_UrlNotBlocked_ThrowsBadRequest(string? lastError)
    {
        var url = AddUrl("https://shop.example/p/1", lastError);

        var act = () => StartHandler().Handle(new StartChallenge.Command(url.ProductId, url.Id, _userId),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Start_FeatureOff_ThrowsBadRequestBeforeOpeningAnything()
    {
        _sessions.Setup(x => x.IsEnabled).Returns(false);
        var url = AddUrl("https://shop.example/p/1", Blocked);

        var act = () => StartHandler().Handle(new StartChallenge.Command(url.ProductId, url.Id, _userId),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.ErrorCode.Should().Be("ChallengeUnavailable");
    }

    [Fact]
    public async Task Start_AtTheSessionLimit_ThrowsTooManyRequests()
    {
        _sessions.Setup(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChallengeLimitException());
        var url = AddUrl("https://shop.example/p/1", Blocked);

        var act = () => StartHandler().Handle(new StartChallenge.Command(url.ProductId, url.Id, _userId),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Get_NoSession_ReturnsNone()
    {
        var result = await GetHandler().Handle(new GetChallenge.Query(_userId), TestContext.Current.CancellationToken);

        result.Response.State.Should().Be("none");
        result.Retry.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ChallengeNotCleared_ReturnsTheFrame()
    {
        _sessions.Setup(x => x.Find(_userId)).Returns(_session.Object);
        _session.Setup(x => x.IsClearedAsync()).ReturnsAsync(false);
        _session.Setup(x => x.ScreenshotAsync()).ReturnsAsync([1, 2, 3]);

        var result = await GetHandler().Handle(new GetChallenge.Query(_userId), TestContext.Current.CancellationToken);

        result.Response.State.Should().Be("active");
        result.Response.Image.Should().Be(Convert.ToBase64String([1, 2, 3]));
        _clearances.Verify(x => x.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_ChallengeCleared_SavesTheClearanceClosesAndRetriesTheBlockedUrlsOnTheHost()
    {
        _sessions.Setup(x => x.Find(_userId)).Returns(_session.Object);
        _session.Setup(x => x.IsClearedAsync()).ReturnsAsync(true);
        _session.Setup(x => x.StorageStateAsync()).ReturnsAsync("{\"cookies\":[]}");
        var first = AddUrl("https://shop.example/p/1", Blocked);
        var second = AddUrl("https://SHOP.example/p/2", Blocked);
        AddUrl("https://shop.example/p/3", null);
        AddUrl("https://shop.example/p/6", "Access blocked (HTTP 403)");
        AddUrl("https://other.example/p/4", Blocked);
        AddUrl("https://shop.example/p/5", Blocked, ownerId: Guid.NewGuid());

        var result = await GetHandler().Handle(new GetChallenge.Query(_userId), TestContext.Current.CancellationToken);

        result.Response.State.Should().Be("solved");
        result.Response.Image.Should().BeNull();
        _clearances.Verify(x => x.SaveAsync(_userId, "shop.example", "{\"cookies\":[]}", "Session UA", It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(x => x.CloseAsync(_userId), Times.Once);
        result.Retry.Should().BeEquivalentTo([(first.ProductId, first.Id), (second.ProductId, second.Id)]);
    }

    [Fact]
    public async Task Get_PageNavigatingDuringTheScreenshot_StaysActiveWithoutAFrame()
    {
        _sessions.Setup(x => x.Find(_userId)).Returns(_session.Object);
        _session.Setup(x => x.IsClearedAsync()).ReturnsAsync(false);
        _session.Setup(x => x.ScreenshotAsync()).ThrowsAsync(new PlaywrightException("Target page navigated"));

        var result = await GetHandler().Handle(new GetChallenge.Query(_userId), TestContext.Current.CancellationToken);

        result.Response.State.Should().Be("active");
        result.Response.Image.Should().BeNull();
        _sessions.Verify(x => x.CloseAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Input_NoSession_ThrowsNotFound()
    {
        var act = () => new SendChallengeInput.Handler(_sessions.Object)
            .Handle(new SendChallengeInput.Command("key", null, null, null, "Enter") { UserId = _userId }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("down", true)]
    [InlineData("up", false)]
    public async Task Input_Pointer_ForwardsThePressOrRelease(string kind, bool down)
    {
        _sessions.Setup(x => x.Find(_userId)).Returns(_session.Object);

        await new SendChallengeInput.Handler(_sessions.Object)
            .Handle(new SendChallengeInput.Command(kind, 10, 20, null, null) { UserId = _userId }, TestContext.Current.CancellationToken);

        _session.Verify(x => x.PointerAsync(down, 10, 20), Times.Once);
    }

    [Fact]
    public async Task Input_Text_TypesIt()
    {
        _sessions.Setup(x => x.Find(_userId)).Returns(_session.Object);

        await new SendChallengeInput.Handler(_sessions.Object)
            .Handle(new SendChallengeInput.Command("text", null, null, "hello", null) { UserId = _userId }, TestContext.Current.CancellationToken);

        _session.Verify(x => x.TypeAsync("hello"), Times.Once);
    }

    [Theory]
    [InlineData("click", 1d, 1d, null, null)]
    [InlineData("down", null, 1d, null, null)]
    [InlineData("down", 1920d, 1d, null, null)]
    [InlineData("up", 1d, 1080d, null, null)]
    [InlineData("text", null, null, "", null)]
    [InlineData("key", null, null, null, "F12")]
    [InlineData("key", null, null, null, "Control+L")]
    public void InputValidator_BadInput_Fails(string kind, double? x, double? y, string? text, string? key)
    {
        var result = new SendChallengeInput.Validator().Validate(new SendChallengeInput.Command(kind, x, y, text, key));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InputValidator_TextAtTheLimit_Passes()
    {
        var result = new SendChallengeInput.Validator().Validate(
            new SendChallengeInput.Command("text", null, null, new string('a', ChallengeSession.MaxTextLength), null));

        result.IsValid.Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
