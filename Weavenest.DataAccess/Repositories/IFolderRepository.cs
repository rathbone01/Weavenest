using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IFolderRepository
{
    Task<Folder> CreateFolderAsync(Guid userId, string name);
    Task<Folder?> GetFolderByIdAsync(Guid userId, Guid folderId);
    Task<IReadOnlyList<Folder>> GetFoldersByUserAsync(Guid userId);
    Task<IReadOnlyList<Folder>> GetFoldersByUserSortedByActivityAsync(Guid userId);
    Task<Folder> RenameFolderAsync(Guid userId, Guid folderId, string newName);
    Task<bool> DeleteFolderAsync(Guid userId, Guid folderId);
}
