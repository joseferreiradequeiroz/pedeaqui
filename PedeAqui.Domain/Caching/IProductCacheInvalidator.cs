namespace PedeAqui.Domain.Caching;

public interface IProductCacheInvalidator
{
    Task Invalidate(IEnumerable<long> productIds, CancellationToken cancellationToken);
}
