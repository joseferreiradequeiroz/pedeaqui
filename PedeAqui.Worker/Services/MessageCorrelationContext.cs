using PedeAqui.Domain.Services;

namespace PedeAqui.Worker.Services;

public sealed class MessageCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;
}
