using PedeAqui.Domain.Services;
using PedeAqui.Middlewares;

namespace PedeAqui.Services;

public class HttpCorrelationContext : ICorrelationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCorrelationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.HTTP_CONTEXT_ITEM_KEY] as string
        ?? string.Empty;
}
