using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Auth;

public static class GetRegistrationStatus
{
    public record Query;

    public record Response(bool Open);

    public class Handler(OphiDbContext dbContext, IOptions<RegistrationSettings> settings)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken) =>
            new(await IsOpenAsync(dbContext, settings.Value, cancellationToken));
    }

    /// <summary>
    /// Shared with <see cref="Register"/> so the sign-up page and the endpoint can never disagree.
    /// </summary>
    internal static async Task<bool> IsOpenAsync(OphiDbContext dbContext, RegistrationSettings settings, CancellationToken cancellationToken) =>
        settings.Enabled || !await dbContext.Users.AnyAsync(cancellationToken);

    public static void MapGetRegistrationStatusEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/auth/registration", async (IMessageBus bus) =>
            Results.Ok(await bus.InvokeAsync<Response>(new Query())))
        .WithName("GetRegistrationStatus")
        .WithTags("Auth")
        .WithSummary("Check whether sign-up is open")
        .WithDescription("Reports whether new accounts can register. Closed when the operator sets Registration:Enabled=false, except while the instance has no accounts yet, so the first account can still be created.")
        .Produces<Response>(200)
        .AllowAnonymous();
}
