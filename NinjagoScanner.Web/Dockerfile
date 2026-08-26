# Build context must be the repository root, e.g.:
#   docker build -f NinjagoScanner.Web/Dockerfile -t ninjago-web .
# NinjagoScanner.Web has no project references to the other services (it calls CatalogService and
# PictureService over gRPC at runtime), so only its own project directory is needed.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NinjagoScanner.Web/NinjagoScanner.Web.csproj NinjagoScanner.Web/
RUN dotnet restore NinjagoScanner.Web/NinjagoScanner.Web.csproj

COPY NinjagoScanner.Web/ NinjagoScanner.Web/
# No --no-restore: a restore done with only the .csproj present (the cache-friendly step above)
# silently drops the ASP.NET Core shared framework's Blazor JS assets (_framework/blazor.web.js,
# blazor.server.js) from the publish output, causing production 404s on those files even though
# the build succeeds with no warnings. Restoring again here (packages are already downloaded, so
# it's fast) fixes it.
RUN dotnet publish NinjagoScanner.Web/NinjagoScanner.Web.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# mcr.microsoft.com/dotnet/aspnet:10.0 already ships a non-root "app" user/group; reuse it.
USER app

COPY --from=build --chown=app:app /app .

# .NET 8+ ASP.NET Core container images default ASPNETCORE_HTTP_PORTS to 8080. This is the only
# port this image exposes - it's plain HTTP/1.1 (Blazor Server over SignalR), not gRPC, so it
# needs none of CatalogService/PictureService's separate health-check-port dance.
EXPOSE 8080

# Required at runtime (see WebConfig.cs): CatalogService__Address, PictureService__Address. This
# service never holds AWS credentials - photo bytes and download URLs both come from
# PictureService over gRPC.
ENTRYPOINT ["dotnet", "NinjagoScanner.Web.dll"]
