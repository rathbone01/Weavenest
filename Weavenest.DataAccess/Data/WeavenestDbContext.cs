using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Data;

public class WeavenestDbContext : DbContext
{
    public WeavenestDbContext(DbContextOptions<WeavenestDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ChatSession> Sessions => Set<ChatSession>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<Folder> Folders => Set<Folder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeavenestDbContext).Assembly);
    }
}
