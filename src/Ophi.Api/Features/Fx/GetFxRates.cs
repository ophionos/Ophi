using Microsoft.EntityFrameworkCore;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Fx;

/// <summary>
/// Serves the stored ECB euro reference rates for display-currency conversion. The backend only
/// stores and serves them: conversion happens in the frontend display layer, so nothing that
/// decides an alert or a comparison can ever read a rate.
/// </summary>
public static class GetFxRates
{
    /// <summary>
    /// The ECB currencies that have a published euro reference rate (live feed, 2026-09-25), plus EUR.
    /// A display currency outside this set would have no rate to convert with.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedCurrencies = new HashSet<string>(
    [
        "EUR", "USD", "JPY", "CZK", "DKK", "GBP", "HUF", "PLN", "RON", "SEK", "CHF", "ISK", "NOK",
        "TRY", "AUD", "BRL", "CAD", "CNY", "HKD", "IDR", "ILS", "INR", "KRW", "MXN", "MYR", "NZD",
        "PHP", "SGD", "THB", "ZAR"
    ], StringComparer.OrdinalIgnoreCase);

    public record Query;

    /// <param name="Rates">Units of each currency per 1 EUR. Cross rates go through EUR.</param>
    /// <param name="AsOf">ECB publication date; null until the worker's first fetch.</param>
    /// <param name="Supported">
    /// Currencies a user may pick as display currency — the one owner of that list, so the settings
    /// picker never drifts from the validator.
    /// </param>
    public record Response(
        string Base, DateTime? AsOf, IReadOnlyDictionary<string, decimal> Rates, IReadOnlyList<string> Supported);

    public class Handler(OphiDbContext dbContext)
    {
        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            var rows = await dbContext.ExchangeRates.AsNoTracking().ToListAsync(cancellationToken);
            return new Response(
                "EUR",
                rows.Count == 0 ? null : rows.Max(r => r.AsOf),
                rows.ToDictionary(r => r.Currency, r => r.UnitsPerEur),
                SupportedCurrencies.Order(StringComparer.Ordinal).ToList());
        }
    }

    public static void MapGetFxRatesEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/fx-rates", async (IMessageBus bus) =>
            Results.Ok(await bus.InvokeAsync<Response>(new Query())))
        .WithName("GetFxRates")
        .WithTags("Fx")
        .WithSummary("ECB euro reference rates for display-currency conversion")
        .WithDescription("Display only. Rates are units per 1 EUR; convert A→B as amount / rate[A] * rate[B]. " +
                         "Currencies the ECB does not publish are absent — show no conversion for them.")
        .Produces<Response>()
        .RequireAuthorization();
}
