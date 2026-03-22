using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByIdAsync(Guid userId);
    Task<bool> UsernameExistsAsync(string username);
    Task<User> CreateUserAsync(string username, string passwordHash, byte[] encryptionSalt);
    Task UpdateUserPromptAsync(Guid userId, string? userPrompt);
    Task UpdatePasswordAsync(Guid userId, string newPasswordHash, byte[] newEncryptionSalt);
    Task UpdatePasswordHashAsync(Guid userId, string newPasswordHash, byte[] encryptionSalt);
}
