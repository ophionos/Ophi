using System.Text.Json;
using Ophi.Api.Common.Events;
using Ophi.Domain.Messages.Events;
using Wolverine.Attributes;

namespace Ophi.Api.Features.Events;

/// <summary>
/// Bridges the Wolverine <see cref="LiveUpdate"/> message to the in-process SSE registry. In split
/// mode the worker routes <see cref="LiveUpdate"/> across the Postgres transport to this API instance;
/// in embedded mode it's handled in-process. Either way this serializes a thin frame and fans it out
/// to the user's open <c>EventSource</c> connections.
/// </summary>
[WolverineHandler]
public static class PushLiveUpdateHandler
{
    private static readonly JsonSerializerOptions FrameOptions = new(JsonSerializerDefaults.Web);

    public static ValueTask HandleAsync(LiveUpdate message, ISseConnectionRegistry registry)
    {
        var frame = JsonSerializer.Serialize(new Frame(message.Kind, message.ProductId), FrameOptions);
        return registry.PublishAsync(message.UserId, frame);
    }

    // The thin payload the browser receives. No price data — surfaces refetch via their own loaders.
    // Serialized camelCase (type, productId) to match the rest of the API and JS conventions.
    private record Frame(string Type, Guid? ProductId);
}
