namespace Weavenest.Services.Interfaces;

public interface IUserIdentityService
{
    Task<Guid?> GetCurrentUserIdAsync();
    Task<string?> GetCurrentUserNameAsync();
}
