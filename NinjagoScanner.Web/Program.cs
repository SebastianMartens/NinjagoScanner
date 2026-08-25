using NinjagoScanner.Web;
using NinjagoScanner.Web.Components;
using NinjagoScanner.Web.Services;

// CatalogService/PictureService are only reachable over plain HTTP on Fly's private network (no
// TLS between internal services — see infra/README.md). Grpc.Net.Client requires HTTP/2, and
// SocketsHttpHandler refuses cleartext HTTP/2 (h2c) unless this switch is set, so without it every
// GrpcChannel.ForAddress("http://...") call below throws.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var catalogServiceAddress = WebConfig.ResolveCatalogServiceAddress(builder.Configuration);
var pictureServiceAddress = WebConfig.ResolvePictureServiceAddress(builder.Configuration);
var maxUploadBytes = WebConfig.ResolveMaxUploadBytes(builder.Configuration);

builder.Services.AddSingleton(_ => new CardCatalogService(catalogServiceAddress, pictureServiceAddress, maxUploadBytes));
builder.Services.AddSingleton(_ => new PictureServiceClient(pictureServiceAddress, catalogServiceAddress));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// No HTTPS redirect/HSTS here: Fly's edge proxy terminates TLS and forwards plain HTTP to this
// app (fly.toml sets force_https), the same way CatalogService/PictureService already run without
// TLS on Fly's private network — see infra/README.md.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
