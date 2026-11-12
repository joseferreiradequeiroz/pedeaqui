using PedeAqui.Domain.Repositories.Idempotency;

namespace PedeAqui.Application.UseCases.Idempotency.Cleanup;

public class CleanupIdempotencyKeysUseCase : ICleanupIdempotencyKeysUseCase
{
    private readonly IIdempotencyRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CleanupIdempotencyKeysUseCase(IIdempotencyRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public Task<int> Execute(CancellationToken cancellationToken)
    {
        return _repository.DeleteExpired(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
