using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NinjagoScanner.Web.Pages.Account;

// AllowAnonymous is required here, not just conventional: this is ASP.NET Core Identity's
// default AccessDeniedPath target. Without it, this page would itself be subject to the
// FallbackPolicy (RequireAuthenticatedUser + CollectionOwnerRequirement) like any other page,
// so a user who is authenticated but owns no collection would be denied access to the very
// page meant to explain that - redirecting back to AccessDeniedPath again, forever, each hop
// nesting the ReturnUrl query parameter until the URL exceeds Kestrel's request-line limit.
[AllowAnonymous]
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
    }
}
