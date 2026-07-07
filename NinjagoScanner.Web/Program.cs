using NinjagoScanner.Web.Components;
using Microsoft.Extensions.FileProviders;
using NinjagoScanner.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var cardPhotosDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "cardFotos"));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton(new CardCatalogService(cardPhotosDirectory));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (Directory.Exists(cardPhotosDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(cardPhotosDirectory),
        RequestPath = "/cardFotos"
    });
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
