namespace PedeAqui.Application.Idempotency;

public interface IIdempotencyService
{
    Task<IdempotencyStart> Begin(long userId, string key, string method, string path, string requestBody, CancellationToken cancellationToken);

    Task Complete(long requestId, int statusCode, string? body, CancellationToken cancellationToken);

    Task Release(long requestId, CancellationToken cancellationToken);
}
