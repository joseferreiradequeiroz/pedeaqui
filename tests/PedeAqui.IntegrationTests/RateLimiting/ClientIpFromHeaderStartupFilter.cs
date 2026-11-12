using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace PedeAqui.IntegrationTests.RateLimiting;

public sealed class ClientIpFromHeaderStartupFilter : IStartupFilter
{
    public const string HEADER_NAME = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use(async (httpContext, nextMiddleware) =>
            {
                if (IPAddress.TryParse(httpContext.Request.Headers[HEADER_NAME], out var clientIp))
                    httpContext.Connection.RemoteIpAddress = clientIp;

                await nextMiddleware(httpContext);
            });

            next(application);
        };
    }
}
