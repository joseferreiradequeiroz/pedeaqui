using System.Text.Json;
using Microsoft.Extensions.Options;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Infrastructure.Redis;
using StackExchange.Redis;

namespace PedeAqui.Infrastructure.Caching;

internal class CachedProductReadOnlyRepository : IProductReadOnlyRepository
{
    private readonly IProductReadOnlyRepository _inner;
    private readonly RedisConnection _redis;
    private readonly TimeSpan _timeToLive;

    public CachedProductReadOnlyRepository(IProductReadOnlyRepository inner, RedisConnection redis, IOptions<RedisOptions> options)
    {
        _inner = inner;
        _redis = redis;
        _timeToLive = TimeSpan.FromSeconds(options.Value.ProductCacheTtlSeconds);
    }

    public async Task<List<Product>> GetAllActive(CancellationToken cancellationToken)
    {
        return await GetOrLoad(ProductCacheKeys.ALL, async token => await _inner.GetAllActive(token), cancellationToken) ?? [];
    }

    public Task<Product?> GetById(long id, CancellationToken cancellationToken)
    {
        return GetOrLoad(ProductCacheKeys.ById(id), token => _inner.GetById(id, token), cancellationToken);
    }

    public Task<bool> ExistsWithSku(string sku, long? ignoreProductId, CancellationToken cancellationToken)
    {
        return _inner.ExistsWithSku(sku, ignoreProductId, cancellationToken);
    }

    private async Task<T?> GetOrLoad<T>(string key, Func<CancellationToken, Task<T?>> load, CancellationToken cancellationToken)
        where T : class
    {
        var cached = await _redis.TryExecute((database, _) => database.StringGetAsync(key), cancellationToken);

        if (cached.Succeeded && TryDeserialize<T>(cached.Value, out var hit))
            return hit;

        var loaded = await load(cancellationToken);

        if (loaded is not null)
            await _redis.TryExecute(
                (database, _) => database.StringSetAsync(key, ProductCacheSerializer.Serialize(loaded), _timeToLive),
                cancellationToken);

        return loaded;
    }

    private static bool TryDeserialize<T>(RedisValue value, out T? result) where T : class
    {
        result = null;

        if (!value.HasValue)
            return false;

        try
        {
            result = ProductCacheSerializer.Deserialize<T>(value.ToString());
        }
        catch (JsonException)
        {
            return false;
        }

        return result is not null;
    }
}
