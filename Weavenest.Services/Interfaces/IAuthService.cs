namespace Weavenest.Services.Interfaces;

public interface IAuthService
{
    Task<(string? Token, string? Error)> LoginAsync(string username, string password);
    Task<(bool Success, string? Error)> RegisterAsync(string username, string password);
    Task<(bool Success, string? Error)> ChangePasswordAsync(Guid userId, string oldPassword, string newPassword);
}
