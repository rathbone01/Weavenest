using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class CustomAuthenticationStateProvider(
    TokenService tokenService,
    ILocalStorageService localStorageService) : AuthenticationStateProvider
{
    private readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await localStorageService.GetItem<string>("token");
        if (string.IsNullOrEmpty(token))
            return new AuthenticationState(_anonymous);

        var principal = tokenService.ParseToken(token);
        if (principal.Identity?.IsAuthenticated != true)
        {
            await localStorageService.RemoveItem("token");
            return new AuthenticationState(_anonymous);
        }

        return new AuthenticationState(principal);
    }

    public async Task Login(string token)
    {
        await localStorageService.SetItem("token", token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task Logout()
    {
        await localStorageService.RemoveItem("token");
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
