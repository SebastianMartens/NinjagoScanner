using Microsoft.AspNetCore.Authorization;

namespace NinjagoScanner.Web.Data;

/// <summary>
/// Layered on top of the existing RequireAuthenticatedUser() fallback policy: per
/// web-collections' "Every page and action requires the Owner role on the current collection"
/// requirement, an authenticated user with no Owner CollectionMembership is still denied.
/// context.User is populated correctly whether this runs via ASP.NET Core's authorization
/// middleware (the initial page request) or Blazor's AuthorizeRouteView (subsequent in-circuit
/// navigation) - both call IAuthorizationService with the current ClaimsPrincipal.
/// </summary>
internal sealed class CollectionOwnerRequirement : IAuthorizationRequirement
{
}

internal sealed class CollectionOwnerHandler(ICurrentCollectionContext currentCollectionContext)
    : AuthorizationHandler<CollectionOwnerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CollectionOwnerRequirement requirement)
    {
        var collection = await currentCollectionContext.GetOwnedCollectionAsync(context.User);
        if (collection is not null)
        {
            context.Succeed(requirement);
        }
    }
}
