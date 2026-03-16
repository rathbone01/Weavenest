using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<bool> UsernameExistsAsync(string username);
    Task<User> CreateUserAsync(string username, string passwordHash);
}
