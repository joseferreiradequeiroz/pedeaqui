namespace PedeAqui.Infrastructure.Caching;

internal static class ProductCacheKeys
{
    public const string ALL = "products:all";

    public static string ById(long productId) => $"products:{productId}";
}
