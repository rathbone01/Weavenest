using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public class EfFolderRepository(IDbContextFactory<WeavenestDbContext> contextFactory) : IFolderRepository
{
    public async Task<Folder> CreateFolderAsync(Guid userId, string name)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var folder = new Folder
        {
            Name = name,
            UserId = userId
        };

        context.Folders.Add(folder);
        await context.SaveChangesAsync();
        return folder;
    }

    public async Task<Folder?> GetFolderByIdAsync(Guid userId, Guid folderId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Folders
            .Include(f => f.Sessions)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersByUserAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersByUserSortedByActivityAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Folders
            .Where(f => f.UserId == userId)
            .Include(f => f.Sessions)
            .OrderByDescending(f => f.Sessions.Max(s => (DateTime?)s.UpdatedAt) ?? f.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Folder> RenameFolderAsync(Guid userId, Guid folderId, string newName)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId)
            ?? throw new KeyNotFoundException($"Folder {folderId} not found");

        folder.Name = newName;
        folder.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return folder;
    }

    public async Task<bool> DeleteFolderAsync(Guid userId, Guid folderId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);
        if (folder is null)
            return false;

        context.Folders.Remove(folder);
        await context.SaveChangesAsync();
        return true;
    }
}
