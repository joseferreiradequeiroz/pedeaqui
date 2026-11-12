using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Application.UseCases.Orders.Cancel;
using PedeAqui.Application.UseCases.Orders.Confirm;
using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Application.UseCases.Orders.GetAll;
using PedeAqui.Application.UseCases.Orders.GetById;
using PedeAqui.Application.UseCases.Orders.Update;
using PedeAqui.Application.UseCases.Products.Delete;
using PedeAqui.Application.UseCases.Products.GetAll;
using PedeAqui.Application.UseCases.Products.GetById;
using PedeAqui.Application.UseCases.Products.Register;
using PedeAqui.Application.UseCases.Products.Update;
using PedeAqui.Application.UseCases.Stock.Entry;
using PedeAqui.Application.UseCases.Stock.Exit;
using PedeAqui.Application.UseCases.Stock.GetByProduct;
using PedeAqui.Application.UseCases.Users.Login;
using PedeAqui.Application.UseCases.Users.Register;

namespace PedeAqui.Application;

public static class DependencyInjectionExtension
{
    public static void AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        AddUseCases(services);
        AddMapper(services, configuration);
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<IRegisterUserUseCase, RegisterUserUseCase>();
        services.AddScoped<ILoginUseCase, LoginUseCase>();

        services.AddScoped<IRegisterProductUseCase, RegisterProductUseCase>();
        services.AddScoped<IUpdateProductUseCase, UpdateProductUseCase>();
        services.AddScoped<IDeleteProductUseCase, DeleteProductUseCase>();
        services.AddScoped<IGetAllProductsUseCase, GetAllProductsUseCase>();
        services.AddScoped<IGetProductByIdUseCase, GetProductByIdUseCase>();

        services.AddScoped<IRegisterStockEntryUseCase, RegisterStockEntryUseCase>();
        services.AddScoped<IRegisterStockExitUseCase, RegisterStockExitUseCase>();
        services.AddScoped<IGetStockByProductUseCase, GetStockByProductUseCase>();

        services.AddScoped<ICreateOrderUseCase, CreateOrderUseCase>();
        services.AddScoped<IUpdateOrderUseCase, UpdateOrderUseCase>();
        services.AddScoped<ICancelOrderUseCase, CancelOrderUseCase>();
        services.AddScoped<IConfirmOrderUseCase, ConfirmOrderUseCase>();
        services.AddScoped<IGetAllOrdersUseCase, GetAllOrdersUseCase>();
        services.AddScoped<IGetOrderByIdUseCase, GetOrderByIdUseCase>();
    }

    private static void AddMapper(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(
            config => config.LicenseKey = configuration["AutoMapper:LicenseKey"],
            typeof(DependencyInjectionExtension));
    }
}
