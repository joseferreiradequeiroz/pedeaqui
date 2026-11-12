using PedeAqui.Application.UseCases.Stock.Entry;
using PedeAqui.Application.UseCases.Stock.Exit;
using PedeAqui.Application.UseCases.Stock.GetByProduct;
using PedeAqui.Communication.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Stock;

public class StockUseCasesTests
{
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Entry_ValidRequest_IncreasesStockAndRecordsMovement()
    {
        var product = ProductBuilder.Build(stock: 10);
        var request = RequestBuilder.StockMovement(5);

        var response = await CreateEntryUseCase(product).Execute(product.Id, request);

        Assert.Equal(15, product.StockQuantity);
        var movement = Assert.Single(_movements.Movements);
        Assert.Equal(request.Reason, movement.Reason);
        Assert.Equal(StockMovementType.Entry, response.Type);
        Assert.Equal(5, response.Quantity);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Entry_InvalidRequest_ThrowsAndKeepsStock()
    {
        var product = ProductBuilder.Build(stock: 10);

        await Assert.ThrowsAsync<ErrorOnValidationException>(
            () => CreateEntryUseCase(product).Execute(product.Id, RequestBuilder.StockMovement(0)));

        Assert.Equal(10, product.StockQuantity);
        Assert.Empty(_movements.Movements);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Entry_UnknownProduct_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateEntryUseCase().Execute(42, RequestBuilder.StockMovement()));

        Assert.Equal(ResourceErrorMessages.PRODUCT_NOT_FOUND, exception.Message);
    }

    [Fact]
    public async Task Exit_QuantityAvailable_DecreasesStock()
    {
        var product = ProductBuilder.Build(stock: 10);

        var response = await CreateExitUseCase(product).Execute(product.Id, RequestBuilder.StockMovement(4));

        Assert.Equal(6, product.StockQuantity);
        Assert.Equal(StockMovementType.Exit, response.Type);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Exit_MoreThanAvailable_ThrowsBusinessRuleAndDoesNotCommit()
    {
        var product = ProductBuilder.Build(stock: 2);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateExitUseCase(product).Execute(product.Id, RequestBuilder.StockMovement(3)));

        Assert.StartsWith(ResourceErrorMessages.INSUFFICIENT_STOCK, exception.Message);
        Assert.Equal(2, product.StockQuantity);
        Assert.Empty(_movements.Movements);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task GetByProduct_ReturnsBalanceAndMovementsNewestFirst()
    {
        var product = ProductBuilder.Build(stock: 0);
        await _movements.Add(product.AddStock(10, "Compra"));
        await _movements.Add(product.RemoveStock(3, "Avaria"));
        var repository = new InMemoryProductRepository(product);

        var response = await new GetStockByProductUseCase(repository, _movements, MapperFactory.Create()).Execute(product.Id);

        Assert.Equal(7, response.Quantity);
        Assert.Equal(product.Name, response.ProductName);
        Assert.Equal(["Avaria", "Compra"], response.Movements.Select(movement => movement.Reason));
    }

    private RegisterStockEntryUseCase CreateEntryUseCase(params PedeAqui.Domain.Entities.Product[] products)
    {
        return new RegisterStockEntryUseCase(new InMemoryProductRepository(products), _movements, _unitOfWork, MapperFactory.Create());
    }

    private RegisterStockExitUseCase CreateExitUseCase(params PedeAqui.Domain.Entities.Product[] products)
    {
        return new RegisterStockExitUseCase(new InMemoryProductRepository(products), _movements, _unitOfWork, MapperFactory.Create());
    }
}
