using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>Always reports the same authenticated test user, for constructing scoped services outside a real Blazor circuit.</summary>
internal sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestAuth");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
