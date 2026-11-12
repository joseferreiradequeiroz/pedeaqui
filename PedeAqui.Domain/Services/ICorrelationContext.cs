namespace PedeAqui.Domain.Services;

public interface ICorrelationContext
{
    string CorrelationId { get; }
}
