using PedeAqui.Application.UseCases.Products.Delete;
using PedeAqui.Application.UseCases.Products.GetAll;
using PedeAqui.Application.UseCases.Products.GetById;
using PedeAqui.Application.UseCases.Products.Register;
using PedeAqui.Application.UseCases.Products.Update;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Products;

public class ProductUseCasesTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Register_ValidRequest_StoresProductWithZeroStock()
    {
        var repository = new InMemoryProductRepository();
        var request = RequestBuilder.Product();

        var response = await new RegisterProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(request);

        var stored = Assert.Single(repository.Products);
        Assert.Equal(stored.Id, response.Id);
        Assert.Equal(request.Name, stored.Name);
        Assert.Equal(request.Sku, stored.Sku);
        Assert.Equal(request.Price, stored.Price);
        Assert.Equal(0, stored.StockQuantity);
        Assert.True(stored.Active);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Register_DuplicatedSku_ThrowsAndDoesNotStore()
    {
        var existing = ProductBuilder.Build();
        var repository = new InMemoryProductRepository(existing);
        var request = RequestBuilder.Product();
        request.Sku = existing.Sku;

        var exception = await Assert.ThrowsAsync<ErrorOnValidationException>(
            () => new RegisterProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(request));

        Assert.Contains(ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS, exception.Errors);
        Assert.Single(repository.Products);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Update_ValidRequest_ChangesFieldsButKeepsStock()
    {
        var product = ProductBuilder.Build(stock: 7);
        var repository = new InMemoryProductRepository(product);
        var request = RequestBuilder.Product();

        await new UpdateProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(product.Id, request);

        Assert.Equal(request.Name, product.Name);
        Assert.Equal(request.Price, product.Price);
        Assert.Equal(request.Sku, product.Sku);
        Assert.Equal(7, product.StockQuantity);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Update_KeepingOwnSku_IsAllowed()
    {
        var product = ProductBuilder.Build();
        var repository = new InMemoryProductRepository(product);
        var request = RequestBuilder.Product();
        request.Sku = product.Sku;

        await new UpdateProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(product.Id, request);

        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Update_SkuOfAnotherProduct_Throws()
    {
        var product = ProductBuilder.Build(id: 1);
        var other = ProductBuilder.Build(id: 2);
        var repository = new InMemoryProductRepository(product, other);
        var request = RequestBuilder.Product();
        request.Sku = other.Sku;

        var exception = await Assert.ThrowsAsync<ErrorOnValidationException>(
            () => new UpdateProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(product.Id, request));

        Assert.Contains(ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS, exception.Errors);
    }

    [Fact]
    public async Task Update_UnknownProduct_ThrowsNotFound()
    {
        var repository = new InMemoryProductRepository();

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => new UpdateProductUseCase(repository, repository, _unitOfWork, MapperFactory.Create()).Execute(42, RequestBuilder.Product()));

        Assert.Equal(ResourceErrorMessages.PRODUCT_NOT_FOUND, exception.Message);
    }

    [Fact]
    public async Task Delete_ExistingProduct_DeactivatesInsteadOfRemoving()
    {
        var product = ProductBuilder.Build();
        var repository = new InMemoryProductRepository(product);

        await new DeleteProductUseCase(repository, _unitOfWork).Execute(product.Id);

        Assert.Single(repository.Products);
        Assert.False(product.Active);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Delete_AlreadyInactive_ThrowsNotFound()
    {
        var product = ProductBuilder.Inactive();
        var repository = new InMemoryProductRepository(product);

        await Assert.ThrowsAsync<NotFoundException>(() => new DeleteProductUseCase(repository, _unitOfWork).Execute(product.Id));
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveProducts()
    {
        var active = ProductBuilder.Build(id: 1);
        var inactive = ProductBuilder.Inactive(id: 2);
        var repository = new InMemoryProductRepository(active, inactive);

        var response = await new GetAllProductsUseCase(repository, MapperFactory.Create()).Execute();

        var returned = Assert.Single(response.Products);
        Assert.Equal(active.Id, returned.Id);
        Assert.Equal(active.StockQuantity, returned.StockQuantity);
    }

    [Fact]
    public async Task GetById_InactiveProduct_ThrowsNotFound()
    {
        var product = ProductBuilder.Inactive();
        var repository = new InMemoryProductRepository(product);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetProductByIdUseCase(repository, MapperFactory.Create()).Execute(product.Id));
    }
}
