using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddSingleton<CatalogRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<CardCatalogGrpcService>();
app.MapGet("/", () => "This service exposes card catalog data via gRPC. Use a gRPC client to call CardCatalog endpoints.");

app.Run();
