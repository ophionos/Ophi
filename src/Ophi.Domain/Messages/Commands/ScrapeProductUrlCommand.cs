namespace Ophi.Domain.Messages.Commands;

public record ScrapeProductUrlCommand(Guid ProductUrlId, bool Force = false);
