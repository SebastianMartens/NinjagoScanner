using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web;
using NinjagoScanner.Web.Components;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// CatalogService/PictureService are only reachable over plain HTTP on Fly's private network (no
// TLS between internal services — see infra/README.md). Grpc.Net.Client requires HTTP/2, and
// SocketsHttpHandler refuses cleartext HTTP/2 (h2c) unless this switch is set, so without it every
// GrpcChannel.ForAddress("http://...") call below throws.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();

var dbPath = WebConfig.ResolveAuthDatabasePath(builder.Configuration);
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory))
    Directory.CreateDirectory(dbDirectory);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.User.RequireUniqueEmail = false;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.AddPolicy("registration", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync(
            "Zu viele Anfragen. Bitte versuche es später erneut.", cancellationToken);
    };
});

var catalogServiceAddress = WebConfig.ResolveCatalogServiceAddress(builder.Configuration);
var pictureServiceAddress = WebConfig.ResolvePictureServiceAddress(builder.Configuration);
var maxUploadBytes = WebConfig.ResolveMaxUploadBytes(builder.Configuration);

builder.Services.AddSingleton(_ => new CatalogServiceClient(catalogServiceAddress));
builder.Services.AddSingleton(_ => new PictureServiceClient(pictureServiceAddress, catalogServiceAddress, maxUploadBytes));
builder.Services.AddSingleton(provider => new CollectionQueryService(
    provider.GetRequiredService<CatalogServiceClient>(),
    provider.GetRequiredService<PictureServiceClient>()));

// OTLP endpoint/headers (OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_HEADERS) are read
// automatically by AddOtlpExporter() from the standard OTel environment variables — see
// openspec/changes/add-opentelemetry-observability/design.md. gRPC client instrumentation hooks
// into SocketsHttpHandler/HttpClient diagnostics, so CatalogServiceClient/PictureServiceClient's
// per-call GrpcChannel.ForAddress(...) calls pick it up with no changes to those classes. The
// trace batch export interval is shortened because this app's Fly machine has
// min_machines_running = 0 (autostop) — a longer default delay risks losing the last batch when
// the machine is stopped shortly after handling a request. Protocol is forced to HttpProtobuf
// because the .NET SDK otherwise defaults to gRPC, which Grafana Cloud's OTLP gateway doesn't
// accept (export would fail silently — non-blocking by design).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "ninjago-scanner-web"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 2000;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("System.Runtime")
        .AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorPages();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
