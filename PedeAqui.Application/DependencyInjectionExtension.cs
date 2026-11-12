using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PedeAqui.Application.Idempotency;
using PedeAqui.Application.Services;
using PedeAqui.Application.Settings;
using PedeAqui.Application.UseCases.Idempotency.Cleanup;
using PedeAqui.Application.UseCases.Orders.Cancel;
using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Application.UseCases.Orders.Expire;
using PedeAqui.Application.UseCases.Orders.GetAll;
using PedeAqui.Application.UseCases.Orders.GetById;
using PedeAqui.Application.UseCases.Orders.Update;
using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Application.UseCases.Payments.Reconcile;
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
        AddSettings(services, configuration);
        AddServices(services);
        AddUseCases(services);
        AddMapper(services, configuration);
    }

    public static IServiceCollection AddPaymentProcessing(this IServiceCollection services)
    {
        services.AddScoped<IProcessOrderPaymentUseCase, ProcessOrderPaymentUseCase>();
        services.AddScoped<IReconcilePaymentsUseCase, ReconcilePaymentsUseCase>();

        return services;
    }

    private static void AddSettings(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RequestLimits>(configuration.GetSection(RequestLimits.SECTION));
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SECTION));
        services.Configure<OrdersOptions>(configuration.GetSection(OrdersOptions.SECTION));
        services.Configure<PaymentsOptions>(configuration.GetSection(PaymentsOptions.SECTION));
    }

    private static void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOrderStockReleaser, OrderStockReleaser>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
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
        services.AddScoped<IGetAllOrdersUseCase, GetAllOrdersUseCase>();
        services.AddScoped<IGetOrderByIdUseCase, GetOrderByIdUseCase>();
        services.AddScoped<IExpirePendingOrdersUseCase, ExpirePendingOrdersUseCase>();

        services.AddScoped<ICleanupIdempotencyKeysUseCase, CleanupIdempotencyKeysUseCase>();
    }

    private static void AddMapper(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(
            config => config.LicenseKey = configuration["AutoMapper:LicenseKey"],
            typeof(DependencyInjectionExtension));
    }
}
