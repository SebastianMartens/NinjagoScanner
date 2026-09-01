## 1. NuGet packages and project setup

- [x] 1.1 Add NuGet packages to `NinjagoScanner.Web.csproj`: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`
- [x] 1.2 Add Razor Pages support in `Program.cs` (`AddRazorPages()` / `MapRazorPages()`) — needed for the login/register/logout Razor Pages

## 2. Identity model and DbContext

- [x] 2.1 Create `AppUser : IdentityUser` in `NinjagoScanner.Web/Data/AppUser.cs` (empty class for now — future `TenantId` goes here)
- [x] 2.2 Create `AppDbContext : IdentityDbContext<AppUser>` in `NinjagoScanner.Web/Data/AppDbContext.cs`
- [x] 2.3 Register Identity and EF Core services in `Program.cs`: `AddDbContext<AppDbContext>` with SQLite connection string, `AddIdentity<AppUser, IdentityRole>().AddEntityFrameworkStores<AppDbContext>()`, configure Identity options (password policy, lockout, cookie settings) per design.md section 7
- [x] 2.4 Add database path resolution to `WebConfig.cs` (config key `Auth:DatabasePath` → env var `AUTH_DATABASE_PATH` → default), add `Data/` to `.gitignore`
- [x] 2.5 Database initialization at startup using `Database.EnsureCreated()` (replaces migration-based approach — no migration files needed; the Identity schema is created directly from the model)

## 3. Auth middleware pipeline

- [x] 3.1 Add `UseAuthentication()` and `UseAuthorization()` to the middleware pipeline in `Program.cs`, between `UseAntiforgery()` and `MapStaticAssets()`
- [x] 3.2 Configure a global fallback authorization policy requiring authenticated users (`options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`)
- [x] 3.3 Configure the authentication cookie: redirect unauthenticated requests to `/Account/Login`

## 4. Blazor auth integration

- [x] 4.1 Add `@using Microsoft.AspNetCore.Components.Authorization` to `_Imports.razor`
- [x] 4.2 Replace `<RouteView>` with `<AuthorizeRouteView>` in `Routes.razor`, add `<NotAuthorized>` block that redirects to `/Account/Login`
- [x] 4.3 Add `<CascadingAuthenticationState>` wrapper in `Routes.razor` (or `App.razor`)
- [x] 4.4 Create a `RevalidatingServerAuthenticationStateProvider` subclass and register it in DI

## 5. Login, registration, and logout pages

- [x] 5.1 Create the login Razor Page at `Pages/Account/Login.cshtml` + `Login.cshtml.cs` — form with username/password fields, `[AllowAnonymous]`, `SignInManager.PasswordSignInAsync`, generic error message on failure, lockout message when locked, German UI text
- [x] 5.2 Create the registration Razor Page at `Pages/Account/Register.cshtml` + `Register.cshtml.cs` — form with username/password/confirm-password fields, `[AllowAnonymous]`, `UserManager.CreateAsync`, validation errors for duplicate username and weak password, German UI text, redirect to login on success
- [x] 5.3 Create the logout Razor Page at `Pages/Account/Logout.cshtml.cs` — POST-only, `SignInManager.SignOutAsync`, redirect to `/Account/Login`
- [x] 5.4 Style the login and registration pages to match the existing app design (use existing CSS variables and layout patterns)

## 6. Navigation changes

- [x] 6.1 Add a logout button/form to `NavMenu.razor` (POST form to `/Account/Logout` with antiforgery token)

## 7. Rate limiting

- [x] 7.1 Add rate limiting services in `Program.cs`: `AddRateLimiter()` with two named fixed-window policies — `"registration"` (5/hour per IP) and `"login"` (10/minute per IP)
- [x] 7.2 Apply rate limit policies to the login and registration Razor Pages (via `[EnableRateLimiting("policy")]` attribute)
- [x] 7.3 Add `UseRateLimiter()` to the middleware pipeline

## 8. Fly infrastructure

- [x] 8.1 Update `NinjagoScanner.Web/fly.toml` to add a `[mounts]` section mounting a volume at `/data`

## 9. Testing and verification

- [ ] 9.1 Verify the solution builds (`dotnet build NinjagoScanner.slnx`) — blocked by NuGet restore issue in current shell environment (Environment.GetFolderPath returns null for Windows special folders under Git Bash)
- [ ] 9.2 Run the Web app locally and verify: registration flow, login flow, logout, unauthenticated redirect, rate limiting feedback
- [ ] 9.3 Run existing tests (`dotnet test NinjagoScanner.slnx`) to confirm no regressions
