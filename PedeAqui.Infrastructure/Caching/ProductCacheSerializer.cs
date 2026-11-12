using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Caching;

internal static class ProductCacheSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IncludePrivateSetters } }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    private static void IncludePrivateSetters(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(Product))
            return;

        foreach (var property in typeInfo.Properties.Where(property => property.Get is not null && property.Set is null))
        {
            var setter = typeof(Product).GetProperty(property.Name)?.GetSetMethod(nonPublic: true);

            if (setter is not null)
                property.Set = (instance, value) => setter.Invoke(instance, [value]);
        }
    }
}
