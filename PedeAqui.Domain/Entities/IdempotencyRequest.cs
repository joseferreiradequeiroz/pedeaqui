using PedeAqui.Domain.Enums;

namespace PedeAqui.Domain.Entities;

public class IdempotencyRequest
{
    public const int KEY_MAX_LENGTH = 100;

    public long Id { get; set; }
    public long UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LockedUntil { get; set; }

    public bool IsSameOperationAs(string method, string path, string requestHash)
    {
        return Method == method && Path == path && RequestHash == requestHash;
    }

    public bool IsLockedAt(DateTime now) => LockedUntil > now;

    public bool IsExpiredAt(DateTime now) => ExpiresAt <= now;
}
