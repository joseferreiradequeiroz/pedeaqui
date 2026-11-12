FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PedeAqui.PaymentGateway/PedeAqui.PaymentGateway.csproj PedeAqui.PaymentGateway/
RUN dotnet restore PedeAqui.PaymentGateway/PedeAqui.PaymentGateway.csproj

COPY PedeAqui.PaymentGateway/ PedeAqui.PaymentGateway/
RUN dotnet publish PedeAqui.PaymentGateway/PedeAqui.PaymentGateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PedeAqui.PaymentGateway.dll"]
