# Build context must be the repository root, e.g.:
#   docker build -f NinjagoScanner.CatalogService/Dockerfile -t ninjago-catalog-service .
# CatalogService has no project references to the other services, so only its own project
# directory is needed.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj NinjagoScanner.CatalogService/
RUN dotnet restore NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj

COPY NinjagoScanner.CatalogService/ NinjagoScanner.CatalogService/
# No --no-restore: a restore done with only the .csproj present (the cache-friendly step above)
# can silently drop ASP.NET Core shared-framework static web assets from the publish output (seen
# for real on NinjagoScanner.Web's blazor.web.js — see that Dockerfile's comment). Restoring again
# here is cheap since packages are already downloaded.
RUN dotnet publish NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# mcr.microsoft.com/dotnet/aspnet:10.0 already ships a non-root "app" user/group; reuse it.
USER app

COPY --from=build --chown=app:app /app .

# .NET 8+ ASP.NET Core container images default ASPNETCORE_HTTP_PORTS to 8080.
EXPOSE 8080

ENTRYPOINT ["dotnet", "NinjagoScanner.CatalogService.dll"]
