## Why

The Web app is publicly accessible with no authentication. Anyone with the URL can view, upload, and modify the card collection. Adding a login gate protects the data and establishes a user identity model that future tenant/multi-collection work can build on.

## What Changes

- Add ASP.NET Core Identity with a stripped-down configuration (no email requirement, no 2FA, no email confirmation) to the Web project.
- Store user accounts in a SQLite database via EF Core, persisted on a Fly volume.
- Add a login page and a self-registration page (username + password, no email required).
- Gate all existing pages behind authentication — unauthenticated visitors are redirected to login.
- Add rate limiting on the login and registration endpoints to mitigate brute-force and spam.
- Add a logout button to the navigation.

## Capabilities

### New Capabilities
- `web-user-authentication`: Username/password login, self-registration, session management via cookie auth, and route-level authorization gating all pages.
- `web-auth-rate-limiting`: Rate limiting on login and registration endpoints to prevent brute-force and spam registrations.

### Modified Capabilities

_(none — existing page behavior is unchanged once authenticated)_

## Impact

- **NinjagoScanner.Web**: New NuGet dependencies (Identity, EF Core, SQLite provider). New `AppDbContext`, `AppUser` model, login/register pages, changes to `Program.cs` (auth middleware pipeline), `Routes.razor` (AuthorizeRouteView), `NavMenu.razor` (logout button). EF Core migrations for Identity schema.
- **Fly infrastructure**: Web app needs a persistent volume mounted for the SQLite database file. Single-instance deployment assumed.
- **CatalogService / PictureService**: No changes — already behind Fly's private network (6PN).
