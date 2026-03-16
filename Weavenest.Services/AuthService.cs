using Weavenest.DataAccess.Repositories;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class AuthService(IUserRepository userRepository, TokenService tokenService) : IAuthService
{
    public async Task<string?> LoginAsync(string username, string password)
    {
        var hashedPassword = HashHelper.Hash(password);
        var user = await userRepository.GetByUsernameAsync(username);

        if (user is null || user.PasswordHash != hashedPassword)
            return null;

        return tokenService.GenerateToken(user);
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "Username is required.");

        if (string.IsNullOrWhiteSpace(password))
            return (false, "Password is required.");

        if (await userRepository.UsernameExistsAsync(username))
            return (false, "Username is already taken.");

        var hashedPassword = HashHelper.Hash(password);
        await userRepository.CreateUserAsync(username, hashedPassword);
        return (true, null);
    }
}
