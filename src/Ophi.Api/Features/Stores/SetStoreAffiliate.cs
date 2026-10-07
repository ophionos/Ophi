using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Features.Stores;

public static class SetStoreAffiliate
{
    public static void MapSetStoreAffiliateEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/stores/{storeId}/affiliate", async (string storeId, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(storeId, request.AffiliateParamName, request.AffiliateTag) { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("SetStoreAffiliate")
        .WithTags("Stores")
        .WithSummary("Set affiliate code for any store (including built-in)")
        .Produces<Response>()
        .RequireAuthorization();
    }

    public record Request(string? AffiliateParamName, string? AffiliateTag);

    public record Command(string StoreId, string? AffiliateParamName, string? AffiliateTag)
    {
        public Guid UserId { get; init; }
    }

    public record Response(string StoreId, string? AffiliateParamName, string? AffiliateTag);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StoreId).NotEmpty();

            RuleFor(x => x.AffiliateParamName)
                .MaximumLength(50)
                .Matches(@"^\S+$").WithMessage("Parameter name must not contain whitespace")
                .When(x => !string.IsNullOrEmpty(x.AffiliateParamName));

            RuleFor(x => x.AffiliateTag)
                .MaximumLength(100)
                .When(x => !string.IsNullOrEmpty(x.AffiliateTag));
        }
    }

    public class Handler(OphiDbContext dbContext, IStoreConfigProvider configProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            // Check if user already has a StoreConfiguration for this storeId
            var existing = await dbContext.StoreConfigurations
                .FirstOrDefaultAsync(s => s.UserId == command.UserId && s.StoreId == command.StoreId, cancellationToken);

            if (existing != null)
            {
                existing.AffiliateParamName = command.AffiliateParamName;
                existing.AffiliateTag = command.AffiliateTag;
                await dbContext.SaveChangesAsync(cancellationToken);
                configProvider.InvalidateCache(command.UserId);
                return new Response(command.StoreId, existing.AffiliateParamName, existing.AffiliateTag);
            }

            // Verify the storeId exists as a built-in store
            var builtIn = configProvider.GetAllConfigs()
                .FirstOrDefault(c => string.Equals(c.Id, command.StoreId, StringComparison.OrdinalIgnoreCase));

            if (builtIn == null)
                throw new NotFoundException($"Store '{command.StoreId}' not found");

            // Create a skeleton StoreConfiguration for affiliate-only config
            var skeleton = new Domain.Entities.StoreConfiguration
            {
                StoreId = builtIn.Id,
                Name = builtIn.Name,
                DomainPatternsJson = "[]",
                SelectorsJson = "{}",
                IsAutoCreated = true,
                UserId = command.UserId,
                AffiliateParamName = command.AffiliateParamName,
                AffiliateTag = command.AffiliateTag
            };

            dbContext.StoreConfigurations.Add(skeleton);
            await dbContext.SaveChangesAsync(cancellationToken);
            configProvider.InvalidateCache(command.UserId);

            logger.LogInformation("Affiliate set for store {StoreId} by user {UserId}", command.StoreId, command.UserId);
            return new Response(command.StoreId, skeleton.AffiliateParamName, skeleton.AffiliateTag);
        }
    }
}
