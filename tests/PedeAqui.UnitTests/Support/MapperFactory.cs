using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using PedeAqui.Application;

namespace PedeAqui.UnitTests.Support;

public static class MapperFactory
{
    public static IMapper Create()
    {
        var configuration = new MapperConfiguration(
            config => config.AddMaps(typeof(DependencyInjectionExtension).Assembly),
            NullLoggerFactory.Instance);

        return configuration.CreateMapper();
    }
}
