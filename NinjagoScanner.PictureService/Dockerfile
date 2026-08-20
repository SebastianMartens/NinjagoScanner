# Build context must be the repository root, e.g.:
#   docker build -f NinjagoScanner.PictureService/Dockerfile -t ninjago-picture-service .
# PictureService has no project references to the other services (it calls CatalogService over
# gRPC at runtime), so only its own project directory is needed.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj NinjagoScanner.PictureService/
RUN dotnet restore NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj

COPY NinjagoScanner.PictureService/ NinjagoScanner.PictureService/
RUN dotnet publish NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# mcr.microsoft.com/dotnet/aspnet:10.0 already ships a non-root "app" user/group; reuse it.
USER app

COPY --from=build --chown=app:app /app .

# .NET 8+ ASP.NET Core container images default ASPNETCORE_HTTP_PORTS to 8080.
EXPOSE 8080

# Required at runtime (see ScannerConfig.ResolvePhotosBucketName/ResolveSidecarTableName and
# GeminiApiService): Storage__PhotosBucketName, Storage__SidecarTableName, Gemini__ApiKey
# (from Secrets Manager via the ECS task definition), CatalogService__Address.
ENTRYPOINT ["dotnet", "NinjagoScanner.PictureService.dll"]
