## Context

The Web app (Blazor Server, Interactive Server render mode) currently has no authentication. See proposal.md for motivation. The app runs as a single Fly.io machine with `min_machines_running = 0` (autostop). CatalogService and PictureService are internal (Fly 6PN) and remain untouched.

Key current-state constraints:
- `Routes.razor` uses `<RouteView>`, not `<AuthorizeRouteView>` — no auth-aware routing.
- `Program.cs` has no auth middleware in the pipeline.
- The `.csproj` has no Identity or EF Core packages.
- `fly.toml` has no volume mount.
- The UI is German-language (`Übersicht`, `Galerie`, `Sammlung`, etc.).

## Goals / Non-Goals

**Goals:**
- Gate the entire Web app behind username+password login.
- Allow self-registration (username, not email).
- Persist user accounts across deploys via SQLite on a Fly volume.
- Rate-limit login and registration endpoints.

**Non-Goals:**
- Tenant/multi-collection isolation (future work — `AppUser` will carry a `TenantId` later, but no tenant logic now).
- Authenticating gRPC calls between Web → CatalogService/PictureService (private network is sufficient).
- Email verification, password reset, 2FA, OAuth, or any external identity provider.
- Admin UI for user management.
- Role-based authorization (all authenticated users are equal).

## Decisions

### 1. ASP.NET Core Identity (stripped down) over custom auth

**Choice:** Use `Microsoft.AspNetCore.Identity.EntityFrameworkCore` with a custom `AppUser : IdentityUser`, configured to disable email requirement, email confirmation, and 2FA.

**Why over custom cookie auth:** Identity provides audited password hashing (PBKDF2-HMAC-SHA256, 100k iterations), account lockout, and `UserManager<T>` CRUD — all security-critical code we'd otherwise hand-roll. The EF Core dependency is the cost, but it's justified by not having to maintain password storage ourselves.

**Why over full Identity with scaffolded UI:** The scaffolded Identity UI assumes email-based accounts and generates dozens of Razor Pages we'd immediately delete. Writing two pages (login + register) is less work than pruning.

### 2. SQLite via EF Core for user storage

**Choice:** `Microsoft.EntityFrameworkCore.Sqlite` with the database file at a configurable path (default `/data/users.db` in production, `Data/users.db` in development).

**Why over DynamoDB:** Identity ships with an EF Core store (`AddEntityFrameworkStores<T>`). A DynamoDB store would require implementing `IUserStore<T>` (~200 lines of auth-critical code). SQLite is zero custom store code.

**Why a Fly volume:** SQLite is a file. Fly machines are ephemeral — without a volume, the database is lost on every deploy. A volume persists across deploys and machine restarts. The volume mount path is configured in `fly.toml`.

**Database path resolution:** Follow the same pattern as `WebConfig` — check config key `Auth:DatabasePath`, then env var `AUTH_DATABASE_PATH`, then fall back to the default. In development the file lives in the project's `Data/` directory (gitignored).

### 3. Razor Pages for login/register, not Blazor components

**Choice:** Implement `/Account/Login` and `/Account/Register` as traditional ASP.NET Core Razor Pages (`@page`/`@model` with `PageModel`, HTTP POST form submission), not as Blazor interactive components.

**Why:** Blazor Server runs over a SignalR WebSocket. Setting an authentication cookie requires an HTTP response, which only happens on the initial page load — not on subsequent Blazor interactions within the circuit. The official Microsoft guidance for Blazor Server auth is to use Razor Pages (or minimal API endpoints) for the login/logout POST, then let `RevalidatingServerAuthenticationStateProvider` propagate the auth state into the Blazor circuit. Trying to set cookies from within a Blazor component requires `NavigationManager.NavigateTo(..., forceLoad: true)` hacks that are fragile.

`/Account/Logout` is a POST-only Razor Page (CSRF-safe form post from the nav).

### 4. Auth middleware pipeline ordering

Insert auth middleware between `UseAntiforgery()` and `MapStaticAssets()`:

```
UseStatusCodePagesWithReExecute(...)
UseAntiforgery()
UseAuthentication()          ← new
UseAuthorization()           ← new
MapStaticAssets()
MapRazorComponents<App>()
```

`MapStaticAssets()` serves CSS/JS/images and must remain accessible without auth (the login page needs them). Static assets don't go through the authorization middleware because `MapStaticAssets` short-circuits before `UseAuthorization` in the pipeline. Razor Pages for login/register are decorated with `[AllowAnonymous]`. Everything else requires auth via a global fallback policy.

### 5. Blazor auth integration

- Wrap `<Routes>` in `<CascadingAuthenticationState>` in `App.razor` (or `Routes.razor`).
- Replace `<RouteView>` with `<AuthorizeRouteView>` in `Routes.razor`, with a `<NotAuthorized>` block that redirects to login.
- Add `@using Microsoft.AspNetCore.Components.Authorization` to `_Imports.razor`.
- Register `AddAuthorizationCore()` and the `RevalidatingServerAuthenticationStateProvider` subclass.

### 6. Rate limiting with ASP.NET Core built-in middleware

**Choice:** Use `Microsoft.AspNetCore.RateLimiting` with `AddFixedWindowLimiter`, applied selectively to the login and registration Razor Pages via named policies.

**Configuration:**
- Registration: 5 requests per IP per hour.
- Login: 10 requests per IP per minute.

These are sensible defaults for a small-user app. The rate limiter uses the in-memory `FixedWindowRateLimiter` — no Redis/external state needed. Rate limit state is lost on restart, which is acceptable.

### 7. Identity configuration

```
SignIn.RequireConfirmedAccount  = false
SignIn.RequireConfirmedEmail    = false
User.RequireUniqueEmail         = false
User.AllowedUserNameCharacters  = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._"
Password.RequiredLength         = 6
Password.RequireDigit           = true
Password.RequireNonAlphanumeric = true
Password.RequireUppercase       = false
Password.RequireLowercase       = false
Lockout.MaxFailedAccessAttempts = 5
Lockout.DefaultLockoutTimeSpan  = 15 minutes
Lockout.AllowedForNewUsers      = true
Cookie.HttpOnly                 = true
Cookie.SameSite                 = Strict
Cookie.SecurePolicy             = Always (Fly terminates TLS)
Cookie.ExpireTimeSpan           = 14 days
Cookie.SlidingExpiration        = true
```

### 8. Database initialization

Create the Identity schema (`AspNetUsers`, `AspNetRoles`, etc.) at startup via `dbContext.Database.EnsureCreated()`. This creates all tables from the current EF Core model without requiring migration files. For a single-instance SQLite app with few users, startup initialization is safe and avoids a separate migration step in the deploy pipeline. When schema changes are needed later (e.g., adding `TenantId` to `AppUser`), switch to EF Core migrations.

### 9. UI language

The login and registration pages use German to match the existing UI (`Anmelden`, `Registrieren`, `Benutzername`, `Passwort`). Error messages are also in German.

## Risks / Trade-offs

- **Single-instance constraint** → SQLite doesn't support concurrent writers from multiple processes. If the Web app scales to multiple Fly machines, SQLite must be replaced (e.g., with Postgres or DynamoDB). Acceptable for now; the proposal explicitly scopes this to single-instance.
- **Startup database creation** → `Database.EnsureCreated()` at startup adds latency to the first request after a deploy. For a small Identity schema on SQLite this is negligible (<100ms). Note: `EnsureCreated()` is a no-op when the database already exists.
- **No password reset** → If a user forgets their password, there's no self-service recovery. The admin must manually reset it (e.g., via a CLI tool or direct DB edit). Acceptable for few users; a reset flow can be added later.
- **Rate limit state in memory** → If the machine restarts, rate limit counters reset. An attacker could abuse this by waiting for autostop. The account lockout (Identity-level, persisted in SQLite) is the durable defense; rate limiting is a supplementary layer.

## Migration Plan

1. Add Fly volume: `fly volumes create data --region fra --size 1 --app ninjago-scanner-web`
2. Update `fly.toml` to mount the volume at `/data`.
3. Deploy. On first startup, `Database.EnsureCreated()` creates the SQLite file and Identity tables.
4. Register the first user account via the self-registration page.
5. Rollback: remove auth middleware and Identity registration from `Program.cs`, redeploy. The SQLite file stays on the volume but is inert.
