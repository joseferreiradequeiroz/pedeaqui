namespace PedeAqui.Application.UseCases;

public interface IBackgroundJobUseCase
{
    Task<int> Execute(CancellationToken cancellationToken);
}
