FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PedeAqui.Worker/PedeAqui.Worker.csproj PedeAqui.Worker/
COPY PedeAqui.Application/PedeAqui.Application.csproj PedeAqui.Application/
COPY PedeAqui.Communication/PedeAqui.Communication.csproj PedeAqui.Communication/
COPY PedeAqui.Domain/PedeAqui.Domain.csproj PedeAqui.Domain/
COPY PedeAqui.Exception/PedeAqui.Exception.csproj PedeAqui.Exception/
COPY PedeAqui.Infrastructure/PedeAqui.Infrastructure.csproj PedeAqui.Infrastructure/
RUN dotnet restore PedeAqui.Worker/PedeAqui.Worker.csproj

COPY . .
RUN dotnet publish PedeAqui.Worker/PedeAqui.Worker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PedeAqui.Worker.dll"]
