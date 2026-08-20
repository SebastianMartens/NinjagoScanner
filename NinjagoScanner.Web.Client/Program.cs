using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NinjagoScanner.Web.Client;
using NinjagoScanner.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var bffBaseAddress = builder.Configuration["BffBaseAddress"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(bffBaseAddress) });
builder.Services.AddScoped<CardCatalogService>();
builder.Services.AddScoped<PictureServiceClient>();

await builder.Build().RunAsync();
