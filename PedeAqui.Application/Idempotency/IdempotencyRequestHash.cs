using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.Idempotency;

public static class IdempotencyRequestHash
{
    public static string Of(string requestBody)
    {
        var canonicalJson = CanonicalJson(requestBody);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CanonicalJson(string requestBody)
    {
        try
        {
            var parsed = JsonNode.Parse(requestBody);

            return WithPropertiesSortedByName(parsed)?.ToJsonString() ?? "null";
        }
        catch (System.Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new ErrorOnValidationException([ResourceErrorMessages.IDEMPOTENCY_REQUEST_BODY_INVALID]);
        }
    }

    private static JsonNode? WithPropertiesSortedByName(JsonNode? node)
    {
        return node switch
        {
            JsonObject jsonObject => new JsonObject(jsonObject
                .OrderBy(property => property.Key, StringComparer.Ordinal)
                .Select(property => KeyValuePair.Create(property.Key, WithPropertiesSortedByName(property.Value)))),
            JsonArray jsonArray => new JsonArray(jsonArray.Select(WithPropertiesSortedByName).ToArray()),
            _ => node?.DeepClone()
        };
    }
}
