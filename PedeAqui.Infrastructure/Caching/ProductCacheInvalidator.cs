using PedeAqui.Domain.Caching;
using PedeAqui.Infrastructure.Redis;
using StackExchange.Redis;

namespace PedeAqui.Infrastructure.Caching;

internal class ProductCacheInvalidator : IProductCacheInvalidator
{
    private readonly RedisConnection _redis;

    public ProductCacheInvalidator(RedisConnection redis)
    {
        _redis = redis;
    }

    public async Task Invalidate(IEnumerable<long> productIds, CancellationToken cancellationToken)
    {
        var keys = productIds
            .Distinct()
            .Select(productId => (RedisKey)ProductCacheKeys.ById(productId))
            .Append(ProductCacheKeys.ALL)
            .ToArray();

        await _redis.TryExecute((database, _) => database.KeyDeleteAsync(keys), cancellationToken);
    }
}
