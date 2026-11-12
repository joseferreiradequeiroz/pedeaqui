using System.Text.Json;

namespace PedeAqui.Infrastructure.Messaging;

public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
