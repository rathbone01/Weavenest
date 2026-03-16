using Microsoft.AspNetCore.Components.Authorization;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class UserIdentityService(AuthenticationStateProvider authenticationStateProvider) : IUserIdentityService
{
    public async Task<Guid?> GetCurrentUserIdAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var userIdClaim = user.FindFirst(c => c.Type.Contains("nameidentifier"))?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
            return userId;

        return null;
    }

    public async Task<string?> GetCurrentUserNameAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.Name;
    }
}
