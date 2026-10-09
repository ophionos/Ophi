using Ophi.Api.Features.Account;
using Ophi.Api.Features.Alerts;
using Ophi.Api.Features.ApiKeys;
using Ophi.Api.Features.Auth;
using Ophi.Api.Features.Comparisons;
using Ophi.Api.Features.Events;
using Ophi.Api.Features.Fx;
using Ophi.Api.Features.Notifications;
using Ophi.Api.Features.Challenges;
using Ophi.Api.Features.Products;
using Ophi.Api.Features.Scraping;
using Ophi.Api.Features.Settings;
using Ophi.Api.Features.Stores;
using Ophi.Api.Features.Tags;
using Ophi.Api.Features.Webhooks;

namespace Ophi.Api.Common.Startup;

internal static class EndpointRouting
{
    /// <summary>
    /// Registers every feature endpoint. Grouped by domain for readability; route
    /// strings and policies live in the individual Map…Endpoint extension methods.
    /// </summary>
    public static IEndpointRouteBuilder MapOphiEndpoints(this IEndpointRouteBuilder app)
    {
        // Auth
        app.MapRegisterEndpoint();
        app.MapGetRegistrationStatusEndpoint();
        app.MapLoginEndpoint();
        app.MapLogoutEndpoint();
        app.MapMeEndpoint();
        app.MapForgotPasswordEndpoint();
        app.MapResetPasswordEndpoint();

        // Account (signed-in self-service)
        app.MapChangePasswordEndpoint();
        app.MapUpdateProfileEndpoint();
        app.MapConfirmEmailChangeEndpoint();
        app.MapDeleteAccountEndpoint();
        app.MapExportBackupEndpoint();
        app.MapImportBackupEndpoint();

        // Products
        app.MapAddProductEndpoint();
        app.MapGetProductsEndpoint();
        app.MapGetProductEndpoint();
        app.MapLookupProductEndpoint();
        app.MapGetPriceHistoryEndpoint();
        app.MapDeleteProductEndpoint();
        app.MapUpdateProductEndpoint();
        app.MapAddProductUrlEndpoint();
        app.MapRemoveProductUrlEndpoint();
        app.MapGetScrapeLogEndpoint();
        app.MapRetryScrapeProductUrlEndpoint();
        app.MapResumeProductUrlEndpoint();
        app.MapCreateProductEndpoint();
        app.MapExportProductsEndpoint();
        app.MapImportProductsEndpoint();

        // Anti-bot challenge sessions
        app.MapGetChallengeAvailabilityEndpoint();
        app.MapStartChallengeEndpoint();
        app.MapGetChallengeEndpoint();
        app.MapSendChallengeInputEndpoint();
        app.MapCloseChallengeEndpoint();

        // Alerts
        app.MapCreateAlertEndpoint();
        app.MapGetAlertsEndpoint();
        app.MapRedenominateAlertEndpoint();
        app.MapSetAlertActiveEndpoint();
        app.MapDeleteAlertEndpoint();

        // Tags
        app.MapCreateTagEndpoint();
        app.MapGetTagsEndpoint();
        app.MapUpdateTagEndpoint();
        app.MapDeleteTagEndpoint();
        app.MapAddTagToProductEndpoint();
        app.MapRemoveTagFromProductEndpoint();

        // Comparisons
        app.MapCreateComparisonGroupEndpoint();
        app.MapGetComparisonGroupsEndpoint();
        app.MapDeleteComparisonGroupEndpoint();
        app.MapAddProductToGroupEndpoint();
        app.MapAddProductsToGroupEndpoint();
        app.MapRemoveProductFromGroupEndpoint();
        app.MapGetComparisonGroupEndpoint();

        // Stores
        app.MapGetStoresEndpoint();
        app.MapCreateStoreEndpoint();
        app.MapUpdateStoreEndpoint();
        app.MapDeleteStoreEndpoint();
        app.MapTestStoreEndpoint();
        app.MapExportStoreEndpoint();
        app.MapImportStoreEndpoint();
        app.MapDetectStoreEndpoint();
        app.MapSetStoreAffiliateEndpoint();

        // Settings
        app.MapGetSettingsEndpoint();
        app.MapUpdateSettingsEndpoint();
        app.MapTestDiscordWebhookEndpoint();
        app.MapTestPushChannelEndpoint();
        app.MapGetFxRatesEndpoint();
        app.MapSendTestEmailEndpoint();

        // Notifications
        app.MapGetNotificationCountEndpoint();
        app.MapGetNotificationsEndpoint();
        app.MapMarkNotificationReadEndpoint();
        app.MapMarkAllNotificationsReadEndpoint();

        // Webhooks
        app.MapGetWebhookTargetsEndpoint();
        app.MapCreateWebhookTargetEndpoint();
        app.MapUpdateWebhookTargetEndpoint();
        app.MapDeleteWebhookTargetEndpoint();
        app.MapTestWebhookTargetEndpoint();

        // API Keys
        app.MapCreateApiKeyEndpoint();
        app.MapListApiKeysEndpoint();
        app.MapDeleteApiKeyEndpoint();

        // Scraping diagnostics
        app.MapGetScrapeHealthEndpoint();

        // Real-time updates (SSE)
        app.MapStreamEventsEndpoint();

        return app;
    }
}
